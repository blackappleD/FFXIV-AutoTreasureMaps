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

    // Seconds from the map's start to its end, or to the end of the dungeon its portal led to; 0 on lines written
    // before it was kept.
    public int Seconds { get; set; }

    // The rare loot of the map and its dungeon, by item id.
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

    private static string FilePath => CharacterData.PathOf(FileName);

    // The character that logged in has a log of its own.
    public void Reload()
    {
        Entries = [];
        Load();
        Revision++;
    }

    public MapLogEntry Add(uint mapItemId, bool portal, int deaths, TimeSpan duration, ImmutableArray<ItemGain> gains)
    {
        var entry = new MapLogEntry
        {
            Number = (Entries.Count == 0 ? 0 : Entries[^1].Number) + 1,
            AtUtc = DateTime.UtcNow,
            MapItemId = mapItemId,
            Portal = portal,
            Deaths = deaths,
            Seconds = (int)duration.TotalSeconds,
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

    // The time runs on from the line's map to the dungeon's end, through the walk to the portal and the load in.
    public void NoteDungeon(MapLogEntry entry, int floor, int rounds, bool died, ImmutableArray<ItemGain> gains)
    {
        entry.Seconds += (int)(DateTime.UtcNow - entry.AtUtc).TotalSeconds;
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
            if (!RareLoot.IsRare(item.ItemId))
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
            // The unreadable file is kept as a .bak, so the next save does not overwrite the only copy of the log.
            RunLog.Warning(exception, "MapLog load failed; starting empty and keeping the file as map-log.json.bak");
            try
            {
                File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            }
            catch (Exception copyException)
            {
                RunLog.Warning(copyException, "MapLog: could not keep the unreadable file");
            }
        }
    }

    private void Save()
    {
        try
        {
            CharacterData.EnsureFolder();

            // Written beside the log and moved over it, so a crash mid-write cannot leave half a file behind.
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(Entries, Formatting.None));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "MapLog save failed");
        }
    }
}
