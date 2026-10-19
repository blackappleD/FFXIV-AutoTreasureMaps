using AutoTreasureMaps.Core.Cleanup;
using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Core.Treasure;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
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
    private const int BonusRoundCapMs = 120_000;
    private const int LootPendingWaitMs = 60_000;
    private const int LootRetryMs = 20_000;
    private const int LeaveZoneWaitMs = 15_000;
    private const int AdvanceRetryLimit = 3;
    private const int AdvanceRetryDelayMs = 5_000;

    private enum DungeonStep : byte { Lure, Bonus, Fight, Loot, Coffer, Advance, Leave, Idle }

    // Set when the last RunDungeon ended on an unexpected error, for the caller to hand to the session's fault handling.
    private protected Exception? DungeonFault { get; private set; }

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
        LootCleanup.Note(result.Items, dungeon.BagsBefore);
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
                    await WaitOutBonusRound(context);
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

        var (_, items) = BagSnapshot.Gains(before, BagSnapshot.Capture());
        LootCleanup.ShowPreview(items, before);
        return fingerprint;
    }

    // In order of what must come first: a dialog that waits on an answer, then anything that attacks, then the loot,
    // then the next coffer, and only then the way on.
    private DungeonStep Decide(TreasureDungeonHandler handler, DungeonContext context, long idleSince)
    {
        var dungeon = context.Dungeon;
        if (LureDialog.Open())
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

    // The bonus round wants the glowing sacks picked up by hand, which the run does not do yet.
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
            if (DigOps.IsCofferPrompt(prompt))
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
                if (IsKnockedOut() || context.Zoning || !context.InDungeon || LureDialog.Open())
                {
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

    private async Task AnswerDungeonLure(DungeonContext context)
    {
        const string scope = "dungeon-lure";
        var dungeon = context.Dungeon;
        var policy = Plugin.Instance.Configuration.DungeonLurePolicy;
        if (policy == LurePolicy.OpenCoffer)
        {
            Status = "Opening the coffer as it is";
            if (!await HumanDelay(HumanAction.OpenCoffer, scope))
            {
                return;
            }

            for (var attempt = 1; attempt <= LureClickAttempts && LureDialog.Open(); attempt++)
            {
                if (LureDialog.OpenCoffer())
                {
                    Diag($"{scope}: the Gambler's Lure is up; opening the coffer as it is (attempt {attempt}).");
                }

                await WaitUntilTimed(() => !LureDialog.Open(), LureCloseWaitMs, "lure closed", 1);
            }

            if (!LureDialog.Open())
            {
                return;
            }

            Warn($"{scope}: the lure window would not take the open-the-coffer choice; leaving it to the player.");
        }

        if (!dungeon.LureNoticeShown)
        {
            dungeon.LureNoticeShown = true;
            Diag($"{scope}: the Gambler's Lure is up; it is left to the player.");
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatLure)}");
        }

        Status = "Gambler's Lure: up to you";
        while (LureDialog.Open() && !CancelToken.IsCancellationRequested && context.InDungeon)
        {
            await NextFrame(DungeonTickFrames);
        }
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

        if (CanLeaveContent())
        {
            Diag($"{scope}: round {dungeon.Round} of {dungeon.Profile.Rounds} done and the field is quiet; leaving the duty.");
            dungeon.LeaveRequested = true;
            LeaveContent();
            return await WaitUntilTimed(() => context.Zoning || !context.InDungeon, LeaveZoneWaitMs, "leaving the duty");
        }

        if (DungeonField.NearestExit(dungeon.Profile) is { } exit)
        {
            var exitId = exit.GameObjectId;
            Diag($"{scope}: the duty cannot be left from the duty list; using the exit {exit.BaseId}.");
            dungeon.LeaveRequested = true;
            await context.Interact(() => Svc.Objects.SearchById(exitId), $"exit {exitId:X}", () => NpcInteraction.SelectYesnoOpen() || context.Zoning, scope);
            context.ConfirmPrompt(scope);
            return await WaitUntilTimed(() => context.Zoning || !context.InDungeon, LeaveZoneWaitMs, "leaving through the exit");
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
        return $"coffer {(coffer is null ? "none" : $"{coffer.BaseId} ({dungeon.CofferInteractions(coffer.GameObjectId)} interaction(s))")}, {advance} advance object(s), {enemies} enemy(ies), in combat {Svc.Condition[ConditionFlag.InCombat]}, loot waiting {LootRolls.Pending(static _ => true)}";
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
            => Svc.Condition[ConditionFlag.InCombat] || LureDialog.Open() || DungeonField.NearestEnemy(out _) is not null;

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
