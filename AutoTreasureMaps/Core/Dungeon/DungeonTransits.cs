using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using ECommons;
using System.Numerics;
using System.Threading.Tasks;
using DungeonContext = AutoTreasureMaps.Core.Tasks.AutoCommon.DungeonContext;

namespace AutoTreasureMaps.Core.Dungeon;

// After a gate opens, the way on is a circle that carries the character to the next chamber. The circle is placed by
// the profile where it knows it, or else it is the listed event object, or the one that showed up near the gate once
// it opened. With none of these, the character walks through the open gate. The transit is over once the character
// has been carried away and has landed.
internal sealed class WalkIntoCircleTransit : ITransitStrategy
{
    private const string Scope = "dungeon-transit";
    private const int CircleAppearMs = 8_000;
    private const float CircleSearchMeters = 40f;
    private const float CircleArriveMeters = 0.5f;
    // Past the gate this far, in the line the character walked to it, is inside the next chamber's way.
    private const float ThroughGateMeters = 8f;
    private const int WalkTimeoutMs = 30_000;
    private const int CarriedWaitMs = 10_000;
    // A jump this far from where the walk began is the circle's teleport.
    private const float CarriedMeters = 25f;
    // The character stands about 20m from a placed circle when the walk begins and only ever comes closer on foot.
    private const float CarriedFromCircleMeters = 30f;
    private const int FreeToWalkMs = 5_000;
    private const int StraightWalkMs = 12_000;
    // The circle flies the character on and it cannot act on the way; it has landed once it is free and holds still.
    private const int LandingWaitMs = 40_000;
    private const int LandedStillMs = 1_000;
    private const float StillMeters = 0.05f;

    public static readonly WalkIntoCircleTransit Instance = new();

    private WalkIntoCircleTransit()
    {
    }

    public async Task<bool> Run(DungeonContext context, AdvanceOutcome advance)
    {
        context.Status = Loc.T(L.Status.NextChamber);
        if (context.Profile.CircleBehind(advance.BaseId) is { } placed)
        {
            return await WalkIntoPlacedCircle(context, advance, placed);
        }

        var start = DungeonField.PlayerPosition;
        bool Carried() => context.Zoning || !context.InDungeon || Vector3.Distance(DungeonField.PlayerPosition, start) > CarriedMeters;

        await context.WaitUntil(() => DungeonField.NearestTransit(context.Profile, advance.At, CircleSearchMeters, advance.ObjectsBefore) is not null || Carried(), CircleAppearMs, "transit circle");
        if (context.Cancelled)
        {
            return false;
        }

        if (Carried())
        {
            context.Diag($"{Scope}: carried on before the circle was walked into.");
            return true;
        }

        Vector3 destination;
        if (DungeonField.NearestTransit(context.Profile, advance.At, CircleSearchMeters, advance.ObjectsBefore) is { } circle)
        {
            destination = circle.Position;
            context.Diag($"{Scope}: walking into {circle.ObjectKind} base {circle.BaseId} '{circle.Name}' at ({destination.X:F1}, {destination.Y:F1}, {destination.Z:F1}){(context.Profile.IsTransit(circle.BaseId) ? string.Empty : ", which appeared once the gate opened")}.");
        }
        else
        {
            var through = advance.At - start;
            through.Y = 0f;
            destination = through.LengthSquared() > 0.01f ? advance.At + Vector3.Normalize(through) * ThroughGateMeters : advance.At;
            context.Diag($"{Scope}: no circle showed within {CircleAppearMs / TimeUnits.MillisecondsPerSecond}s; walking through the gate to ({destination.X:F1}, {destination.Y:F1}, {destination.Z:F1}).");
        }

        await context.WalkTo(destination, CircleArriveMeters, Carried, WalkTimeoutMs, Scope);
        var carried = await context.WaitUntil(Carried, CarriedWaitMs, "carried to the next chamber");
        if (!carried && !context.Cancelled)
        {
            context.Warn($"{Scope}: still {Vector3.Distance(DungeonField.PlayerPosition, start):F1}m from where the walk began; the circle did not carry the character.");
        }

        if (carried)
        {
            await WaitForLanding(context);
        }

        return carried;
    }

    // The gate is still a wall to the navmesh, so the walk goes straight through its middle into the circle, and only
    // asks the navmesh for a way when that does not carry the character on.
    private static async Task<bool> WalkIntoPlacedCircle(DungeonContext context, AdvanceOutcome advance, Vector3 circle)
    {
        bool Carried() => context.Zoning || !context.InDungeon || Vector3.Distance(DungeonField.PlayerPosition, circle) > CarriedFromCircleMeters;

        // Opening the gate holds the character for a moment.
        await context.WaitUntil(() => !GenericHelpers.IsOccupied(), FreeToWalkMs, "free to walk through the gate");
        var through = advance.At with { Y = circle.Y };
        context.Diag($"{Scope}: walking through gate {advance.BaseId} at ({through.X:F1}, {through.Z:F1}) into its circle at ({circle.X:F1}, {circle.Y:F1}, {circle.Z:F1}).");
        var navmesh = NavmeshIPC.Instance;
        navmesh.MoveAlong([through, circle], fly: false);
        var carried = await context.WaitUntil(Carried, StraightWalkMs, "carried by the circle");
        navmesh.Stop();
        if (!carried && !context.Cancelled)
        {
            context.Diag($"{Scope}: the straight walk did not reach the circle ({Vector3.Distance(DungeonField.PlayerPosition, circle):F1}m off); asking the navmesh for a way.");
            await context.WalkTo(circle, CircleArriveMeters, Carried, WalkTimeoutMs, Scope);
            carried = await context.WaitUntil(Carried, CarriedWaitMs, "carried by the circle");
        }

        if (!carried)
        {
            if (!context.Cancelled)
            {
                context.Warn($"{Scope}: still {Vector3.Distance(DungeonField.PlayerPosition, circle):F1}m from the circle behind gate {advance.BaseId}; it did not carry the character.");
            }

            return false;
        }

        await WaitForLanding(context);
        return true;
    }

    // The flight to the next chamber takes a while and the character cannot act until it lands; the new chamber's
    // objects arrive with it.
    private static async Task WaitForLanding(DungeonContext context)
    {
        var last = DungeonField.PlayerPosition;
        var stillSince = Environment.TickCount64;
        bool Landed()
        {
            var now = Environment.TickCount64;
            var position = DungeonField.PlayerPosition;
            if (context.Zoning || GenericHelpers.IsOccupied() || Vector3.DistanceSquared(position, last) > StillMeters * StillMeters)
            {
                last = position;
                stillSince = now;
                return false;
            }

            return now - stillSince >= LandedStillMs;
        }

        var landed = await context.WaitUntil(Landed, LandingWaitMs, "landed in the next chamber");
        var at = DungeonField.PlayerPosition;
        context.Diag($"{Scope}: {(landed ? "landed" : "still on the move")} at ({at.X:F1}, {at.Y:F1}, {at.Z:F1}).");
    }
}

// A slot dungeon stays in its room: the draw is followed by enemies, a bonus round, a coffer or the way out, and the
// run waits for whichever comes before looking at the field again.
internal sealed class StayInPlaceTransit : ITransitStrategy
{
    private const int DrawResultMs = 20_000;
    private const int FinalNotion = 4;

    public static readonly StayInPlaceTransit Instance = new();

    private StayInPlaceTransit()
    {
    }

    private static ReadOnlySpan<uint> Results => [DungeonLogMessages.SlotEnvisions, DungeonLogMessages.BonusRoundStarted, DungeonLogMessages.VaultExpelled];

    public async Task<bool> Run(DungeonContext context, AdvanceOutcome advance)
    {
        context.Status = Loc.T(L.Status.WaitingDraw);
        var shown = await context.WaitUntil(
            () => context.TryGetMessage(Results, advance.SinceMs, out _) || context.FieldBusy() || context.Zoning || !context.InDungeon,
            DrawResultMs,
            "draw result");
        if (context.TryGetMessage(Results, advance.SinceMs, out var result))
        {
            var drawn = result.Id == DungeonLogMessages.SlotEnvisions && result.TryGetParameter(0, out var notion) ? notion : 0;
            if (drawn == FinalNotion)
            {
                context.Dungeon.MarkFinalRound();
            }

            context.Diag($"dungeon-draw: {DungeonLogMessages.NameOf(result.Id)}{(drawn == 0 ? string.Empty : $", notion {drawn}")}, round {context.Dungeon.Round} of {context.Profile.Rounds}.");
        }

        return shown;
    }
}
