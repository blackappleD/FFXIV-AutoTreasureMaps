using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int CofferSpawnWaitMs = 10_000;
    private const int CofferOpenAttempts = 3;
    private const int CofferOpenConfirmMs = 6_000;
    private const int CofferRetryDelayMs = 1_500;
    private const int UnlockAttempts = 4;
    private const int UnlockAnswerTimeoutMs = 5_000;
    // A coffer also fades when the hunt runs out, and the game's word for that can trail the fade by a moment.
    private const int FadeExpiryGraceMs = 2_000;
    // A coffer still sealed after this many fights back has guards the run cannot find.
    private const int MaxFightRounds = 3;
    // The treasure hunt's own limit is about five minutes; a fight cleanup inside it gets the rest of that.
    private const int HuntFightCapMs = 300_000;
    private const int LootAppearWaitMs = 5_000;
    private const int LootRollResendMs = 3_000;
    // A roll the game has not taken is sent once more, then left to the player, so a refused roll is not repeated.
    private const int MaxRollRounds = 2;
    private const int LootSettleQuietMs = 2_000;
    private const int LootSettleCapMs = 15_000;
    private const int LootPollFrames = 10;
    private const int MaxOtherChests = 16;
    private const string NeedGreedAddon = "NeedGreed";

    private readonly uint[] chestsBeforeUnlock = new uint[MaxOtherChests];
    private int chestsBeforeUnlockCount;

    private enum CofferStep : byte { Opened, NotFound, Unreachable, PromptMismatch, Cancelled }

    private enum UnlockStep : byte { Unlocked, Sealed, TimedOut, KnockedOut, Gone, Unreachable, Cancelled }

    private static ReadOnlySpan<uint> CofferOpenedMessages =>
    [
        TreasureLogMessages.TrapSprung,
        TreasureLogMessages.TrapSprungSecondRow,
        TreasureLogMessages.TrapSprungThirdRow,
        TreasureLogMessages.MapCrumbled,
        TreasureLogMessages.MapCrumbledSecondRow,
    ];

    private static ReadOnlySpan<uint> UnlockReplies =>
    [
        TreasureLogMessages.CofferUnlocked,
        TreasureLogMessages.CofferSealed,
        TreasureLogMessages.CofferLocked,
        TreasureLogMessages.HuntTimeExpired,
    ];

    // Finds the coffer the dig turned up, walks into reach and answers the game's warning that opening it costs the map.
    // The bags are counted just before that answer, since the loot is measured from there.
    private async Task<CofferStep> OpenCoffer(MapInProgress map, bool fresh)
    {
        EnterStep(TreasurePhase.Opening);
        Status = Loc.T(L.Status.LookingForCoffer);
        var spotPosition = map.Spot.Position;
        ulong[] skip = fresh ? coffersBeforeDig.AsSpan(0, coffersBeforeDigCount).ToArray() : [];
        await WaitUntilTimed(() => TreasureCoffers.FindNearest(spotPosition, CofferSearchMeters, skip) is not null, CofferSpawnWaitMs, "coffer spawn");
        var coffer = TreasureCoffers.FindNearest(spotPosition, CofferSearchMeters, skip);
        if (coffer is null && skip.Length > 0)
        {
            coffer = TreasureCoffers.FindNearest(spotPosition, CofferSearchMeters, []);
            if (coffer is not null)
            {
                Warn("Coffer: no new coffer appeared after the dig; trying the nearest one, which was there before it.");
            }
        }

        if (coffer is null)
        {
            Warn($"Coffer: no treasure hunt coffer within {CofferSearchMeters:F0}m of the spot after the dig.");
            return CancelToken.IsCancellationRequested ? CofferStep.Cancelled : CofferStep.NotFound;
        }

        map.CofferId = coffer.GameObjectId;
        map.CofferEntityId = coffer.EntityId;
        map.CofferPosition = coffer.Position;
        Diag($"Coffer: {TreasureCoffers.Describe(map.CofferId)} at {FormatPosition(coffer.Position)}, {System.Numerics.Vector3.Distance(coffer.Position, spotPosition):F1}m from the Level point{(fresh && skip.Length > 0 ? $", {skip.Length} other coffer(s) skipped" : string.Empty)}.");

        for (var attempt = 1; attempt <= CofferOpenAttempts; attempt++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return CofferStep.Cancelled;
            }

            var step = await TryOpenCoffer(map, attempt);
            if (step is not CofferStep.NotFound)
            {
                return step;
            }

            await DelayMs(CofferRetryDelayMs);
        }

        Warn($"Coffer: could not open it after {CofferOpenAttempts} attempts.");
        return CofferStep.Unreachable;
    }

    // NotFound here means "not opened yet, try again".
    private async Task<CofferStep> TryOpenCoffer(MapInProgress map, int attempt)
    {
        const string scope = "map-coffer";
        Status = Loc.T(L.Status.OpeningCoffer);
        await FightOffAttackers(scope);
        var target = await ApproachObject(() => Svc.Objects.SearchById(map.CofferId), $"coffer {map.CofferId:X}", scope);
        if (target is null || !await ReadyToInteract(scope))
        {
            Diag($"{scope}: attempt {attempt} could not reach the coffer.");
            return CofferStep.NotFound;
        }

        LogOffsetFrom("Coffer", map.Spot.Position);
        if (!await HumanDelay(HumanAction.OpenCoffer, scope))
        {
            return CofferStep.Cancelled;
        }

        var since = TreasureMessageWatcher.Now;
        await InteractWithObject(target, () => NpcInteraction.SelectYesnoOpen() || CofferOpenedSince(since) || NotOurCoffer(since), scope);
        if (NotOurCoffer(since))
        {
            Warn($"{scope}: the game says {TreasureCoffers.Describe(map.CofferId)} belongs to another player; leaving it.");
            return CofferStep.Unreachable;
        }

        if (!CofferOpenedSince(since))
        {
            if (!NpcInteraction.SelectYesnoOpen())
            {
                return CofferStep.NotFound;
            }

            var prompt = NpcInteraction.SelectYesnoText();
            if (!DigOps.IsCofferPrompt(prompt))
            {
                Warn($"{scope}: the confirmation '{prompt}' is not the coffer's; declining it.");
                NpcInteraction.AnswerNo();
                return CofferStep.PromptMismatch;
            }

            map.BagsBefore = BagSnapshot.Capture();
            map.CofferOpenedAtMs = TreasureMessageWatcher.Now;
            Diag($"{scope}: the coffer asks '{prompt}'; answering Yes.");
            DialogDriver.Confirm();
        }
        else
        {
            map.BagsBefore ??= BagSnapshot.Capture();
            map.CofferOpenedAtMs = since;
        }

        var openedAt = map.CofferOpenedAtMs;
        var catalog = Plugin.Instance.Catalog;
        await WaitUntilTimed(() => CofferOpenedSince(openedAt) || !HeldMapReader.TryRead(catalog, out _), CofferOpenConfirmMs, "coffer opened", MessagePollFrames);
        // The Yes has gone out and the map is as good as spent, so a stop now keeps the coffer as opened for the resume.
        if (CancelToken.IsCancellationRequested)
        {
            return CofferStep.Cancelled;
        }

        if (!CofferOpenedSince(openedAt) && HeldMapReader.TryRead(catalog, out _))
        {
            Diag($"{scope}: the map is still held and no trap sprang, so the coffer did not open.");
            map.CofferOpenedAtMs = 0;
            map.BagsBefore = null;
            return CofferStep.NotFound;
        }

        ReadHuntClock();
        var trap = Messages.TryGetLatest(CofferOpenedMessages, openedAt, out var message) ? TreasureLogMessages.NameOf(message.Id) : "no message yet";
        Diag($"{scope}: the coffer opened ({trap}); {TreasureCoffers.Describe(map.CofferId)}.");
        return CofferStep.Opened;
    }

    private static bool CofferOpenedSince(long since) => Messages.TryGetLatest(CofferOpenedMessages, since, out _);

    private static bool NotOurCoffer(long since) => Messages.ArrivedSince(TreasureLogMessages.CofferOfAnotherPlayer, since);

    // The fight and the unlock alternate: a coffer that answers that it is still sealed has guards left, so the fight
    // goes on, a bounded number of times.
    private async Task<MapResult> FinishHunt(MapInProgress map)
    {
        for (var round = 1; round <= MaxFightRounds; round++)
        {
            var fight = await FightGuards(map);
            switch (fight)
            {
                case FightEnd.TimedOut:
                    return Conclude(map, map.Tier, MapOutcome.TimedOut);
                case FightEnd.Died:
                    return Conclude(map, map.Tier, MapOutcome.Died);
                case FightEnd.Cancelled:
                    return Conclude(map, map.Tier, MapOutcome.Cancelled);
            }

            var unlock = await UnlockCoffer(map, GuardsClearedAt(map));
            switch (unlock)
            {
                case UnlockStep.Unlocked:
                    await LootCoffer(map);
                    return Conclude(map, map.Tier, CancelToken.IsCancellationRequested ? MapOutcome.Cancelled : MapOutcome.Looted);
                case UnlockStep.Sealed:
                    Diag($"Coffer: still sealed after fight round {round}; back to the guards.");
                    continue;
                case UnlockStep.TimedOut:
                    return Conclude(map, map.Tier, MapOutcome.TimedOut);
                case UnlockStep.KnockedOut:
                    return Conclude(map, map.Tier, MapOutcome.Died);
                case UnlockStep.Cancelled:
                    return Conclude(map, map.Tier, MapOutcome.Cancelled);
                default:
                    return Conclude(map, map.Tier, MapOutcome.Unreachable);
            }
        }

        Warn($"Coffer: still sealed after {MaxFightRounds} fights.");
        return Conclude(map, map.Tier, MapOutcome.Unreachable);
    }

    // Some clients answer the first interaction, the one that springs the trap, with the unlock message as well, so only
    // an unlock that came once the guards were cleared means the coffer will open.
    private static long GuardsClearedAt(MapInProgress map)
        => Messages.TryGetLatest(TreasureLogMessages.GuardsDefeated, map.CofferOpenedAtMs, out var cleared) ? cleared.AtMs : TreasureMessageWatcher.Now;

    private async Task<UnlockStep> UnlockCoffer(MapInProgress map, long clearedAtMs)
    {
        const string scope = "map-unlock";
        EnterStep(TreasurePhase.Looting);
        for (var attempt = 1; attempt <= UnlockAttempts; attempt++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return UnlockStep.Cancelled;
            }

            if (Messages.ArrivedSince(TreasureLogMessages.CofferUnlocked, clearedAtMs))
            {
                return UnlockStep.Unlocked;
            }

            if (Messages.ArrivedSince(TreasureLogMessages.HuntTimeExpired, map.CofferOpenedAtMs))
            {
                return UnlockStep.TimedOut;
            }

            // A guard or an add can still knock the character out after the fight, and nothing is reached while down.
            if (IsKnockedOut())
            {
                if (!await RecoverInHunt(map))
                {
                    return CancelToken.IsCancellationRequested ? UnlockStep.Cancelled : UnlockStep.KnockedOut;
                }

                EnterStep(TreasurePhase.Looting);
            }

            if (TreasureCoffers.Gone(map.CofferId))
            {
                Warn($"{scope}: the coffer is gone before its lock opened.");
                return UnlockStep.Gone;
            }

            Status = Loc.T(L.Status.OpeningCofferLock);
            ReadHuntClock();
            await FightOffAttackers(scope, HuntFightCapMs);
            var target = await ApproachObject(() => Svc.Objects.SearchById(map.CofferId), $"coffer {map.CofferId:X}", scope);
            if (target is null || !await ReadyToInteract(scope))
            {
                await DelayMs(CofferRetryDelayMs);
                continue;
            }

            // The coffer's loot can land in the same frame as its unlock message, so other chests are noted before the lock opens.
            chestsBeforeUnlockCount = LootRolls.PendingChests(chestsBeforeUnlock);
            var since = TreasureMessageWatcher.Now;
            var bagsAtInteract = BagSnapshot.Fingerprint();
            // A bag change only speaks for this coffer when no other chest's roll could land in the meantime.
            var bagsSpeakForCoffer = chestsBeforeUnlockCount == 0;
            var cofferEntityId = map.CofferEntityId;
            Func<uint, bool> fromCoffer = chest => chest == cofferEntityId;
            bool OpenedForSure() => LootRolls.Pending(fromCoffer) > 0 || (bagsSpeakForCoffer && BagSnapshot.Fingerprint() != bagsAtInteract);
            // The coffer may open without a word: its loot waits on a roll, the gil and items land in the bags, or it fades.
            bool OpenedQuietly() => OpenedForSure() || TreasureCoffers.Gone(map.CofferId);
            await InteractWithObject(target, () => Messages.TryGetLatest(UnlockReplies, since, out _) || NpcInteraction.SelectYesnoOpen() || OpenedQuietly(), scope);
            AnswerUnlockPrompt(scope);
            await WaitUntilTimed(() => Messages.TryGetLatest(UnlockReplies, since, out _) || OpenedQuietly(), UnlockAnswerTimeoutMs, "coffer lock answer", MessagePollFrames);
            if (Messages.TryGetLatest(UnlockReplies, since, out var reply))
            {
                Diag($"{scope}: the coffer answered {TreasureLogMessages.NameOf(reply.Id)} ({TreasureCoffers.Describe(map.CofferId)}).");
                switch (reply.Id)
                {
                    case TreasureLogMessages.CofferUnlocked:
                        return UnlockStep.Unlocked;
                    case TreasureLogMessages.CofferSealed:
                        return UnlockStep.Sealed;
                    case TreasureLogMessages.HuntTimeExpired:
                        return UnlockStep.TimedOut;
                }
            }

            if (OpenedForSure())
            {
                Diag($"{scope}: the coffer opened without an unlock message ({TreasureCoffers.Describe(map.CofferId)}).");
                return UnlockStep.Unlocked;
            }

            if (TreasureCoffers.Gone(map.CofferId))
            {
                var openedAt = map.CofferOpenedAtMs;
                await WaitUntilTimed(() => Messages.ArrivedSince(TreasureLogMessages.HuntTimeExpired, openedAt), FadeExpiryGraceMs, "hunt expiry after the fade", MessagePollFrames);
                if (Messages.ArrivedSince(TreasureLogMessages.HuntTimeExpired, openedAt))
                {
                    Diag($"{scope}: the coffer faded because the hunt ran out.");
                    return UnlockStep.TimedOut;
                }

                Diag($"{scope}: the coffer faded after the interaction with no word that the hunt ran out; taking it as opened.");
                return UnlockStep.Unlocked;
            }

            await DelayMs(CofferRetryDelayMs);
        }

        Warn($"{scope}: the coffer's lock did not open after {UnlockAttempts} attempts.");
        return UnlockStep.Unreachable;
    }

    // Whether the second interaction asks anything is not known yet. The coffer's own warning is answered again, and
    // any other question is only logged and left to the player.
    private void AnswerUnlockPrompt(string scope)
    {
        if (!NpcInteraction.SelectYesnoOpen())
        {
            return;
        }

        var prompt = NpcInteraction.SelectYesnoText();
        if (DigOps.IsCofferPrompt(prompt))
        {
            Diag($"{scope}: the coffer asks '{prompt}' again; answering Yes.");
            DialogDriver.Confirm();
            return;
        }

        Warn($"{scope}: a confirmation '{prompt}' opened at the coffer; leaving it to you.");
    }

    // Rolls the coffer's rolled items by the Loot setting, then waits until the bags stop changing, so the gains are
    // measured once everything has arrived. Items of a chest that was already waiting before the unlock are left alone.
    private async Task LootCoffer(MapInProgress map)
    {
        Status = Loc.T(L.Status.LootingCoffer);
        var otherChests = chestsBeforeUnlock;
        var otherCount = chestsBeforeUnlockCount;
        var chestEntityId = map.CofferEntityId;
        Func<uint, bool> fromCoffer = chest => chest == chestEntityId || Array.IndexOf(otherChests, chest, 0, otherCount) < 0;
        var choice = Plugin.Instance.Configuration.LootRoll;

        await WaitUntilTimed(() => LootRolls.Pending(fromCoffer) > 0 || NpcInteraction.IsAddonReady(NeedGreedAddon), LootAppearWaitMs, "loot window", LootPollFrames);
        NotePortal(map);
        var pending = LootRolls.Pending(fromCoffer);
        Diag($"Loot: {pending} item(s) to roll from coffer entity {chestEntityId:X}, window {(NpcInteraction.IsAddonReady(NeedGreedAddon) ? "open" : "closed")}, roll setting {choice}; {LootRolls.Describe()}.");
        if (pending > 0 && choice == LootRoll.Manual)
        {
            Diag("Loot: the rolls are left to you.");
        }
        else if (pending > 0 && !LootRolls.Available)
        {
            Warn("Loot: rolling is not available in this game version; the rolls are left to you.");
        }

        var nextRollAt = 0L;
        var rollRounds = 0;
        var lastFingerprint = BagSnapshot.Fingerprint();
        var quietSince = Environment.TickCount64;
        var deadline = Environment.TickCount64 + LootSettleCapMs;
        while (!CancelToken.IsCancellationRequested && Environment.TickCount64 < deadline)
        {
            var now = Environment.TickCount64;
            ReadHuntClock();
            pending = LootRolls.Pending(fromCoffer);
            if (pending > 0 && choice != LootRoll.Manual && rollRounds < MaxRollRounds && now >= nextRollAt)
            {
                LootRolls.Roll(choice, fromCoffer);
                rollRounds++;
                nextRollAt = now + LootRollResendMs;
            }

            var fingerprint = BagSnapshot.Fingerprint();
            if (fingerprint != lastFingerprint)
            {
                lastFingerprint = fingerprint;
                quietSince = now;
            }

            var rollsDone = pending == 0 || choice == LootRoll.Manual || (rollRounds >= MaxRollRounds && now >= nextRollAt);
            if (rollsDone && now - quietSince >= LootSettleQuietMs)
            {
                break;
            }

            await NextFrame(LootPollFrames);
        }

        NotePortal(map);
        Diag($"Loot: settled with {LootRolls.Pending(fromCoffer)} item(s) still waiting on a roll; {LootRolls.Describe()}.");
    }
}
