using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    // Dig's recast is 10 s.
    private const int DigRecastWaitMs = 11_000;
    private const int DigAnswerTimeoutMs = 5_000;
    private const int DigRetryDelayMs = 1_500;
    private const int DigUsableWaitMs = 3_000;
    private const int MaxUnansweredDigs = 3;
    private const float DigStepToleranceMeters = 0.3f;
    private const float DigStepLiftMeters = 3f;
    private const float DigStepFloorHalfExtentMeters = 3f;
    private const int DigStepWatchdogMs = 15_000;
    private const int PortalKind = 1;

    private readonly ulong[] coffersBeforeDig = new ulong[TreasureCoffers.MaxRemembered];
    private int coffersBeforeDigCount;

    private enum DigAnswer : byte { Found, Portal, NotHere, Refused, NoMap, NoAnswer, Unreachable, Cancelled }

    // Fresh is true when this dig turned the coffer up, false when an earlier one already had.
    private readonly record struct DigResult(DigAnswer Answer, MapRefusal Refusal = MapRefusal.None, bool Fresh = false);

    private static ReadOnlySpan<uint> DigFinds => [TreasureLogMessages.Discovered, TreasureLogMessages.AlreadyDiscovered];

    private static ReadOnlySpan<uint> DigReplies =>
    [
        TreasureLogMessages.Discovered,
        TreasureLogMessages.AlreadyDiscovered,
        TreasureLogMessages.NothingToDigHere,
        TreasureLogMessages.DigPartyNotInArea,
        TreasureLogMessages.DigBoundByDuty,
        TreasureLogMessages.DigPartyBoundByDuty,
        TreasureLogMessages.DigWithoutMap,
        TreasureLogMessages.CannotExecute,
    ];

    // A dig that finds nothing first steps onto the Level point itself, then travels to the spot afresh, before it gives
    // up. A dig the game never answers, or answers with its generic refusal, is tried again a few times.
    private async Task<DigResult> DigUp(MapInProgress map)
    {
        coffersBeforeDigCount = TreasureCoffers.Collect(map.Spot.Position, CofferSearchMeters, coffersBeforeDig);
        if (!map.CofferOpened)
        {
            Span<ulong> portals = stackalloc ulong[TreasurePortals.MaxRemembered];
            map.PortalsBefore = portals[..TreasurePortals.Collect(map.Spot.Position, PortalSearchMeters, portals)].ToArray();
        }
        var nearMisses = 0;
        var unanswered = 0;
        while (!CancelToken.IsCancellationRequested)
        {
            EnterStep(TreasurePhase.Digging);
            var result = await DigOnce(map);
            if (result.Answer == DigAnswer.NoAnswer)
            {
                if (++unanswered >= MaxUnansweredDigs)
                {
                    Warn($"Dig: the game did not take the dig {unanswered} times; giving up.");
                    return result;
                }

                await DelayMs(DigRetryDelayMs);
                continue;
            }

            if (result.Answer != DigAnswer.NotHere)
            {
                return result;
            }

            nearMisses++;
            if (nearMisses == 1)
            {
                await StepOntoSpot(map.Spot.Position);
                continue;
            }

            if (nearMisses == 2)
            {
                if (!await TravelToSpotAgain(map))
                {
                    return new DigResult(CancelToken.IsCancellationRequested ? DigAnswer.Cancelled : DigAnswer.Unreachable);
                }

                continue;
            }

            Warn($"Dig: nothing found at {map.Spot.ZoneName} {FormatPosition(map.Spot.Position)} after stepping onto it and travelling there again.");
            return result;
        }

        return new DigResult(DigAnswer.Cancelled);
    }

    private async Task<DigResult> DigOnce(MapInProgress map)
    {
        await FightOffAttackers("map-dig");
        if (DigOps.RecastActive())
        {
            Status = Loc.T(L.Status.WaitingDig);
            await WaitUntilTimed(() => !DigOps.RecastActive(), DigRecastWaitMs, "dig recast", MessagePollFrames);
        }

        if (CancelToken.IsCancellationRequested)
        {
            return new DigResult(DigAnswer.Cancelled);
        }

        if (!await HumanDelay(HumanAction.Dig, "map-dig"))
        {
            return new DigResult(DigAnswer.Cancelled);
        }

        if (!await GroundedForDig())
        {
            return new DigResult(DigAnswer.Cancelled);
        }

        Status = Loc.T(L.Status.Digging);
        LogOffsetFrom("Dig", map.Spot.Position);
        var since = TreasureMessageWatcher.Now;
        var status = DigOps.ActionStatus();
        var used = DigOps.Use();
        Diag($"Dig: General Action 20 used (returned {used}, action status {status}).");
        await WaitUntilTimed(() => Messages.TryGetLatest(DigReplies, since, out _), DigAnswerTimeoutMs, "dig answer", MessagePollFrames);
        if (!Messages.TryGetLatest(DigReplies, since, out var reply))
        {
            return new DigResult(DigAnswer.NoAnswer);
        }

        // A dig at a coffer already out, one the player dug by hand say, is answered with AlreadyDiscovered and, in the same
        // moment, a refusal such as the party being bound by duty, which that coffer is the cause of; the coffer counts.
        if (Messages.TryGetLatest(DigFinds, since, out var found))
        {
            reply = found;
        }

        var portal = reply.TryGetParameter(0, out var kind) && kind == PortalKind;
        Diag($"Dig: the game answered {TreasureLogMessages.NameOf(reply.Id)} ({(portal ? "a portal" : "a coffer or nothing")}).");
        return reply.Id switch
        {
            TreasureLogMessages.Discovered => new DigResult(portal ? DigAnswer.Portal : DigAnswer.Found, Fresh: true),
            TreasureLogMessages.AlreadyDiscovered => new DigResult(portal ? DigAnswer.Portal : DigAnswer.Found),
            TreasureLogMessages.NothingToDigHere => new DigResult(DigAnswer.NotHere),
            TreasureLogMessages.DigWithoutMap => new DigResult(DigAnswer.NoMap),
            TreasureLogMessages.CannotExecute => new DigResult(DigAnswer.NoAnswer),
            _ => new DigResult(DigAnswer.Refused, MapOutcomes.FromDigMessage(reply.Id)),
        };
    }

    // A dig used while the character is still coming off the mount is refused as used in the air, so the dig waits for
    // solid ground, and then for the game to report Dig usable, before it is pressed.
    private async Task<bool> GroundedForDig()
    {
        const string scope = "map-dig-ground";
        if (Svc.Condition[ConditionFlag.Mounted] && !await SafeDismount(scope))
        {
            Diag($"{scope}: still mounted before the dig ({ConditionTag()})");
        }

        if (!await SettleOnGround(scope))
        {
            Diag($"{scope}: not settled on the ground before the dig ({ConditionTag()})");
        }

        if (DigOps.ActionStatus() is not 0 and var status
            && !await WaitUntilTimed(() => DigOps.ActionStatus() == 0, DigUsableWaitMs, scope, MessagePollFrames))
        {
            Diag($"{scope}: the game still reports Dig unusable (status {status}); digging anyway");
        }

        return !CancelToken.IsCancellationRequested;
    }

    private async Task StepOntoSpot(Vector3 point)
    {
        var floor = NavmeshIPC.Instance.PointOnFloor(point with { Y = point.Y + DigStepLiftMeters }, allowUnlandable: true, DigStepFloorHalfExtentMeters) ?? point;
        Status = Loc.T(L.Status.SteppingOntoSpot);
        Diag($"Dig: nothing here; stepping {DistanceTo(floor):F1}m onto the spot's own point at {FormatPosition(floor)}.");
        const string scope = "map-dig-step";
        var step = new MoveOp(move => move.MoveInZone(floor, walkMovement.WithTolerance(DigStepToleranceMeters), null));
        await RunCancellable(step, DigStepWatchdogMs, scope, StuckDetector.MoveStallAbort(scope));
        if (step.Fault is { } fault)
        {
            Diag($"{scope}: faulted: {fault.Message}");
        }
    }

    // The spot is read again first, in case the held map is not the one the run set out with.
    private async Task<bool> TravelToSpotAgain(MapInProgress map)
    {
        var state = HeldSpot.Read(Plugin.Instance.Catalog, out _, out var tier, out var spot);
        if (state == HeldSpotState.Resolved && tier.Rank == map.Tier.Rank && spot.LevelId != map.Spot.LevelId)
        {
            Diag($"Dig: the held map now points at Level {spot.LevelId} instead of {map.Spot.LevelId}.");
            map.Spot = spot;
        }

        Diag($"Dig: still nothing at the spot; travelling to {map.Spot.ZoneName} {FormatPosition(map.Spot.Position)} afresh.");
        return await ReachSpot(map.Spot);
    }

    // Logged before every dig and at the coffer, so the game's real dig reach can be read off a test run.
    private void LogOffsetFrom(string label, Vector3 point)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return;
        }

        var position = player.Position;
        Diag($"{label}: standing {GroundDistance.Between(position, point):F1}m across and {position.Y - point.Y:F1}m above the Level point ({ConditionTag()}).");
    }
}
