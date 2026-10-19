using System.Collections.Frozen;

namespace AutoTreasureMaps.Core.Treasure;

// Major 0 means the patch is not known.
internal readonly record struct GamePatch(byte Major, byte Minor)
{
    public bool IsKnown => Major != 0;

    public override string ToString() => $"{Major}.{Minor}";
}

// The patch each map arrived in, as the FFXIV wiki's treasure hunt page lists it (ff14.huijiwiki.com/wiki/寻宝).
// The game data does not carry it. Keyed by the timeworn map's item id; a map the wiki leaves out has no patch.
internal static class MapPatches
{
    private static readonly FrozenDictionary<uint, GamePatch> byItem = new Dictionary<uint, GamePatch>
    {
        // Leather, goatskin, toadskin, boarskin, peisteskin
        [6688] = new(2, 1), [6689] = new(2, 1), [6690] = new(2, 1), [6691] = new(2, 1), [6692] = new(2, 1),
        // Unhidden leather map
        [8156] = new(2, 3),
        // Archaeoskin, wyvernskin, dragonskin
        [12241] = new(3, 0), [12242] = new(3, 0), [12243] = new(3, 0),
        // Gaganaskin, gazelleskin
        [17835] = new(4, 0), [17836] = new(4, 0),
        // Deep dungeon portal map
        [19770] = new(4, 1),
        // Gliderskin, zonureskin
        [26744] = new(5, 0), [26745] = new(5, 0),
        // Special maps 1 and 2
        [24794] = new(5, 2), [33328] = new(5, 5),
        // Saigaskin, kumbhiraskin
        [36611] = new(6, 0), [36612] = new(6, 0),
        // Ophiotauroskin, special maps 3 and 4
        [39591] = new(6, 3), [39593] = new(6, 3), [39918] = new(6, 4),
        // Loboskin, br'aaxskin
        [43556] = new(7, 0), [43557] = new(7, 0),
        // Gargantuaskin, special map 5
        [46185] = new(7, 3), [44349] = new(7, 5),
    }.ToFrozenDictionary();

    public static GamePatch Of(uint itemId) => byItem.TryGetValue(itemId, out var patch) ? patch : default;
}
