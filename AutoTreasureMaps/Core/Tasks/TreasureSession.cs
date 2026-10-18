using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Treasure;
using ECommons.DalamudServices;
using System.Collections.Immutable;
using System.Threading;

namespace AutoTreasureMaps.Core.Tasks;

// One run of the controller: the clock, the job, what the maps came to and how the run ended. It outlives a pause,
// since every resume builds the run task afresh.
public sealed class TreasureSession
{
    private readonly HashSet<byte> marketSkippedRanks = [];
    private long pausedMs;
    private long pauseStartedAtMs;
    private DateTime? endedAt;

    public TreasureSession()
    {
        JobAbbreviation = CurrentJobAbbreviation();
    }

    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public string JobAbbreviation { get; private set; }

    // Maps looted.
    public int MapsCompleted { get; private set; }

    // Maps carried to an end: looted, failed, or dug up into a portal. A refused map is still held and does not count.
    public int MapsRun { get; private set; }

    public int MapsTimedOut { get; private set; }

    public int MapsKnockedOut { get; private set; }

    public int MapsDigFailed { get; private set; }

    public int MapsUnreachable { get; private set; }

    // Faults during a map; the map itself resumes after the restart, so it is counted again once it ends.
    public int MapsFaulted { get; private set; }

    public int PortalsSeen { get; private set; }

    // Treasure dungeons carried to an end, and those of them cleared to the last round.
    public int DungeonsRun { get; private set; }

    public int DungeonsCleared { get; private set; }

    public int Gil { get; private set; }

    public int ItemsGained { get; private set; }

    public int MapsBought { get; private set; }

    // Market tax included.
    public int GilSpentOnMaps { get; private set; }

    // Daily maps gathered as a Miner or Botanist.
    public int MapsGathered { get; private set; }

    // Maps in a row that ended with nothing dug up, out of reach or on an error; any other end starts it over.
    public int FailedInARow { get; private set; }

    public bool EndedWithFault { get; private set; }

    public bool CompletedByStopCondition { get; private set; }

    internal bool Recorded;
    internal bool AfterActionDispatched;

    // The map under way, or null between maps; a pause or a fault restart resumes its hunt from here.
    internal MapInProgress? ActiveMap;

    // The treasure dungeon under way, or null outside one; a pause or a fault restart resumes it from here.
    internal DungeonInProgress? ActiveDungeon;

    internal Dictionary<uint, int> NotableItems { get; } = [];

    // Null while the run goes on, and for a run stopped from outside before it decided.
    internal RunEnd? EndedBy { get; private set; }

    internal MapRefusal EndRefusal { get; private set; }

    // Set once the game refused a purchase for want of gil; the run buys nothing more.
    internal bool MarketStopped { get; private set; }

    // Set once the daily map gather was tried. A node shows the map only by chance, so a run that came back without it
    // does not go back after every map.
    internal bool DailyGatherTried { get; private set; }

    // A run that only bought or gathered a map still spent gil or the day's allowance, so it is recorded.
    public bool DidNothing => MapsRun == 0 && MapsBought == 0 && MapsGathered == 0 && DungeonsRun == 0;

    public int MapsFailed => MapsTimedOut + MapsKnockedOut + MapsDigFailed + MapsUnreachable;

    public TimeSpan Elapsed => (endedAt ?? DateTime.UtcNow) - StartedAt - TimeSpan.FromMilliseconds(PausedTotalMs);

    private long PausedTotalMs
        => pausedMs + (pauseStartedAtMs == 0 ? 0 : Environment.TickCount64 - pauseStartedAtMs);

    // Gains count only once a map has ended: a faulted map is measured again from the same bags when it resumes.
    internal void RecordMap(in MapResult result)
    {
        switch (result.Outcome)
        {
            case MapOutcome.Cancelled:
                return;
            case MapOutcome.Faulted:
                MapsFaulted++;
                FailedInARow++;
                return;
            case MapOutcome.Refused when result.Refusal != MapRefusal.PortalDug:
                return;
        }

        MapsRun++;
        Gil += result.Gil;
        ItemsGained += result.ItemCount;
        if (result.PortalSeen || result.Refusal == MapRefusal.PortalDug)
        {
            PortalsSeen++;
        }

        NoteNotableItems(result.Items);
        CountOutcome(result.Outcome);
    }

    // A dungeon's gains count once it has ended, like a map's.
    internal void RecordDungeon(in DungeonResult result)
    {
        if (!DungeonOutcomes.Final(result.Outcome))
        {
            return;
        }

        DungeonsRun++;
        if (result.Outcome == DungeonOutcome.Cleared)
        {
            DungeonsCleared++;
        }

        Gil += result.Gil;
        ItemsGained += result.ItemCount;
        NoteNotableItems(result.Items);
    }

    internal void RecordPurchase(int gil)
    {
        MapsBought++;
        GilSpentOnMaps += gil;
    }

    internal void StopMarket() => MarketStopped = true;

    // A tier the game says is already held somewhere the bags do not show is left for the rest of the run.
    internal void SkipMarketTier(byte rank) => marketSkippedRanks.Add(rank);

    internal bool MarketSkips(byte rank) => marketSkippedRanks.Contains(rank);

    internal void Finish(RunEnd end, MapRefusal refusal = MapRefusal.None)
    {
        EndedBy = end;
        EndRefusal = refusal;
        CompletedByStopCondition = RunEnds.MetStopCondition(end);
    }

    internal void NoteMapGathered() => MapsGathered++;

    internal void NoteDailyGatherTried() => DailyGatherTried = true;

    // A run stopped from outside keeps the end it had already decided on.
    internal void MarkStopped() => EndedBy ??= RunEnd.Stopped;

    public void Sample()
    {
        if (JobAbbreviation.Length == 0)
        {
            JobAbbreviation = CurrentJobAbbreviation();
        }
    }

    // A Stop unwinds the run loop through the same catch as a genuine fault, and only a genuine fault may resume the run.
    public void RecordFault(Exception exception, CancellationToken cancelToken)
    {
        if (cancelToken.IsCancellationRequested || exception is OperationCanceledException)
        {
            return;
        }

        EndedWithFault = true;
        // clib's task runner writes the same exception to dalamud.log when the task unwinds.
        RunLog.Record(RunLogLevel.Error, exception, "The treasure task ended with an unexpected error");
    }

    internal void ClearFault() => EndedWithFault = false;

    public void BeginPause()
    {
        if (pauseStartedAtMs != 0)
        {
            return;
        }

        pauseStartedAtMs = Environment.TickCount64;
    }

    public void EndPause()
    {
        if (pauseStartedAtMs == 0)
        {
            return;
        }

        pausedMs += Environment.TickCount64 - pauseStartedAtMs;
        pauseStartedAtMs = 0;
    }

    // Freezes the clock, so a finished run left on screen during its after-run action stops counting.
    internal void End()
    {
        if (endedAt is not null)
        {
            return;
        }

        EndPause();
        endedAt = DateTime.UtcNow;
    }

    private void CountOutcome(MapOutcome outcome)
    {
        switch (outcome)
        {
            case MapOutcome.Looted:
                MapsCompleted++;
                FailedInARow = 0;
                break;
            case MapOutcome.TimedOut:
                MapsTimedOut++;
                FailedInARow = 0;
                break;
            case MapOutcome.Died:
                MapsKnockedOut++;
                FailedInARow = 0;
                break;
            case MapOutcome.DigFailed:
                MapsDigFailed++;
                FailedInARow++;
                break;
            case MapOutcome.Unreachable:
                MapsUnreachable++;
                FailedInARow++;
                break;
        }
    }

    private void NoteNotableItems(ImmutableArray<ItemGain> gains)
    {
        if (gains.IsDefaultOrEmpty)
        {
            return;
        }

        var items = gains.AsSpan();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (NotableLoot.IsNotable(item.ItemId))
            {
                NotableItems[item.ItemId] = NotableItems.GetValueOrDefault(item.ItemId) + item.Quantity;
            }
        }
    }

    private static string CurrentJobAbbreviation()
        => Svc.Objects.LocalPlayer?.ClassJob.ValueNullable?.Abbreviation.ExtractText() ?? string.Empty;
}
