using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int MaxLandingSpots = 3;
    private const float LandingRingRadiusMeters = 6f;
    private const int LandingRingPoints = 6;
    private const int LandingCandidateCount = 1 + LandingRingPoints;
    private const float LandingFloorHalfExtentMeters = 3f;
    // Probed from well above, so the floor found is the top layer under the spot and not a cave or a tunnel below it.
    private const float LandingProbeLiftMeters = 30f;
    private const float LandingArriveMeters = 1.5f;
    private const int LandingFlightWatchdogMs = 20_000;
    private const int LandingDismountWatchdogMs = 6_000;
    private const int GroundDismountWatchdogMs = 8_000;
    private const float LandingBackOffMeters = 8f;
    private const int LandingBackOffMs = 1_500;
    private protected const float LandsOnTheSpot = 0f;
    // An air dismount drops the character off the mount, and the mounted flag clears while it is still falling.
    private const int GroundSettleTimeoutMs = 5_000;
    private const int GroundSettleStableMs = 300;
    private const float GroundSettleStableMeters = 0.05f;
    private const int GroundSettlePollFrames = 2;

    private readonly Vector3[] landingSpots = new Vector3[LandingCandidateCount];

    protected async Task<bool> SafeDismount(string scope)
    {
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (!Svc.Condition[ConditionFlag.InFlight])
        {
            return await GroundDismount(scope);
        }

        var here = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;
        return await LandAndDismount(here, LandsOnTheSpot, scope);
    }

    // From the air, the movement library's dismount descends straight down from wherever the flight ended; over a tent,
    // a canopy or a cliff face that hovers for good or drops the character through the world. So the landing is aimed
    // at a landable floor point first, and a descent that stops moving is cut short and tried from the next spot. With
    // a stand-off the spots ring the place at that distance, nearest the character first, so a flight toward a target
    // touches down short of it and not on top of it.
    protected async Task<bool> LandAndDismount(Vector3 around, float standOffMeters, string scope)
    {
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (!Svc.Condition[ConditionFlag.InFlight])
        {
            return await GroundDismount(scope);
        }

        Status = Loc.T(L.Status.Landing);
        var from = Svc.Objects.LocalPlayer?.Position ?? around;
        var found = FindLandingSpots(around, standOffMeters, from, allowUnlandable: false, landingSpots);
        if (found == 0)
        {
            found = FindLandingSpots(around, standOffMeters, from, allowUnlandable: true, landingSpots);
        }

        if (found == 0)
        {
            Diag($"{scope}: no floor to land on around {FormatPosition(around)}; descending where the flight ended");
            return await DescendAndDismount(scope);
        }

        var attempts = Math.Min(found, MaxLandingSpots);
        for (var spotIndex = 0; spotIndex < attempts; spotIndex++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            var spot = landingSpots[spotIndex];
            var legScope = $"{scope}-land#{spotIndex + 1}";
            Diag($"{legScope}: flying {DistanceTo(spot):F0}m to a landable spot at {FormatPosition(spot)}");
            var flight = new MoveOp(move => move.MoveInZone(spot, walkMovement.WithTolerance(LandingArriveMeters), null));
            await RunCancellable(flight, LandingFlightWatchdogMs, legScope, StuckDetector.MoveStallAbort(legScope));
            if (flight.Fault is { } fault)
            {
                Diag($"{legScope}: the landing flight faulted: {fault.Message}");
            }

            if (await DescendAndDismount(legScope))
            {
                return true;
            }

            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            await BackOffInFlight(legScope);
        }

        Warn($"{scope}: could not land near {FormatPosition(around)} after {attempts} spot(s) ({ConditionTag()})");
        return false;
    }

    private async Task<bool> DescendAndDismount(string scope)
    {
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (!Svc.Condition[ConditionFlag.InFlight])
        {
            return await GroundDismount(scope);
        }

        await DismountViaOp(scope, LandingDismountWatchdogMs, StuckDetector.AirborneFreezeAbort(scope));
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            await SettleOnGround(scope);
            return true;
        }

        Diag($"{scope}: the descent did not land ({ConditionTag()})");
        CancelDescent();
        return false;
    }

    private async Task<bool> GroundDismount(string scope)
    {
        await DismountViaOp(scope, GroundDismountWatchdogMs);
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            await SettleOnGround(scope);
            return true;
        }

        Diag($"{scope}: still mounted after the dismount ({ConditionTag()})");
        return false;
    }

    // True once the character is off the mount, out of the air and has stopped dropping, so an action used next is not
    // refused as used mid-air. Gives up after a few seconds; the caller's own retries cover a ledge it never leaves.
    private async Task<bool> SettleOnGround(string scope)
    {
        var stableSince = 0L;
        var lastHeight = float.NaN;
        return await WaitUntilTimed(() =>
        {
            if (Svc.Objects.LocalPlayer is not { } player || Airborne())
            {
                stableSince = 0;
                lastHeight = float.NaN;
                return false;
            }

            var now = Environment.TickCount64;
            var height = player.Position.Y;
            if (float.IsNaN(lastHeight) || MathF.Abs(height - lastHeight) > GroundSettleStableMeters)
            {
                lastHeight = height;
                stableSince = now;
                return false;
            }

            return now - stableSince >= GroundSettleStableMs;
        }, GroundSettleTimeoutMs, $"{scope}-settle", GroundSettlePollFrames);
    }

    private static bool Airborne()
        => Svc.Condition[ConditionFlag.Mounted]
        || Svc.Condition[ConditionFlag.InFlight]
        || Svc.Condition[ConditionFlag.MountOrOrnamentTransition]
        || Svc.Condition[ConditionFlag.Jumping]
        || Svc.Condition[ConditionFlag.Jumping61];

    // Pressing jump in the air ends the game's own descent, which otherwise carries on after the operation is cancelled
    // and reads as player input to the pathfinder, so every path queued meanwhile would be dropped at once.
    private void CancelDescent()
    {
        if (Svc.Condition[ConditionFlag.InFlight])
        {
            UseGeneralAction(JumpGeneralActionId);
        }
    }

    // Straight up is the one direction known to be clear, since the flight came from there.
    private async Task BackOffInFlight(string scope)
    {
        if (Svc.Objects.LocalPlayer is not { } player || !Svc.Condition[ConditionFlag.InFlight])
        {
            return;
        }

        var above = player.Position with { Y = player.Position.Y + LandingBackOffMeters };
        Diag($"{scope}: backing off {LandingBackOffMeters:F0}m upward");
        NavmeshIPC.Instance.MoveAlong([above], fly: true);
        await DelayMs(LandingBackOffMs);
        NavmeshIPC.Instance.Stop();
    }

    // Each candidate is snapped to the highest floor under it. Landing on the spot takes the spot itself first and then
    // a ring around it; with a stand-off the ring comes first, nearest the character, and the spot itself is the last resort.
    private static int FindLandingSpots(Vector3 around, float standOffMeters, Vector3 from, bool allowUnlandable, Vector3[] spots)
    {
        var navmesh = NavmeshIPC.Instance;
        var standsOff = standOffMeters > LandsOnTheSpot;
        var found = 0;
        if (!standsOff && LandingFloor(navmesh, around, allowUnlandable) is { } center)
        {
            spots[found++] = center;
        }

        var radius = standsOff ? standOffMeters : LandingRingRadiusMeters;
        for (var step = 0; step < LandingRingPoints; step++)
        {
            var angle = MathF.Tau * step / LandingRingPoints;
            var candidate = around + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            if (LandingFloor(navmesh, candidate, allowUnlandable) is { } floor)
            {
                spots[found++] = floor;
            }
        }

        if (!standsOff)
        {
            return found;
        }

        SortNearestFirst(spots, found, from);
        if (LandingFloor(navmesh, around, allowUnlandable) is { } lastResort)
        {
            spots[found++] = lastResort;
        }

        return found;
    }

    private static void SortNearestFirst(Vector3[] spots, int count, Vector3 from)
    {
        for (var sortedCount = 1; sortedCount < count; sortedCount++)
        {
            var spot = spots[sortedCount];
            var distance = GroundDistance.SquaredBetween(from, spot);
            var slot = sortedCount;
            while (slot > 0 && GroundDistance.SquaredBetween(from, spots[slot - 1]) > distance)
            {
                spots[slot] = spots[slot - 1];
                slot--;
            }

            spots[slot] = spot;
        }
    }

    private static Vector3? LandingFloor(NavmeshIPC navmesh, Vector3 point, bool allowUnlandable)
        => navmesh.PointOnFloor(point with { Y = point.Y + LandingProbeLiftMeters }, allowUnlandable, LandingFloorHalfExtentMeters);
}
