namespace AutoTreasureMaps.Core.Stats;

// What one kind of map came to: how often it was run, how often its portal led into a treasure dungeon and on which
// floor each of those ended, and the rare loot from both. Kept per run and summed for the history.
[Serializable]
internal sealed class MapTally
{
    public int Maps { get; set; }
    public int Dungeons { get; set; }

    // Dungeons by the floor they ended on: the chamber reached in a gate dungeon, the draws made in a slot dungeon.
    public Dictionary<int, int> Floors { get; set; } = [];

    // Rare items only, by item id.
    public Dictionary<uint, int> Items { get; set; } = [];

    public void NoteFloor(int floor)
    {
        Dungeons++;
        Floors[floor] = Floors.GetValueOrDefault(floor) + 1;
    }

    public void NoteItem(uint itemId, int quantity)
        => Items[itemId] = Items.GetValueOrDefault(itemId) + quantity;

    public void Add(MapTally other)
    {
        Maps += other.Maps;
        Dungeons += other.Dungeons;
        foreach (var (floor, count) in other.Floors)
        {
            Floors[floor] = Floors.GetValueOrDefault(floor) + count;
        }

        foreach (var (itemId, quantity) in other.Items)
        {
            NoteItem(itemId, quantity);
        }
    }

    public MapTally Copy()
    {
        var copy = new MapTally();
        copy.Add(this);
        return copy;
    }

    public static Dictionary<uint, MapTally> Copy(Dictionary<uint, MapTally> tallies)
    {
        var copy = new Dictionary<uint, MapTally>(tallies.Count);
        foreach (var (mapItemId, tally) in tallies)
        {
            copy[mapItemId] = tally.Copy();
        }

        return copy;
    }

    // Adds each kind of map's tally into the sum for that kind.
    public static void AddAll(Dictionary<uint, MapTally> sums, Dictionary<uint, MapTally> tallies)
    {
        foreach (var (mapItemId, tally) in tallies)
        {
            if (!sums.TryGetValue(mapItemId, out var sum))
            {
                sum = new MapTally();
                sums[mapItemId] = sum;
            }

            sum.Add(tally);
        }
    }
}
