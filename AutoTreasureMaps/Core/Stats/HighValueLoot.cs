namespace AutoTreasureMaps.Core.Stats;

// The loot worth counting on its own for each map, keyed by the timeworn map's item id. Only these items are tallied
// per map and priced into the gil a run made; everything else stays in the plain item count.
internal static class HighValueLoot
{
    // G17 陈旧的狞豹革地图 and G18 陈旧的卡冈图亚革地图.
    public const uint BraaxskinMap = 43557;
    public const uint GargantuaskinMap = 46185;

    private const uint DreamscapeWeaponCoffer = 45070; // 幻梦武器箱
    private const uint LambHorn = 46829;               // 羊角笛
    private const uint WeeLamb = 47964;                // 小小羊
    private const uint EnergyDrinkGg = 46773;          // 能量饮料GG
    private const uint Grabbycat = 47965;              // 索取小猫
    private const uint AthleticCloth = 52630;          // 运动布
    private const uint IndigoTwill = 52631;            // 靛蓝斜纹布

    private static readonly Dictionary<uint, uint[]> itemsByMap = new()
    {
        [GargantuaskinMap] = [DreamscapeWeaponCoffer, LambHorn, WeeLamb, EnergyDrinkGg, Grabbycat],
        [BraaxskinMap] = [DreamscapeWeaponCoffer, IndigoTwill, AthleticCloth],
    };

    private static readonly uint[] allItems = itemsByMap.Values.SelectMany(static items => items).Distinct().ToArray();

    // Every item listed for any map, for one batched price request.
    public static ReadOnlySpan<uint> AllItems => allItems;

    public static ReadOnlySpan<uint> For(uint mapItemId)
        => itemsByMap.TryGetValue(mapItemId, out var items) ? items : [];

    // Listed for any map; the loot cleanup never discards these.
    public static bool IsHighValue(uint itemId) => Array.IndexOf(allItems, itemId) >= 0;

    public static bool Counts(uint mapItemId, uint itemId)
        => itemsByMap.TryGetValue(mapItemId, out var items) && Array.IndexOf(items, itemId) >= 0;
}
