namespace AutoTreasureMaps.Core.Stats;

// The rare loot of each map, keyed by the timeworn map's item id, as the 稀有物品 section of the map's huijiwiki page
// lists it. Only these items are tallied per map and priced into the gil a run made; everything else stays in the plain
// item count.
internal static class RareLoot
{
    // G17 陈旧的狞豹革地图 and G18 陈旧的卡冈图亚革地图.
    public const uint BraaxskinMap = 43557;
    public const uint GargantuaskinMap = 46185;

    private const uint ItTakesTwo = 43606;        // 发型样式：二分区
    private const uint RibbonParasol = 44257;     // 缎带阳伞
    private const uint CatEyeGlasses = 45005;     // 面部配饰：猫眼眼镜
    private const uint CorduroyFelt = 45072;      // 灯芯绒布
    private const uint DumbbellRack = 46105;      // 健身哑铃架
    private const uint TrainingBench = 46106;     // 健身训练凳
    private const uint RememberRoll = 46147;      // 管弦乐琴乐谱：铭记
    private const uint ThinLinen = 46261;         // 薄亚麻布
    private const uint EnergyDrinkGg = 46773;     // 能量饮料GG
    private const uint ScherzoRoll = 48225;       // 管弦乐琴乐谱：诙谐的笑谈
    private const uint HeraldCloth = 50830;       // 神话传令官布料

    // Both pages list the same rare loot.
    private static readonly uint[] dawntrailRareLoot =
    [
        ItTakesTwo, RibbonParasol, CatEyeGlasses, CorduroyFelt, DumbbellRack, TrainingBench,
        RememberRoll, ThinLinen, EnergyDrinkGg, ScherzoRoll, HeraldCloth,
    ];

    private static readonly Dictionary<uint, uint[]> itemsByMap = new()
    {
        [GargantuaskinMap] = dawntrailRareLoot,
        [BraaxskinMap] = dawntrailRareLoot,
    };

    private static readonly uint[] allItems = itemsByMap.Values.SelectMany(static items => items).Distinct().ToArray();

    // Every item listed for any map, for one batched price request.
    public static ReadOnlySpan<uint> AllItems => allItems;

    public static ReadOnlySpan<uint> For(uint mapItemId)
        => itemsByMap.TryGetValue(mapItemId, out var items) ? items : [];

    // Listed for any map; the loot cleanup never discards these.
    public static bool IsRare(uint itemId) => Array.IndexOf(allItems, itemId) >= 0;

    public static bool Counts(uint mapItemId, uint itemId)
        => itemsByMap.TryGetValue(mapItemId, out var items) && Array.IndexOf(items, itemId) >= 0;
}
