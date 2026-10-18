using AutoTreasureMaps.Core.Localization;

namespace AutoTreasureMaps.Core.Tasks;

// How a run ended. History stores the name, so the order may change.
internal enum RunEnd : byte
{
    Stopped,
    OutOfMaps,
    MapLimit,
    TimeLimit,
    FailureStreak,
    KnockedOutTwice,
    Refused,
    SupplyFailed,
    StillKnockedOut,
    Faulted,
    SingleMap,
    Dungeon,
}

internal static class RunEnds
{
    // A run that ended any of these ways did what it was set to do, so the after-run action may follow it.
    public static bool MetStopCondition(RunEnd end)
        => end is RunEnd.OutOfMaps or RunEnd.MapLimit or RunEnd.TimeLimit or RunEnd.FailureStreak or RunEnd.KnockedOutTwice or RunEnd.SingleMap or RunEnd.Dungeon;

    public static LocString Label(RunEnd end) => end switch
    {
        RunEnd.OutOfMaps => L.Run.EndOutOfMaps,
        RunEnd.MapLimit => L.Run.EndMapLimit,
        RunEnd.TimeLimit => L.Run.EndTimeLimit,
        RunEnd.FailureStreak => L.Run.EndFailureStreak,
        RunEnd.KnockedOutTwice => L.Run.EndKnockedOutTwice,
        RunEnd.Refused => L.Run.EndRefused,
        RunEnd.SupplyFailed => L.Run.EndSupplyFailed,
        RunEnd.StillKnockedOut => L.Run.EndStillKnockedOut,
        RunEnd.Faulted => L.Run.EndFaulted,
        RunEnd.SingleMap => L.Run.EndSingleMap,
        RunEnd.Dungeon => L.Run.EndDungeon,
        _ => L.Run.EndStopped,
    };
}
