using Newtonsoft.Json;
using System.IO;

namespace AutoTreasureMaps.Core.Stats;

// Kept out of the main config so a long history can't bloat or corrupt the settings file. Disk access is
// best-effort: a failed read or write degrades to an empty or unsaved history instead of throwing.
internal sealed class RunHistory
{
    private const int MaxRecords = 500;
    private const string FileName = "run-history.json";

    public RunHistory()
    {
        Load();
        RecomputeLifetime();
    }

    public List<RunRecord> Records { get; private set; } = [];

    public LifetimeTotals Lifetime { get; private set; }

    // Every recorded run's tallies summed per kind of map.
    public Dictionary<uint, MapTally> LifetimeTallies { get; private set; } = [];

    // Bumped on every change, so captions built from the history know when to rebuild.
    public int Revision { get; private set; }

    private static string FilePath => CharacterData.PathOf(FileName);

    // The character that logged in has a history of its own.
    public void Reload()
    {
        Records = [];
        Load();
        RecomputeLifetime();
    }

    public void Append(RunRecord record)
    {
        Records.Add(record);
        Records.Sort((left, right) => right.EndedAtUtc.CompareTo(left.EndedAtUtc));
        if (Records.Count > MaxRecords)
        {
            Records.RemoveRange(MaxRecords, Records.Count - MaxRecords);
        }

        RecomputeLifetime();
        Save();
    }

    public void Clear()
    {
        Records.Clear();
        RecomputeLifetime();
        Save();
    }

    private void Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                return;
            }

            var records = JsonConvert.DeserializeObject<List<RunRecord>>(File.ReadAllText(path));
            if (records is not null)
            {
                Records = records;
                UpgradeOldRecords(records);
            }
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "RunHistory load failed; starting empty");
        }
    }

    // A record from before maps run were counted only knows the maps it looted, which it certainly ran.
    private static void UpgradeOldRecords(List<RunRecord> records)
    {
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            record.MapsRun = Math.Max(record.MapsRun, record.MapsCompleted);
        }
    }

    private void RecomputeLifetime()
    {
        var totals = new LifetimeTotals { Runs = Records.Count };
        var tallies = new Dictionary<uint, MapTally>();
        for (var index = 0; index < Records.Count; index++)
        {
            var record = Records[index];
            totals.Maps += record.MapsCompleted;
            totals.MapsRun += record.MapsRun;
            totals.GilLooted += record.Gil;
            totals.GilSpent += record.GilSpentOnMaps;
            totals.Portals += record.PortalsSeen;
            totals.Tomestones += record.Tomestones;
            totals.Seconds += record.DurationSeconds;
            if (record.Tallies is not null)
            {
                MapTally.AddAll(tallies, record.Tallies);
            }
        }

        Lifetime = totals;
        LifetimeTallies = tallies;
        Revision++;
    }

    private void Save()
    {
        try
        {
            CharacterData.EnsureFolder();

            File.WriteAllText(FilePath, JsonConvert.SerializeObject(Records, Formatting.Indented));
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "RunHistory save failed");
        }
    }

    public struct LifetimeTotals
    {
        public int Runs;
        public int Maps;
        public int MapsRun;

        public long GilLooted;
        public long GilSpent;
        public int Portals;
        public int Tomestones;
        public double Seconds;

        public readonly double MapsPerHour => Seconds > 0 ? Maps / (Seconds / 3600.0) : 0;
        public readonly TimeSpan Duration => TimeSpan.FromSeconds(Seconds);
    }
}
