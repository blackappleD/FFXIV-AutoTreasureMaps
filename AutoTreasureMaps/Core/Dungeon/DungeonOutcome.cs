using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;
using System.Collections.Immutable;

namespace AutoTreasureMaps.Core.Dungeon;

internal enum DungeonOutcome : byte
{
    // The last round was looted and the run left the duty.
    Cleared,
    // The game sent the character out: a wrong gate, a losing draw, or the duty's own end.
    Expelled,
    Died,
    // Out of the dungeon without a word from the game, or the duty was left by hand.
    Left,
    // Nothing on the field the run knows how to act on.
    Stuck,
    InParty,
    Cancelled,
    Faulted,
}

internal readonly record struct DungeonResult(DungeonProfile Profile, DungeonOutcome Outcome, int Round, int Gil, ImmutableArray<ItemGain> Items, TimeSpan Duration)
{
    public int ItemCount => LootKinds.ItemCount(Items);

    public int Tomestones => LootKinds.TomestoneCount(Items);
}

internal static class DungeonOutcomes
{
    public static bool Final(DungeonOutcome outcome) => outcome is not (DungeonOutcome.Cancelled or DungeonOutcome.Faulted);

    public static LocString Label(DungeonOutcome outcome) => outcome switch
    {
        DungeonOutcome.Cleared => L.Dungeon.OutcomeCleared,
        DungeonOutcome.Expelled => L.Dungeon.OutcomeExpelled,
        DungeonOutcome.Died => L.Dungeon.OutcomeDied,
        DungeonOutcome.Left => L.Dungeon.OutcomeLeft,
        DungeonOutcome.Stuck => L.Dungeon.OutcomeStuck,
        DungeonOutcome.InParty => L.Dungeon.OutcomeInParty,
        DungeonOutcome.Faulted => L.MapRun.OutcomeFaulted,
        _ => L.MapRun.OutcomeCancelled,
    };
}
