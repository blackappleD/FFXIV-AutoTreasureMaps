using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Game.Watchers;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Supply;
using AutoTreasureMaps.Core.Treasure;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

// The run: the held map, or else the next one the supply chain puts in hand, with upkeep between maps, until a stop
// condition or a refusal ends it. A fresh task is built for every resume and fault restart; the session keeps the
// counts, the map under way and how the run ended.
internal sealed class AutoTreasureRun(TreasureSession session, TreasureProgress progress) : AutoCommon
{
    // The game refuses a dig while a party member is out of the area, so the party gets this long to gather.
    private const int PartyWaitMs = 60_000;
    // The dig is tried again once everyone shows as present, but no sooner than this and no later than the longest.
    private const int PartyRetryShortestMs = 10_000;
    private const int PartyRetryLongestMs = 20_000;
    private const int HeldMapWaitMs = 3_000;
    // The hunt's own time limit is about five minutes, so its duty is over by then however the coffer went.
    private const int HuntDutyWaitMs = 330_000;

    private enum LoopStep : byte { Continue, Stop }

    private long partyWaitStartedAtMs;

    protected override async Task Execute()
    {
        try
        {
            await RunMaps();
        }
        catch (Exception exception)
        {
            session.RecordFault(exception, CancelToken);
            throw;
        }
        finally
        {
            ReleaseCombatMovement("run");
        }
    }

    private async Task RunMaps()
    {
        var plugin = Plugin.Instance;
        while (!CancelToken.IsCancellationRequested)
        {
            if (!await EnsureStanding())
            {
                if (!CancelToken.IsCancellationRequested)
                {
                    Finish(RunEnd.StillKnockedOut);
                }

                return;
            }

            if (!await ReturnFromGatherer())
            {
                if (!CancelToken.IsCancellationRequested)
                {
                    Finish(RunEnd.SupplyFailed);
                }

                return;
            }

            // A resume after a pause or a fault inside a dungeon the run entered picks that dungeon up before anything else.
            // A dungeon the player entered by hand is theirs (/atm dungeon clears it), so the run waits for them outside.
            if (await PickUpDungeon(session, progress) is { } dungeonStep)
            {
                if (dungeonStep == PortalStep.Stop)
                {
                    return;
                }

                session.NoteMapCarriedThrough();
                continue;
            }

            if (RunLimits.Reached(session, plugin.Configuration) is { } limit)
            {
                Finish(limit);
                return;
            }

            var held = session.ActiveMap is not null || HeldMapReader.TryRead(plugin.Catalog, out _);
            if (held && session.ActiveMap is null && await BuySpareFirst(plugin) == LoopStep.Stop)
            {
                return;
            }

            var step = held
                ? await RunMap(plugin)
                : await SupplyMap(plugin);
            if (step == LoopStep.Stop)
            {
                return;
            }
        }
    }

    private async Task<LoopStep> RunMap(Plugin plugin)
    {
        if (session.ActiveMap is null)
        {
            progress.SetPhase(TreasurePhase.Preparing);
            if (!await HumanDelay(HumanAction.MoveOff, "run"))
            {
                return LoopStep.Stop;
            }
        }

        // A party broken up to buy on another world is invited back there before the map.
        await RegroupParty();
        if (CancelToken.IsCancellationRequested)
        {
            return LoopStep.Stop;
        }

        var result = await RunHeldMap(session, progress);
        if (MapFault is { } fault)
        {
            session.RecordFault(fault, CancelToken);
            return LoopStep.Stop;
        }

        if (result.Outcome == MapOutcome.Cancelled || CancelToken.IsCancellationRequested)
        {
            return LoopStep.Stop;
        }

        if (result.Outcome == MapOutcome.Refused && result.Refusal != MapRefusal.PortalDug)
        {
            return await HandleRefusal(result);
        }

        // A coffer that left a portal, or a dig that turned one up straight away (a thief's map), leads into a dungeon.
        if (result.PortalSeen || result.Refusal == MapRefusal.PortalDug)
        {
            var portal = await EnterPortalAndClear(session, progress);
            if (portal == PortalStep.Stop)
            {
                return LoopStep.Stop;
            }

            if (portal == PortalStep.Skipped && result.Refusal == MapRefusal.PortalDug)
            {
                return await HandleRefusal(result);
            }
        }

        partyWaitStartedAtMs = 0;
        session.NoteMapCarriedThrough();
        NoteMapFinished();
        await UpkeepBetweenMaps(plugin);
        return LoopStep.Continue;
    }

    // A run that starts with a deciphered map in hand buys its spare before the dig, as a run that buys its map does at
    // the board, so the map after it needs no trip of its own. Once a run: from then on the spare comes with each purchase.
    private async Task<LoopStep> BuySpareFirst(Plugin plugin)
    {
        if (session.SpareChecked)
        {
            return LoopStep.Continue;
        }

        session.NoteSpareChecked();
        var configuration = plugin.Configuration;
        if (!configuration.MarketSpareFirst)
        {
            return LoopStep.Continue;
        }

        if (session.StopAfterMap || (configuration.RunMapLimit > 0 && session.MapsRun + 2 > configuration.RunMapLimit))
        {
            Diag("Run: a deciphered map is held, and the run ends after it, so no spare is bought.");
            return LoopStep.Continue;
        }

        if (HoldsEnabledMap(plugin))
        {
            Diag("Run: a deciphered map is held, and a spare is in the bags already.");
            return LoopStep.Continue;
        }

        var market = new MarketSupplyStep();
        var check = market.Check(plugin);
        if (!check.CanSupply)
        {
            Diag($"Run: a deciphered map is held, but no spare can be bought ({check.Reason.Key}).");
            return LoopStep.Continue;
        }

        Diag("Run: a deciphered map is held but no spare; buying one before the dig.");
        progress.SetPhase(market.Phase);
        var task = market.CreateTask();
        await RunWithStatusFrom(task, () => RunCancellable(task, market.TimeoutMs, "supply-spare"));
        if (CancelToken.IsCancellationRequested)
        {
            return LoopStep.Stop;
        }

        if (task.Fault is { } fault)
        {
            session.RecordFault(fault, CancelToken);
            return LoopStep.Stop;
        }

        // Without a spare the held map is still dug; the next map is bought when it is needed.
        Diag($"Run: the spare purchase ended {task.Answer.Outcome}; on to the held map.");
        return LoopStep.Continue;
    }

    private static bool HoldsEnabledMap(Plugin plugin)
    {
        var owned = MapBag.Read(plugin.Catalog);
        for (var index = 0; index < owned.Length; index++)
        {
            if (MapTierRules.IsEnabled(plugin.Configuration, owned[index].Tier))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<LoopStep> HandleRefusal(MapResult result)
    {
        if (result.Refusal == MapRefusal.PartyNotInArea && await WaitForParty())
        {
            return LoopStep.Continue;
        }

        if (CancelToken.IsCancellationRequested)
        {
            return LoopStep.Stop;
        }

        Finish(RunEnd.Refused, result.Refusal);
        return LoopStep.Stop;
    }

    // True when the dig should be tried again: the map is still held, and the party may have gathered since.
    private async Task<bool> WaitForParty()
    {
        var now = Environment.TickCount64;
        if (partyWaitStartedAtMs == 0)
        {
            partyWaitStartedAtMs = now;
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatPartyWait, PartyWaitMs / TimeUnits.MillisecondsPerSecond)}");
        }

        var deadline = partyWaitStartedAtMs + PartyWaitMs;
        if (now >= deadline)
        {
            Diag($"Run: the party did not gather in the area within {PartyWaitMs / TimeUnits.MillisecondsPerSecond}s.");
            return false;
        }

        progress.SetPhase(TreasurePhase.Preparing);
        Status = "Waiting for the party to reach the area";
        var shortest = now + PartyRetryShortestMs;
        var waitMs = (int)(Math.Min(deadline, now + PartyRetryLongestMs) - now);
        var gathered = await WaitUntilTimed(() => Environment.TickCount64 >= shortest && PartyPresence.AllInZone(), waitMs, "party in the area");
        if (CancelToken.IsCancellationRequested)
        {
            return false;
        }

        Diag($"Run: trying the dig again ({(gathered ? "the party shows as present" : "still waiting on the party")}).");
        return true;
    }

    private async Task<LoopStep> SupplyMap(Plugin plugin)
    {
        await WaitOutHuntDuty();
        if (CancelToken.IsCancellationRequested)
        {
            return LoopStep.Stop;
        }

        // The wait can take minutes, so a limit that ran out meanwhile ends the run before anything is deciphered or bought.
        if (RunLimits.Reached(session, plugin.Configuration) is { } limit)
        {
            Finish(limit);
            return LoopStep.Stop;
        }

        var steps = MapSupplyChain.Steps;
        LocString emptyReason = default;
        var hasReason = false;
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var check = step.Check(plugin);
            if (!check.CanSupply)
            {
                Diag($"Run: the {step.Name} step cannot supply a map ({check.Reason.Key}).");
                NoteReason(check.Reason, ref emptyReason, ref hasReason);
                continue;
            }

            var task = await RunSupplyStep(step);
            if (CancelToken.IsCancellationRequested)
            {
                return LoopStep.Stop;
            }

            if (task.Fault is { } fault)
            {
                session.RecordFault(fault, CancelToken);
                return LoopStep.Stop;
            }

            var answer = task.Answer;
            switch (answer.Outcome)
            {
                case MapSupplyOutcome.Supplied:
                    return step.SuppliesHeldMap ? await AwaitHeldMap(plugin, step) : LoopStep.Continue;
                case MapSupplyOutcome.Empty:
                    NoteReason(answer.Reason, ref emptyReason, ref hasReason);
                    continue;
                case MapSupplyOutcome.Failed:
                    Finish(RunEnd.SupplyFailed);
                    return LoopStep.Stop;
                default:
                    Warn($"Run: the {step.Name} step did not finish within {step.TimeoutMs / TimeUnits.MillisecondsPerSecond}s.");
                    Finish(RunEnd.SupplyFailed);
                    return LoopStep.Stop;
            }
        }

        FinishOutOfMaps(emptyReason, hasReason);
        return LoopStep.Stop;
    }

    private async Task<MapSupplyTask> RunSupplyStep(IMapSupplyStep step)
    {
        progress.SetPhase(step.Phase);
        Diag($"Run: no map held; the {step.Name} step supplies the next one.");
        var task = step.CreateTask();
        await RunWithStatusFrom(task, () => RunCancellable(task, step.TimeoutMs, $"supply-{step.Name}"));
        return task;
    }

    // The game's word that a map was deciphered can come a moment before the held map reads back.
    private async Task<LoopStep> AwaitHeldMap(Plugin plugin, IMapSupplyStep step)
    {
        var catalog = plugin.Catalog;
        if (await WaitUntilTimed(() => HeldMapReader.TryRead(catalog, out _), HeldMapWaitMs, "held map after supply", 1))
        {
            return LoopStep.Continue;
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn($"Run: the {step.Name} step reported a map, but none is held.");
            Finish(RunEnd.SupplyFailed);
        }

        return LoopStep.Stop;
    }

    // The game keeps the character bound by the hunt's duty for a while after its coffer is looted, and until the hunt ends
    // if the coffer never opened; nothing can be deciphered or bought meanwhile, so the supply chain would read an empty
    // bag as the end of the run.
    private async Task WaitOutHuntDuty()
    {
        if (!DutyWatcher.InContent() || !TreasureHuntDirector.IsActive())
        {
            return;
        }

        Status = "Waiting for the treasure hunt to wind down";
        Diag("Run: still bound by the treasure hunt's duty; waiting for it to end before the next one.");
        if (!await WaitUntilTimed(() => !DutyWatcher.InContent(), HuntDutyWaitMs, "hunt duty over"))
        {
            Warn("Run: the treasure hunt's duty did not end; going on anyway.");
        }
    }

    // Upkeep is for the maps still to come, so it is skipped once the run is about to end.
    private async Task UpkeepBetweenMaps(Plugin plugin)
    {
        if (RunLimits.Reached(session, plugin.Configuration) is not null || !MoreToRun(plugin))
        {
            return;
        }

        progress.SetPhase(TreasurePhase.Upkeep);
        await RunUpkeep();
    }

    private static bool MoreToRun(Plugin plugin)
        => HeldMapReader.TryRead(plugin.Catalog, out _) || MapSupplyChain.Check(plugin).CanSupply;

    private static void NoteReason(LocString reason, ref LocString first, ref bool hasReason)
    {
        if (hasReason || reason.Key is null)
        {
            return;
        }

        first = reason;
        hasReason = true;
    }

    private void FinishOutOfMaps(LocString reason, bool hasReason)
    {
        session.Finish(RunEnd.OutOfMaps);
        LogFinish(RunEnd.OutOfMaps);
        var line = hasReason ? Loc.T(L.Run.ChatOutOfMapsReason, Loc.T(reason)) : Loc.T(L.Run.ChatOutOfMaps);
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {line}");
    }

    private void Finish(RunEnd end, MapRefusal refusal = MapRefusal.None)
    {
        session.Finish(end, refusal);
        LogFinish(end);
        switch (end)
        {
            case RunEnd.MapLimit:
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.Plural(L.Run.ChatMapLimit, session.MapsRun)}");
                break;
            case RunEnd.StopAfterMap:
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.Plural(L.Run.ChatStopAfterMapDone, session.MapsRun)}");
                break;
            case RunEnd.TimeLimit:
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.Plural(L.Run.ChatTimeLimit, Plugin.Instance.Configuration.RunMinuteLimit)}");
                break;
            case RunEnd.FailureStreak:
                Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.Plural(L.Run.ChatFailureStreak, session.FailedInARow)}");
                break;
            case RunEnd.KnockedOutTwice:
                Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatKnockedOutTwice)}");
                break;
            case RunEnd.Refused:
                Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatRefused)}");
                break;
            case RunEnd.SupplyFailed:
                Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatSupplyFailed)}");
                break;
        }
    }

    private void LogFinish(RunEnd end)
    {
        var refusal = session.EndRefusal == MapRefusal.None ? string.Empty : $" ({session.EndRefusal})";
        Diag($"Run: over, {end}{refusal}: {session.MapsRun} map(s) run, {session.MapsCompleted} looted, {session.MapsFailed} failed, {session.Gil} gil, {session.MapsBought} bought for {session.GilSpentOnMaps} gil, {session.MapsGathered} gathered, {session.PortalsSeen} portal(s), {session.Elapsed.TotalMinutes:F1} min.");
    }
}
