using System.Numerics;
using System.Threading.Tasks;
using DungeonContext = AutoTreasureMaps.Core.Tasks.AutoCommon.DungeonContext;

namespace AutoTreasureMaps.Core.Dungeon;

// After a gate opens, the way on is a circle that carries the character to the next chamber. The circle is the
// profile's when it is listed, or else the event object that showed up near the gate once it opened. With neither,
// the character walks through the open gate. The transit is over once the character has been carried away.
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
    private const int ChamberSettleMs = 1_000;

    public static readonly WalkIntoCircleTransit Instance = new();

    private WalkIntoCircleTransit()
    {
    }

    public async Task<bool> Run(DungeonContext context, AdvanceOutcome advance)
    {
        context.Status = "Moving on to the next chamber";
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
            // The new chamber's objects arrive a moment after the jump.
            await context.WaitUntil(() => !context.Zoning, CarriedWaitMs, "next chamber settled");
            await context.DelayMs(ChamberSettleMs);
            context.Diag($"{Scope}: carried to ({DungeonField.PlayerPosition.X:F1}, {DungeonField.PlayerPosition.Y:F1}, {DungeonField.PlayerPosition.Z:F1}).");
        }

        return carried;
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
        context.Status = "Waiting for the draw";
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
