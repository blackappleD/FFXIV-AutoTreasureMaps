using AutoTreasureMaps.Core.Cleanup;
using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Core.Treasure;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int DungeonTickFrames = 10;
    // A coffer is asked once to call its guards and once to open; a third time covers a missed answer.
    private const int MaxCofferInteractions = 3;
    private const int CofferAnswerMs = 6_000;
    // The field may sit quiet between a coffer and the next round's objects; this long with nothing to do ends the run.
    private const int DungeonIdleLimitMs = 25_000;
    // The last round's coffer can show a moment after its enemies fall, so leaving waits for the field to stay quiet.
    private const int LeaveQuietMs = 5_000;
    private const int DungeonFieldQuietMs = 3_000;
    private const int DungeonFightCapMs = 600_000;
    private const int LureClickAttempts = 3;
    private const int LureCloseWaitMs = 3_000;
    // Nobody may be there to guess: the game waits about five minutes for a guess before it takes the treasure away.
    private const int LureQuietLimitMs = 360_000;
    // Each challenge is taken before the first card shows, so it is won 7 times in 9 whatever the card turns out to
    // be. A coffer with rare loot is challenged once: the treasure is kept 78% of the time, and a second challenge
    // would keep it only 60% of the time. Any other coffer is challenged twice and taken at 3x: 1.82x on average with
    // the treasure lost 40% of the time, against 1.88x and 53% for a third challenge.
    private const int LureChallengesWithRareLoot = 1;
    private const int LureChallengesPlain = 2;
    // The game takes the treasure away about five minutes after the cards are dealt if no one guesses. With the card
    // dealt face up readable, the run guesses on it at once; when it cannot be read, a guess not made by the player
    // within this long is made by a coin toss: an even chance beats losing the treasure for sure.
    private const int LureGuessWaitMs = 60_000;
    // The middle card: 1 to 4 guess high, 6 to 9 guess low, and a 5 wins either way half the time.
    private const int LureMiddleCard = 5;
    private const int LureGuessAnswerMs = 10_000;
    private const int LureCardReadMs = 3_000;
    private const int BonusRoundCapMs = 120_000;
    private const float SackReachMeters = 0.6f;
    private const int SackWalkMs = 10_000;
    // Standing on a sack picks it up at once; one still there after this is asked by interacting instead.
    private const int SackPickupMs = 1_200;
    private const int LootPendingWaitMs = 60_000;
    private const int LootRetryMs = 20_000;
    private const int LeaveZoneWaitMs = 15_000;
    private const int AdvanceRetryLimit = 3;
    private const int AdvanceRetryDelayMs = 5_000;

    private enum DungeonStep : byte { Lure, Bonus, Fight, Loot, Coffer, Advance, Leave, Idle }

    // Set when the last RunDungeon ended on an unexpected error, for the caller to hand to the session's fault handling.
    private protected Exception? DungeonFault { get; private set; }

    // Set once the run had to guess a lure's cards itself: no one is at the keyboard, so the coffers that follow in this
    // run are taken as they are rather than challenged. Cleared when the player guesses one themselves.
    private bool lureUnattended;

    // Set while the rotations are told to attack only the enemy the settings want killed first.
    private bool priorityTargeting;

    // Clears the treasure dungeon the character stands in, or picks the session's dungeon up where a pause or a fault
    // left it. Every step reads the field first, so the run carries on wherever the dungeon stands.
    // The caller has found the handler for the territory the character stands in.
    private protected async Task<DungeonResult> RunDungeon(TreasureSession session, TreasureProgress progress, TreasureDungeonHandler handler)
    {
        DungeonFault = null;
        var dungeon = session.ActiveDungeon is { } kept && kept.Profile.Key == handler.Profile.Key ? kept : new DungeonInProgress(handler.Profile);
        session.ActiveDungeon = dungeon;
        dungeon.BagsBefore ??= BagSnapshot.Capture();
        mapProgress = progress;
        var context = new DungeonContext(this, dungeon);
        DungeonOutcome outcome;
        try
        {
            outcome = await PlayDungeon(handler, context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && !CancelToken.IsCancellationRequested)
        {
            DungeonFault = exception;
            outcome = DungeonOutcome.Faulted;
        }
        finally
        {
            mapProgress = null;
        }

        if (CancelToken.IsCancellationRequested)
        {
            outcome = DungeonOutcome.Cancelled;
        }

        var (gil, items) = BagSnapshot.Gains(dungeon.BagsBefore, BagSnapshot.Capture());
        var result = new DungeonResult(dungeon.Profile, outcome, dungeon.RoundReached, gil, items, TimeSpan.FromMilliseconds(Environment.TickCount64 - dungeon.StartedAtMs));
        if (!DungeonOutcomes.Final(outcome))
        {
            Diag($"Dungeon: {dungeon.Profile.Key} {outcome} in round {dungeon.Round}; the session keeps it for a resume.");
            return result;
        }

        session.ActiveDungeon = null;
        session.RecordDungeon(result);
        LootCleanup.Note(dungeon.BagsBefore);
        Diag($"Dungeon: {dungeon.Profile.Key} ended {outcome} in round {dungeon.Round} of {dungeon.Profile.Rounds} (reached {dungeon.RoundReached}), {gil} gil, {result.ItemCount} items in {items.Length} stacks, {result.Duration.TotalSeconds:F0}s.");
        AnnounceDungeon(result);
        return result;
    }

    private async Task<DungeonOutcome> PlayDungeon(TreasureDungeonHandler handler, DungeonContext context)
    {
        var dungeon = context.Dungeon;
        var profile = dungeon.Profile;
        Diag($"Dungeon: {profile.Key} (territory {profile.TerritoryId}, {profile.Kind}, {profile.Rounds} rounds, round {dungeon.Round} now){(profile.Verified ? string.Empty : ", not yet checked in game")}.");
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatStarted, TerritoryNames.Of(profile.TerritoryId))}");
        if (!profile.Verified)
        {
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatUnverified)}");
        }

        await HoldCombatMovementAndSettle("dungeon");
        var idleSince = 0L;
        var advanceFailures = 0;
        var lastStep = DungeonStep.Idle;
        var lootFingerprint = 0L;
        while (true)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return DungeonOutcome.Cancelled;
            }

            if (context.Zoning)
            {
                await NextFrame(DungeonTickFrames);
                continue;
            }

            if (!context.InDungeon)
            {
                return HowItEnded(dungeon);
            }

            lootFingerprint = PreviewDungeonLoot(dungeon, lootFingerprint);

            if (Svc.Party.Length > 0)
            {
                Warn("Dungeon: the character is in a party; the run only clears dungeons solo.");
                return DungeonOutcome.InParty;
            }

            if (IsKnockedOut())
            {
                // Solo there is no raise coming, and the return takes the character out of the dungeon.
                Diag($"Dungeon: knocked out in round {dungeon.Round}.");
                await RecoverFromKnockout();
                return CancelToken.IsCancellationRequested ? DungeonOutcome.Cancelled : DungeonOutcome.Died;
            }

            var step = Decide(handler, context, idleSince);
            if (step != lastStep)
            {
                Diag($"Dungeon: round {dungeon.Round}, next step {step}.");
                lastStep = step;
            }

            if (step != DungeonStep.Idle)
            {
                idleSince = 0;
                dungeon.LastBusyAtMs = Environment.TickCount64;
            }

            if (step is DungeonStep.Lure or DungeonStep.Bonus or DungeonStep.Fight or DungeonStep.Loot or DungeonStep.Coffer)
            {
                dungeon.NoteRoundReached();
            }

            switch (step)
            {
                case DungeonStep.Lure:
                    await handler.AnswerLure(context);
                    break;
                case DungeonStep.Bonus:
                    if (CombatPluginCollectsSacks(profile))
                    {
                        await LeaveBonusRoundToCombatPlugin(context);
                    }
                    else if (profile.HasSacks)
                    {
                        await CollectBonusSacks(context);
                    }
                    else
                    {
                        await WaitOutBonusRound(context);
                    }

                    break;
                case DungeonStep.Fight:
                    await handler.Fight(context);
                    break;
                case DungeonStep.Loot:
                    await handler.SettleLoot(context);
                    break;
                case DungeonStep.Coffer:
                    if (DungeonField.NearestCoffer(dungeon, MaxCofferInteractions) is { } coffer)
                    {
                        dungeon.NoteCofferInteraction(coffer.GameObjectId);
                        await handler.OpenCoffer(context, coffer);
                    }

                    break;
                case DungeonStep.Advance:
                    await TopUpConsumables();
                    var advanced = await AdvanceDungeon(handler, context);
                    if (advanced is AdvanceResult.Expelled)
                    {
                        await WaitUntilTimed(() => !context.InDungeon, LeaveZoneWaitMs, "expelled from the dungeon");
                        return context.InDungeon ? DungeonOutcome.Stuck : DungeonOutcome.Expelled;
                    }

                    advanceFailures = advanced is AdvanceResult.Failed ? advanceFailures + 1 : 0;
                    if (advanceFailures >= AdvanceRetryLimit)
                    {
                        Warn($"Dungeon: the advance failed {advanceFailures} times in a row.");
                        return DungeonOutcome.Stuck;
                    }

                    break;
                case DungeonStep.Leave:
                    if (await handler.Leave(context))
                    {
                        await WaitUntilTimed(() => !context.InDungeon, LeaveZoneWaitMs, "left the dungeon");
                        return context.InDungeon ? DungeonOutcome.Stuck : DungeonOutcome.Cleared;
                    }

                    return DungeonOutcome.Stuck;
                default:
                    var now = Environment.TickCount64;
                    if (idleSince == 0)
                    {
                        idleSince = now;
                    }
                    else if (now - idleSince >= DungeonIdleLimitMs)
                    {
                        Warn($"Dungeon: nothing to act on for {DungeonIdleLimitMs / TimeUnits.MillisecondsPerSecond}s in round {dungeon.Round}; {DescribeField(context)}.");
                        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatStuck)}");
                        return DungeonOutcome.Stuck;
                    }

                    Status = "Waiting for the dungeon";
                    await NextFrame(DungeonTickFrames);
                    break;
            }
        }
    }

    // The loot so far shows in the cleanup list while the dungeon goes on; the bags are compared only when they changed.
    private static long PreviewDungeonLoot(DungeonInProgress dungeon, long seen)
    {
        if (dungeon.BagsBefore is not { } before || !Plugin.Instance.Configuration.AutoDiscard)
        {
            return seen;
        }

        var fingerprint = BagSnapshot.Fingerprint();
        if (fingerprint == seen)
        {
            return seen;
        }

        LootCleanup.ShowPreview(before);
        return fingerprint;
    }

    // In order of what must come first: a dialog that waits on an answer, then anything that attacks, then the loot,
    // then the next coffer, and only then the way on.
    private DungeonStep Decide(TreasureDungeonHandler handler, DungeonContext context, long idleSince)
    {
        var dungeon = context.Dungeon;
        if (LureOngoing(dungeon))
        {
            return DungeonStep.Lure;
        }

        if (BonusRoundOn(dungeon))
        {
            return DungeonStep.Bonus;
        }

        if (Svc.Condition[ConditionFlag.InCombat] || DungeonField.NearestEnemy(out _) is not null)
        {
            return DungeonStep.Fight;
        }

        if (Plugin.Instance.Configuration.LootRoll != LootRoll.Manual
            && Environment.TickCount64 >= dungeon.LootNotBeforeMs
            && LootRolls.Pending(static _ => true) > 0)
        {
            return DungeonStep.Loot;
        }

        if (DungeonField.NearestCoffer(dungeon, MaxCofferInteractions) is not null)
        {
            return DungeonStep.Coffer;
        }

        if (handler.MoreRounds(dungeon))
        {
            return handler.ReadyToAdvance(dungeon) && DungeonField.AdvanceObjects(dungeon.Profile).Count > 0 ? DungeonStep.Advance : DungeonStep.Idle;
        }

        // A last round whose coffer was never opened is left to the idle limit, which hands the dungeon back to the player
        // rather than leaving its treasure behind.
        var quiet = idleSince != 0 && Environment.TickCount64 - idleSince >= LeaveQuietMs;
        return quiet && dungeon.CofferInteractionsThisRound > 0 ? DungeonStep.Leave : DungeonStep.Idle;
    }

    private async Task<AdvanceResult> AdvanceDungeon(TreasureDungeonHandler handler, DungeonContext context)
    {
        EnterStep(TreasurePhase.Travelling);
        var advance = await handler.AdvanceRound(context);
        if (advance.Result is AdvanceResult.NotReady or AdvanceResult.Failed)
        {
            context.Dungeon.AdvanceNotBeforeMs = Environment.TickCount64 + AdvanceRetryDelayMs;
        }

        if (advance.Result != AdvanceResult.NextRound)
        {
            return advance.Result;
        }

        context.Dungeon.BeginRound(advance.NextRound);
        await handler.TransitRound(context, advance);
        return AdvanceResult.NextRound;
    }

    private DungeonOutcome HowItEnded(DungeonInProgress dungeon)
    {
        if (dungeon.LeaveRequested)
        {
            return DungeonOutcome.Cleared;
        }

        var expelled = Messages.TryGetLatest(DungeonLogMessages.Expelled, dungeon.StartedAtMs, out var message);
        Diag($"Dungeon: out of {dungeon.Profile.Key} in round {dungeon.Round} ({(expelled ? DungeonLogMessages.NameOf(message.Id) : "no word from the game")}).");
        return expelled ? DungeonOutcome.Expelled : DungeonOutcome.Left;
    }

    private static bool BonusRoundOn(DungeonInProgress dungeon)
        => Messages.TryGetLatest(DungeonLogMessages.BonusRoundStarted, dungeon.RoundStartedAtMs, out var started)
        && Environment.TickCount64 - started.AtMs < BonusRoundCapMs
        && !Messages.ArrivedSince(DungeonLogMessages.BonusRoundEnded, started.AtMs)
        && !Messages.ArrivedSince(DungeonLogMessages.BonusRoundCoffer, started.AtMs);

    // The combat plugin's own module for the sacks starts on one of them, so asking by their data ids tells whether the
    // loaded edition covers the round.
    private static bool CombatPluginCollectsSacks(DungeonProfile profile)
    {
        foreach (var baseId in profile.GoldSackBaseIds.Concat(profile.SilverSackBaseIds))
        {
            if (BossModIPC.Instance.HasModuleFor(baseId))
            {
                return true;
            }
        }

        return false;
    }

    // The combat plugin's module walks the character from sack to sack and keeps it out of the charges in between, so
    // the run only keeps the preset up and its movement free until the round ends.
    private async Task LeaveBonusRoundToCombatPlugin(DungeonContext context)
    {
        const string scope = "dungeon-sacks";
        var dungeon = context.Dungeon;
        if (!dungeon.BonusNoticeShown)
        {
            dungeon.BonusNoticeShown = true;
            Diag("Dungeon: a bonus round began; the combat plugin's module picks up its sacks.");
        }

        EnterStep(TreasurePhase.Fighting);
        Status = "Bonus round: the combat plugin picks up the sacks";
        EnsureCombatPreset();
        ReleaseCombatMovement(scope);
        var deadline = Environment.TickCount64 + BonusRoundCapMs;
        try
        {
            while (!CancelToken.IsCancellationRequested && Environment.TickCount64 < deadline && context.InDungeon && !context.Zoning && !IsKnockedOut() && BonusRoundOn(dungeon))
            {
                await KeepPresetFighting(scope);
                await NextFrame(DungeonTickFrames);
            }
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
            HoldCombatMovement(scope);
        }

        Diag($"{scope}: the bonus round is over; {DescribeField(context)}.");
    }

    // The bonus round scatters sacks over the room for ninety seconds, each picked up by walking onto it. The run walks
    // from sack to sack until the round ends; one the character stood on without picking it up is asked once by
    // interacting, and one it cannot reach or pick up is left alone.
    private async Task CollectBonusSacks(DungeonContext context)
    {
        const string scope = "dungeon-sacks";
        var dungeon = context.Dungeon;
        if (!dungeon.BonusNoticeShown)
        {
            dungeon.BonusNoticeShown = true;
            Diag("Dungeon: a bonus round began; picking up its sacks.");
        }

        EnterStep(TreasurePhase.Fighting);
        Status = "Bonus round: picking up sacks";
        HoldCombatMovement(scope);
        var skipped = new Dictionary<ulong, Vector3>();
        var picked = 0;
        var deadline = Environment.TickCount64 + BonusRoundCapMs;
        while (!CancelToken.IsCancellationRequested && Environment.TickCount64 < deadline && context.InDungeon && !context.Zoning && !IsKnockedOut() && BonusRoundOn(dungeon))
        {
            // The sacks come a little after the round's message, and in waves.
            var sack = DungeonField.NextSack(dungeon.Profile, skipped);
            if (sack is null)
            {
                await NextFrame(DungeonTickFrames);
                continue;
            }

            var sackId = sack.GameObjectId;
            var position = sack.Position;
            // A sack handed out is moved to a new spot, or gone or untargetable on the way there.
            bool Taken() => Svc.Objects.SearchById(sackId) is not { IsTargetable: true } now || DungeonField.SackMoved(now, position);
            await context.WalkTo(position, SackReachMeters, Taken, SackWalkMs, scope);
            if (Taken())
            {
                picked++;
                continue;
            }

            if (!WithinReach(position, SackReachMeters * 2f))
            {
                Diag($"{scope}: could not reach sack {sackId:X} at ({position.X:F1}, {position.Y:F1}, {position.Z:F1}); leaving it.");
                skipped[sackId] = position;
                continue;
            }

            if (await WaitUntilTimed(Taken, SackPickupMs, "sack picked up"))
            {
                picked++;
                continue;
            }

            Diag($"{scope}: standing on sack {sackId:X} did not pick it up; interacting with it.");
            if (Svc.Objects.SearchById(sackId) is { } still)
            {
                await InteractWithObject(still, Taken, scope);
            }

            if (Taken())
            {
                picked++;
            }
            else
            {
                skipped[sackId] = position;
            }
        }

        Diag($"{scope}: the bonus round is over; {picked} sack(s) picked up, {skipped.Count} left.");
    }

    // A bonus round whose sacks the run does not know is left to the player.
    private async Task WaitOutBonusRound(DungeonContext context)
    {
        var dungeon = context.Dungeon;
        if (!dungeon.BonusNoticeShown)
        {
            dungeon.BonusNoticeShown = true;
            Diag("Dungeon: a bonus round began; it is left to the player.");
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatBonusRound)}");
        }

        Status = "Bonus round: up to you";
        await WaitUntilTimed(() => !BonusRoundOn(dungeon) || !context.InDungeon, BonusRoundCapMs, "bonus round over");
    }

    // Asks the coffer and waits for whatever it answers with: guards, the lure, loot, or simply its fading.
    private async Task DungeonOpenCoffer(DungeonContext context, IGameObject coffer)
    {
        const string scope = "dungeon-coffer";
        EnterStep(TreasurePhase.Opening);
        Status = "Opening the coffer";
        var cofferId = coffer.GameObjectId;
        // The dungeon's own coffers ask their own question (要打开宝箱吗？ rather than the dug-up coffer's), which no
        // sheet row gives; a question that opens right after asking one of them is that coffer's.
        var listedCoffer = coffer.ObjectKind == ObjectKind.EventObj && context.Profile.IsCoffer(coffer.BaseId);
        Diag($"{scope}: {coffer.ObjectKind} base {coffer.BaseId} '{coffer.Name}' ({cofferId:X}), interaction {context.Dungeon.CofferInteractions(cofferId)}.");
        if (!await HumanDelay(HumanAction.OpenCoffer, scope))
        {
            return;
        }

        var since = TreasureMessageWatcher.Now;
        bool Answered() => context.FieldBusy() || NpcInteraction.SelectYesnoOpen() || Svc.Objects.SearchById(cofferId) is not { IsTargetable: true };
        await context.Interact(() => Svc.Objects.SearchById(cofferId), $"coffer {cofferId:X}", Answered, scope);
        // A coffer's question is only answered when it reads as the treasure coffer's own; anything else is the player's.
        if (NpcInteraction.SelectYesnoOpen())
        {
            var prompt = NpcInteraction.SelectYesnoText();
            if (listedCoffer || DigOps.IsCofferPrompt(prompt))
            {
                context.ConfirmPrompt(scope);
            }
            else
            {
                Warn($"{scope}: the coffer asks '{prompt}', which is not the coffer's known question; leaving it to you.");
            }
        }

        await WaitUntilTimed(() => context.FieldBusy() || Svc.Objects.SearchById(cofferId) is not { IsTargetable: true }, CofferAnswerMs, "coffer answer");
        var lure = Messages.TryGetLatest(DungeonLogMessages.LureSet, since, out _) ? ", the lure is set" : string.Empty;
        Diag($"{scope}: after the interaction the coffer is {(Svc.Objects.SearchById(cofferId) is { IsTargetable: true } ? "still targetable" : "gone or untargetable")}{lure}; {DescribeField(context)}.");
    }

    // Fights until nothing hostile is left near and the character has been out of combat for a moment. The preset retargets
    // on its own; an enemy that has not engaged yet is still targeted by hand, so a quiet one is pulled too.
    private async Task FightDungeonField(DungeonContext context)
    {
        const string scope = "dungeon-fight";
        EnterStep(TreasurePhase.Fighting);
        Status = "Fighting";
        if (!Svc.Condition[ConditionFlag.InCombat])
        {
            await HumanDelay(HumanAction.Engage, scope, EngageAtOnce);
        }

        EnsureCombatPreset();
        ReleaseCombatMovement(scope);
        var deadline = Environment.TickCount64 + DungeonFightCapMs;
        var quietSince = 0L;
        try
        {
            while (!CancelToken.IsCancellationRequested && Environment.TickCount64 < deadline)
            {
                if (IsKnockedOut() || context.Zoning || !context.InDungeon || LureOngoing(context.Dungeon))
                {
                    return;
                }

                // The bonus round's enemies only get in the way; its sacks are what it pays out.
                if (context.Profile.HasSacks && BonusRoundOn(context.Dungeon))
                {
                    Diag($"{scope}: a bonus round began; leaving the fight for its sacks.");
                    return;
                }

                var enemy = DungeonField.NearestEnemy(out var enemies);
                var inCombat = Svc.Condition[ConditionFlag.InCombat];
                ShowFight(context.Dungeon.Round, enemies);
                if (enemies == 0 && !inCombat)
                {
                    var now = Environment.TickCount64;
                    if (quietSince == 0)
                    {
                        quietSince = now;
                    }
                    else if (now - quietSince >= DungeonFieldQuietMs)
                    {
                        Diag($"{scope}: the field is clear.");
                        return;
                    }
                }
                else
                {
                    quietSince = 0;
                }

                await KeepPresetFighting(scope);
                if (Svc.Targets.Target is { } target && target.IsDead)
                {
                    Svc.Targets.Target = null;
                }

                if (FocusPriorityEnemy(context, scope))
                {
                    await NextFrame(FightTickFrames);
                    continue;
                }

                if (!inCombat && Svc.Targets.Target is null && enemy is not null)
                {
                    Diag($"{scope}: targeting {enemy.Name} ({enemy.GameObjectId:X}), {enemies} enemy(ies) near.");
                    Svc.Targets.Target = enemy;
                }

                await NextFrame(FightTickFrames);
            }

            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"{scope}: still fighting after {DungeonFightCapMs / TimeUnits.MillisecondsPerSecond}s; looking at the field again.");
            }
        }
        finally
        {
            ReleasePriorityTargeting(scope);
            BossModIPC.Instance.ClearActive();
            HoldCombatMovement(scope);
        }
    }

    // Keeps the character on the enemy the settings want killed first, with the rotations told to attack only that
    // target, and hands targeting back to the rotations once none is left. True while such an enemy is targeted.
    private bool FocusPriorityEnemy(DungeonContext context, string scope)
    {
        var configuration = context.Config;
        var priority = DungeonField.PriorityEnemy(context.Profile, configuration.DungeonPreferBonusEnemies, configuration.DungeonMandragorasInOrder);
        if (priority is null)
        {
            ReleasePriorityTargeting(scope);
            return false;
        }

        if (Svc.Targets.Target?.GameObjectId != priority.GameObjectId)
        {
            Diag($"{scope}: targeting {priority.Name} ({priority.GameObjectId:X}, name id {priority.NameId}) first.");
            Svc.Targets.Target = priority;
        }

        if (!priorityTargeting)
        {
            priorityTargeting = true;
            var modules = TreasureCombatPreset.TargetingModules;
            var set = 0;
            for (var index = 0; index < modules.Length; index++)
            {
                if (BossModIPC.Instance.AddTransientStrategy(TreasureCombatPreset.Name, modules[index], TreasureCombatPreset.TargetingTrack, TreasureCombatPreset.ManualTargeting))
                {
                    set++;
                }
            }

            Diag($"{scope}: the rotations attack only the chosen target for now ({set} of {modules.Length} took it).");
        }

        return true;
    }

    private void ReleasePriorityTargeting(string scope)
    {
        if (!priorityTargeting)
        {
            return;
        }

        priorityTargeting = false;
        var modules = TreasureCombatPreset.TargetingModules;
        for (var index = 0; index < modules.Length; index++)
        {
            BossModIPC.Instance.ClearTransientStrategy(TreasureCombatPreset.Name, modules[index], TreasureCombatPreset.TargetingTrack);
        }

        Diag($"{scope}: no enemy is wanted first any more; the rotations pick their targets again.");
    }

    // The lure's window closes the moment a challenge is taken and opens again for the next one, so between the two
    // the coffer looks free to open and the gates free to take. The lure runs while its window is up or its high and
    // low cards wait on a guess, and from the trap springing until the game says how it ended; until then nothing else
    // in the chamber is touched. The cards are read off the field, so a plugin reload mid-lure still sees it.
    private bool LureOngoing(DungeonInProgress dungeon)
    {
        if (LureDialog.Open() || DungeonField.LureGuessPending())
        {
            return true;
        }

        return Messages.TryGetLatest(LureMessages, dungeon.RoundStartedAtMs, out var latest)
            && latest.Id is DungeonLogMessages.LureSprung or DungeonLogMessages.LureRound or DungeonLogMessages.LureDisarmed
            && Environment.TickCount64 - latest.AtMs < LureQuietLimitMs;
    }

    private static ReadOnlySpan<uint> LureMessages =>
    [
        DungeonLogMessages.LureSprung,
        DungeonLogMessages.LureRound,
        DungeonLogMessages.LureDisarmed,
        DungeonLogMessages.LureNotDisarmed,
        DungeonLogMessages.LureDeclined,
        DungeonLogMessages.LureStopped,
        DungeonLogMessages.LureAllDisarmed,
        DungeonLogMessages.LureFailed,
    ];

    private async Task AnswerDungeonLure(DungeonContext context)
    {
        const string scope = "dungeon-lure";
        var dungeon = context.Dungeon;
        var policy = Plugin.Instance.Configuration.DungeonLurePolicy;
        if (DungeonField.LureGuessPending() && !LureDialog.Open())
        {
            await AwaitLureGuess(scope, context, policy);
            return;
        }

        if (policy != LurePolicy.AskMe && !LureDialog.Open())
        {
            // The window is answered; the coffer is opening, or the cards are out for the player to guess.
            Status = "Gambler's Lure: waiting for it to end";
            await NextFrame(DungeonTickFrames);
            return;
        }

        if (policy == LurePolicy.Auto && await PlayLure(scope, dungeon))
        {
            return;
        }

        if (policy == LurePolicy.OpenCoffer && await AnswerLureWindow(scope, challenge: false))
        {
            return;
        }

        if (!dungeon.LureNoticeShown)
        {
            dungeon.LureNoticeShown = true;
            Diag($"{scope}: the Gambler's Lure is up; it is left to the player.");
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatLure)}");
        }

        Status = "Gambler's Lure: up to you";
        while (LureOngoing(dungeon) && !CancelToken.IsCancellationRequested && context.InDungeon)
        {
            await NextFrame(DungeonTickFrames);
        }
    }

    // Challenges or takes the treasure by what the coffer holds and how many challenges it has had. The cards are left
    // to the player. False when the window could not be read or would not take the choice.
    private async Task<bool> PlayLure(string scope, DungeonInProgress dungeon)
    {
        if (!LureDialog.TryReadRareLoot(out var rare, out var entries))
        {
            Warn($"{scope}: the lure window's treasure could not be read; taking the treasure as it is.");
            return await AnswerLureWindow(scope, challenge: false);
        }

        rare.RemoveAll(static itemId => !LootCleanup.KeepsRare(itemId));
        var limit = lureUnattended ? 0 : rare.Count > 0 ? LureChallengesWithRareLoot : LureChallengesPlain;
        var next = NextLureChallenge(dungeon);
        var challenge = next <= limit;
        var names = string.Join(", ", rare.Select(static itemId => NotableLoot.Name(itemId)));
        Diag($"{scope}: {entries} treasure entr(ies), rare loot kept [{names}], unattended {lureUnattended}; challenge {next}, limit {limit}: {(challenge ? "challenging" : "taking the treasure")}.");
        if (rare.Count > 0 && !dungeon.LureNoticeShown)
        {
            dungeon.LureNoticeShown = true;
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatLureRare, names)}");
        }

        if (!await AnswerLureWindow(scope, challenge))
        {
            return false;
        }

        Svc.Chat.Print(challenge
            ? $"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatLureChallenge, next)}"
            : $"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatLureTake, next)}");
        return true;
    }

    // The cards are dealt and wait on a guess. With the policy left to the player, the run names the better guess
    // and waits. Otherwise it guesses on the card dealt face up; if that card cannot be read, the player has a minute
    // to guess before the run tosses a coin, and the run stops challenging for the rest of it.
    private async Task AwaitLureGuess(string scope, DungeonContext context, LurePolicy policy)
    {
        Status = "Gambler's Lure: waiting for the guess";
        var dealtAt = Environment.TickCount64;
        var shown = await WaitForShownCard(context);
        var raw = 0;
        if (shown is null)
        {
            DungeonField.LureShownCard(out raw);
        }

        Diag($"{scope}: the card dealt face up reads {(shown?.ToString(CultureInfo.InvariantCulture) ?? "unreadable")} (mask 0x{raw:X}).");
        if (shown is { } card)
        {
            var high = card < LureMiddleCard || (card == LureMiddleCard && Random.Shared.Next(2) == 0);
            if (policy == LurePolicy.AskMe)
            {
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatLureCardHint, card, Loc.T(high ? L.Dungeon.LureHigh : L.Dungeon.LureLow))}");
            }
            else
            {
                await GuessLure(scope, context, high, $"the card shows {card}");
                return;
            }
        }

        while (DungeonField.LureGuessPending() && !CancelToken.IsCancellationRequested && context.InDungeon)
        {
            if (shown is null && policy != LurePolicy.AskMe && Environment.TickCount64 - dealtAt >= LureGuessWaitMs && !NpcInteraction.SelectYesnoOpen())
            {
                lureUnattended = true;
                var coin = Random.Shared.Next(2) == 0;
                Warn($"{scope}: no guess within {LureGuessWaitMs / TimeUnits.MillisecondsPerSecond}s and the card unreadable; guessing at random and taking the coffers as they are for the rest of the run.");
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatLureBlindGuess, Loc.T(coin ? L.Dungeon.LureHigh : L.Dungeon.LureLow))}");
                await GuessLure(scope, context, coin, "a coin toss");
                return;
            }

            await NextFrame(DungeonTickFrames);
        }

        if (!CancelToken.IsCancellationRequested && lureUnattended && Environment.TickCount64 - dealtAt < LureGuessWaitMs)
        {
            lureUnattended = false;
            Diag($"{scope}: the player guessed; challenges are back on.");
        }
    }

    // The card turns over a moment before the choices can be targeted, so it is read for a short while.
    private async Task<int?> WaitForShownCard(DungeonContext context)
    {
        var deadline = Environment.TickCount64 + LureCardReadMs;
        while (Environment.TickCount64 < deadline && DungeonField.LureGuessPending() && !CancelToken.IsCancellationRequested && context.InDungeon)
        {
            if (DungeonField.LureShownCard(out _) is { } card)
            {
                return card;
            }

            await NextFrame(DungeonTickFrames);
        }

        return null;
    }

    private async Task GuessLure(string scope, DungeonContext context, bool high, string reason)
    {
        if (!await HumanDelay(HumanAction.OpenCoffer, scope) || DungeonField.LureChoice(high) is not { } choice)
        {
            return;
        }

        var choiceId = choice.GameObjectId;
        var name = high ? "high" : "low";
        Diag($"{scope}: guessing {name} ({reason}).");
        Status = "Gambler's Lure: guessing";
        await context.Interact(() => Svc.Objects.SearchById(choiceId), $"lure choice {name}", NpcInteraction.SelectYesnoOpen, scope);
        if (await WaitUntilTimed(NpcInteraction.SelectYesnoOpen, LureGuessAnswerMs, "lure guess question"))
        {
            context.ConfirmPrompt(scope);
        }

        await WaitUntilTimed(() => !DungeonField.LureGuessPending(), LureGuessAnswerMs, "lure guess taken");
    }

    // The challenge the window offers now, counted from 1; the treasure stands at this many shares.
    private int NextLureChallenge(DungeonInProgress dungeon)
    {
        var since = Messages.TryGetLatest(DungeonLogMessages.LureSprung, dungeon.RoundStartedAtMs, out var sprung) ? sprung.AtMs : dungeon.RoundStartedAtMs;
        return Messages.TryGetLatest(DungeonLogMessages.LureRound, since, out var round) && round.TryGetParameter(0, out var taken) ? taken + 1 : 1;
    }

    // True once the window took the choice and closed, or the run was stopped before it chose.
    private async Task<bool> AnswerLureWindow(string scope, bool challenge)
    {
        Status = challenge ? "Gambler's Lure: challenging" : "Opening the coffer as it is";
        if (!await HumanDelay(HumanAction.OpenCoffer, scope))
        {
            return true;
        }

        for (var attempt = 1; attempt <= LureClickAttempts && LureDialog.Open(); attempt++)
        {
            if (challenge ? LureDialog.Challenge() : LureDialog.OpenCoffer())
            {
                Diag($"{scope}: {(challenge ? "challenging the lure" : "opening the coffer as it is")} (attempt {attempt}).");
            }

            await WaitUntilTimed(() => !LureDialog.Open(), LureCloseWaitMs, "lure closed", 1);
        }

        if (!LureDialog.Open())
        {
            return true;
        }

        Warn($"{scope}: the lure window would not take the choice; leaving it to the player.");
        return false;
    }

    // Rolls whatever waits on a roll by the Loot setting, then gives the bags a moment to settle.
    private async Task SettleDungeonLoot(DungeonContext context)
    {
        EnterStep(TreasurePhase.Looting);
        Status = "Looting";
        var choice = Plugin.Instance.Configuration.LootRoll;
        Func<uint, bool> any = static _ => true;
        var rounds = 0;
        var nextRollAt = 0L;
        var deadline = Environment.TickCount64 + LootSettleCapMs;
        while (!CancelToken.IsCancellationRequested && Environment.TickCount64 < deadline && LootRolls.Pending(any) > 0)
        {
            var now = Environment.TickCount64;
            if (choice != LootRoll.Manual && rounds < MaxRollRounds && now >= nextRollAt)
            {
                var rolled = LootRolls.Roll(choice, any);
                Diag($"Dungeon loot: rolled {choice} on {rolled} item(s); {LootRolls.Describe()}.");
                rounds++;
                nextRollAt = now + LootRollResendMs;
            }
            else if (rounds >= MaxRollRounds && now >= nextRollAt)
            {
                break;
            }

            await NextFrame(LootPollFrames);
        }

        await DelayMs(LootSettleQuietMs);
        if (LootRolls.Pending(any) > 0)
        {
            Diag($"Dungeon loot: {LootRolls.Pending(any)} item(s) still wait on a roll; trying again in {LootRetryMs / TimeUnits.MillisecondsPerSecond}s.");
            context.Dungeon.LootNotBeforeMs = Environment.TickCount64 + LootRetryMs;
        }
    }

    private async Task<bool> LeaveDungeonDuty(DungeonContext context)
    {
        const string scope = "dungeon-leave";
        var dungeon = context.Dungeon;
        EnterStep(TreasurePhase.Finishing);
        Status = "Leaving the dungeon";
        // Items still waiting on a roll would be lost with the duty, so the player gets a minute to roll them by hand.
        if (LootRolls.Pending(static _ => true) > 0)
        {
            Diag($"{scope}: items still wait on a roll; waiting up to {LootPendingWaitMs / TimeUnits.MillisecondsPerSecond}s before leaving.");
            await WaitUntilTimed(() => LootRolls.Pending(static _ => true) == 0, LootPendingWaitMs, "loot rolled before leaving");
        }

        if (!await HumanDelay(HumanAction.MoveOff, scope))
        {
            return false;
        }

        // The dungeon's own exit point is the way out a player takes; the duty list is the fallback where it has none.
        if (DungeonField.NearestExit(dungeon.Profile) is { } exit)
        {
            var exitId = exit.GameObjectId;
            Diag($"{scope}: round {dungeon.Round} of {dungeon.Profile.Rounds} done and the field is quiet; leaving through the exit {exit.BaseId}.");
            dungeon.LeaveRequested = true;
            await context.Interact(() => Svc.Objects.SearchById(exitId), $"exit {exitId:X}", () => NpcInteraction.SelectYesnoOpen() || context.Zoning, scope);
            context.ConfirmPrompt(scope);
            if (await WaitUntilTimed(() => context.Zoning || !context.InDungeon, LeaveZoneWaitMs, "leaving through the exit"))
            {
                return true;
            }

            Diag($"{scope}: the exit did not take the character out.");
        }

        if (CanLeaveContent())
        {
            Diag($"{scope}: round {dungeon.Round} of {dungeon.Profile.Rounds} done and the field is quiet; leaving the duty.");
            dungeon.LeaveRequested = true;
            LeaveContent();
            return await WaitUntilTimed(() => context.Zoning || !context.InDungeon, LeaveZoneWaitMs, "leaving the duty");
        }

        Warn($"{scope}: the game does not let the duty be left yet and no exit is known.");
        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatStuck)}");
        return false;
    }

    private static unsafe bool CanLeaveContent() => EventFramework.CanLeaveCurrentContent();

    private static unsafe void LeaveContent() => EventFramework.LeaveCurrentContent(false);

    private static string DescribeField(DungeonContext context)
    {
        var dungeon = context.Dungeon;
        var coffer = DungeonField.NearestCoffer(dungeon, int.MaxValue);
        var advance = DungeonField.AdvanceObjects(dungeon.Profile).Count;
        DungeonField.NearestEnemy(out var enemies);
        var at = DungeonField.PlayerPosition;
        return $"at ({at.X:F1}, {at.Y:F1}, {at.Z:F1}), coffer {(coffer is null ? "none" : $"{coffer.BaseId} ({dungeon.CofferInteractions(coffer.GameObjectId)} interaction(s))")}, {advance} advance object(s), {enemies} enemy(ies), in combat {Svc.Condition[ConditionFlag.InCombat]}, loot waiting {LootRolls.Pending(static _ => true)}";
    }

    private static void AnnounceDungeon(in DungeonResult result)
    {
        var name = TerritoryNames.Of(result.Profile.TerritoryId);
        var outcome = Loc.T(DungeonOutcomes.Label(result.Outcome));
        var gil = result.Gil.ToString("N0", Loc.Culture);
        var line = Loc.T(L.Dungeon.ChatEnded, name, outcome, result.Round, gil, result.ItemCount);
        if (result.Outcome is DungeonOutcome.Cleared or DungeonOutcome.Expelled)
        {
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {line}");
            return;
        }

        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {line}");
    }

    // What a dungeon handler and its strategies may ask of the run: the walk, the interaction, the waits and the
    // shared steps. A step that differs in one dungeon overrides the handler's method and calls the parts it keeps.
    internal sealed class DungeonContext(AutoCommon owner, DungeonInProgress dungeon)
    {
        public DungeonInProgress Dungeon => dungeon;

        public DungeonProfile Profile => dungeon.Profile;

        public Configuration Config => Plugin.Instance.Configuration;

        public bool Cancelled => owner.CancelToken.IsCancellationRequested;

        public string Status
        {
            set => owner.Status = value;
        }

        public bool Zoning => Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];

        public bool InDungeon => Svc.ClientState.TerritoryType == dungeon.Profile.TerritoryId;

        public void Diag(string message, [CallerFilePath] string callerFile = "") => owner.Diag(message, callerFile);

        public void Warn(string message, [CallerFilePath] string callerFile = "") => owner.Warn(message, callerFile);

        public Task<bool> Delay(HumanAction action, string label) => owner.HumanDelay(action, label);

        public Task DelayMs(int milliseconds) => owner.DelayMs(milliseconds);

        public Task<bool> WaitUntil(Func<bool> condition, int timeoutMs, string scope) => owner.WaitUntilTimed(condition, timeoutMs, scope);

        public bool MessageSince(uint logMessageId, long sinceMs) => Messages.ArrivedSince(logMessageId, sinceMs);

        public bool TryGetMessage(ReadOnlySpan<uint> logMessageIds, long sinceMs, out TreasureMessage message)
            => Messages.TryGetLatest(logMessageIds, sinceMs, out message);

        // Something on the field that answers what the run just did: a fight, or the lure.
        public bool FieldBusy()
            => Svc.Condition[ConditionFlag.InCombat] || owner.LureOngoing(Dungeon) || DungeonField.NearestEnemy(out _) is not null;

        public bool PromptOpen() => NpcInteraction.SelectYesnoOpen();

        // A yes-or-no that opens right after the run asked a gate, the machine or the exit is that object's own question
        // (use the key, draw, leave), so it is answered Yes; its text is logged so a recording shows which it was.
        public void ConfirmPrompt(string scope)
        {
            if (!NpcInteraction.SelectYesnoOpen())
            {
                return;
            }

            Diag($"{scope}: the game asks '{NpcInteraction.SelectYesnoText()}'; answering Yes.");
            DialogDriver.Confirm();
        }

        // Walks into reach of the object find returns and interacts with it. True once doneWhen holds.
        public async Task<bool> Interact(Func<IGameObject?> find, string name, Func<bool> doneWhen, string scope)
        {
            var target = await owner.ApproachObject(find, name, scope);
            if (target is null || !await owner.ReadyToInteract(scope))
            {
                return false;
            }

            return await owner.InteractWithObject(target, doneWhen, scope);
        }

        // A walk inside the dungeon, as its own cancellable operation. True once the character is within reach of the
        // destination or stopWhen holds.
        public async Task<bool> WalkTo(Vector3 destination, float tolerance, Func<bool>? stopWhen, int timeoutMs, string scope)
        {
            await owner.WaitForNavmeshReady(timeoutMs);
            var walk = new MoveOp(move => move.MoveInZone(destination, MovementConfig.Default.WithTolerance(tolerance), stopWhen));
            await owner.RunCancellable(walk, timeoutMs, scope, StuckDetector.MoveStallAbort(scope));
            if (walk.Fault is { } fault)
            {
                Diag($"{scope}: the walk faulted: {fault.Message}");
            }

            return WithinReach(destination, tolerance) || stopWhen?.Invoke() == true;
        }

        public Task OpenCoffer(IGameObject coffer) => owner.DungeonOpenCoffer(this, coffer);

        public Task FightField() => owner.FightDungeonField(this);

        public Task AnswerLure() => owner.AnswerDungeonLure(this);

        public Task SettleLoot() => owner.SettleDungeonLoot(this);

        public Task<bool> LeaveDuty() => owner.LeaveDungeonDuty(this);
    }
}
