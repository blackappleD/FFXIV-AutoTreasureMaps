using AutoTreasureMaps.Core.Tasks;
using AutoTreasureMaps.Core.Treasure;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AutoTreasureMaps.Core.Stats;

// Fields added after the first build default when an older history file lacks them, and a null end reads as unknown.
// The end and the refusal are stored by name, so reordering either list cannot misread an old record.
[Serializable]
internal sealed class RunRecord
{
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public double DurationSeconds { get; set; }

    // Maps looted.
    public int MapsCompleted { get; set; }

    public int MapsRun { get; set; }
    public int MapsTimedOut { get; set; }
    public int MapsKnockedOut { get; set; }
    public int MapsDigFailed { get; set; }
    public int MapsUnreachable { get; set; }
    public int MapsFaulted { get; set; }
    public int PortalsSeen { get; set; }
    public int DungeonsRun { get; set; }
    public int DungeonsCleared { get; set; }
    public int Gil { get; set; }
    public int ItemsGained { get; set; }
    public int MapsGathered { get; set; }
    public Dictionary<uint, int>? NotableItems { get; set; }

    // Per kind of map, by the timeworn map's item id.
    public Dictionary<uint, MapTally>? Tallies { get; set; }
    public int MapsBought { get; set; }

    // Market tax included.
    public int GilSpentOnMaps { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public RunEnd? EndedBy { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public MapRefusal Refusal { get; set; }

    public string JobAbbreviation { get; set; } = "";

    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
    public int NetGil => Gil - GilSpentOnMaps;
    public double MapsPerHour => DurationSeconds > 0 ? MapsCompleted / (DurationSeconds / 3600.0) : 0;
}
