using AutoTreasureMaps.Core.Treasure;
using Newtonsoft.Json;
using System.Collections.Immutable;
using System.IO;

namespace AutoTreasureMaps.Core.Stats;

// One line per map the runs carried to an end, numbered from 1, with the dungeon its portal led to folded in. Kept in
// its own file like the run history, and as forgiving of a failed read or write.
[Serializable]
internal sealed class MapLogEntry
{
    public int Number { get; set; }
    public DateTime AtUtc { get; set; }

    // The timeworn map's item id.
    public uint MapItemId { get; set; }

    public bool Portal { get; set; }

    // The treasure dungeon's last round reached, or 0 when the map led to none.
    public int Floor { get; set; }

    // How many rounds that dungeon has, so the floor reads against it; 0 on lines written before it was kept.
    public int Rounds { get; set; }

    public int Deaths { get; set; }

    // The high-value loot of the map and its dungeon, by item id.
    public Dictionary<uint, int>? Loot { get; set; }
}

internal sealed class MapLog
{
    private const int MaxEntries = 2_000;
    private const string FileName = "map-log.json";

    public MapLog()
    {
        Load();
    }

    // Oldest first.
    public List<MapLogEntry> Entries { get; private set; } = [];

    // Bumped on every change, so text built from the log knows when to rebuild.
    public int Revision { get; private set; }

    private static string FilePath
        => Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, FileName);

    public MapLogEntry Add(uint mapItemId, bool portal, int deaths, ImmutableArray<ItemGain> gains)
    {
        var entry = new MapLogEntry
        {
            Number = (Entries.Count == 0 ? 0 : Entries[^1].Number) + 1,
            AtUtc = DateTime.UtcNow,
            MapItemId = mapItemId,
            Portal = portal,
            Deaths = deaths,
        };
        AddLoot(entry, gains);
        Entries.Add(entry);
        if (Entries.Count > MaxEntries)
        {
            Entries.RemoveRange(0, Entries.Count - MaxEntries);
        }

        Changed();
        return entry;
    }

    public void NoteDungeon(MapLogEntry entry, int floor, int rounds, bool died, ImmutableArray<ItemGain> gains)
    {
        entry.Portal = true;
        entry.Floor = Math.Max(entry.Floor, floor);
        entry.Rounds = rounds;
        if (died)
        {
            entry.Deaths++;
        }

        AddLoot(entry, gains);
        Changed();
    }

    public void Clear()
    {
        Entries.Clear();
        Changed();
    }

    private static void AddLoot(MapLogEntry entry, ImmutableArray<ItemGain> gains)
    {
        if (gains.IsDefaultOrEmpty)
        {
            return;
        }

        var items = gains.AsSpan();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (!HighValueLoot.IsHighValue(item.ItemId))
            {
                continue;
            }

            entry.Loot ??= [];
            entry.Loot[item.ItemId] = entry.Loot.GetValueOrDefault(item.ItemId) + item.Quantity;
        }
    }

    private void Changed()
    {
        Revision++;
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

            var entries = JsonConvert.DeserializeObject<List<MapLogEntry>>(File.ReadAllText(path));
            if (entries is not null)
            {
                Entries = entries;
            }
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "MapLog load failed; starting empty");
        }
    }

    private void Save()
    {
        try
        {
            var directory = Plugin.PluginInterface.ConfigDirectory;
            if (!directory.Exists)
            {
                directory.Create();
            }

            File.WriteAllText(FilePath, JsonConvert.SerializeObject(Entries, Formatting.Indented));
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "MapLog save failed");
        }
    }
}
