using AutoTreasureMaps.Core.Dungeon;

namespace AutoTreasureMaps.Core.Stats;

// The rare loot of each map and of the treasure dungeon behind its portal, as huijiwiki lists it: the 稀有物品 section of
// the map's page and the 掉落物品 section of the dungeon's. Only these items are tallied per map and priced into the gil a
// run made; everything else stays in the plain item count. The loot cleanup keeps them unless the player picks one to
// discard.
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

    private const uint Leech = 43572;             // 吸蜜怪
    private const uint GreenGleam = 44494;        // 金曦绿茵号
    private const uint WavyBob = 44506;           // 发型样式：微卷短发
    private const uint NarrowGlasses = 45006;     // 面部配饰：窄框眼镜
    private const uint DreamscapeCoffer = 45070;  // 幻梦武器箱
    private const uint LambHorn = 46829;          // 羊角笛
    private const uint TacticalGoggles = 46842;   // 面部配饰：战术护目镜
    private const uint ProfessorGlasses = 46845;  // 面部配饰：教授眼镜
    private const uint Torch = 46848;             // 火炬
    private const uint WeeLamb = 47964;           // 小小羊
    private const uint Grabbycat = 47965;         // 索取小猫
    private const uint LeopardGlasses = 48152;    // 面部配饰：豹目眼镜
    private const uint FlatbreadRoll = 50795;     // 管弦乐琴乐谱：夹饼欢宴
    private const uint ValorRoll = 50797;         // 管弦乐琴乐谱：勇武
    private const uint SmilesRoll = 52357;        // 管弦乐琴乐谱：为了守护笑容
    private const uint MixingDesk = 49846;        // 永护塔混音台
    private const uint LambNeon = 49847;          // 羊羊壁挂霓虹灯
    private const uint ProjectionScreen = 51262;  // 永护投影屏
    private const uint StereoSpeakers = 51263;    // 永护立体声音箱
    private const uint AthleticCloth = 52630;     // 运动布
    private const uint IndigoTwill = 52631;       // 靛蓝斜纹布
    private const uint TwilightGem = 41805;       // 暮光宝石

    // Both map pages list the same rare loot.
    private static readonly uint[] dawntrailMapLoot =
    [
        ItTakesTwo, RibbonParasol, CatEyeGlasses, CorduroyFelt, DumbbellRack, TrainingBench,
        RememberRoll, ThinLinen, EnergyDrinkGg, ScherzoRoll, HeraldCloth,
    ];

    private static readonly uint[] cenoteLoot =
    [
        Leech, GreenGleam, WavyBob, CatEyeGlasses, NarrowGlasses, RememberRoll, TacticalGoggles, ProfessorGlasses, Torch,
        LeopardGlasses, FlatbreadRoll, ValorRoll, SmilesRoll, DreamscapeCoffer, MixingDesk, LambNeon, ProjectionScreen,
        StereoSpeakers, ThinLinen, HeraldCloth, AthleticCloth, IndigoTwill, TwilightGem,
    ];

    // The vault drops all the cenote does, and the lamb horn and two minions on top.
    private static readonly uint[] vaultLoot =
    [
        Leech, GreenGleam, WavyBob, CatEyeGlasses, NarrowGlasses, RememberRoll, LambHorn, TacticalGoggles, ProfessorGlasses,
        Torch, WeeLamb, Grabbycat, LeopardGlasses, FlatbreadRoll, ValorRoll, SmilesRoll, DreamscapeCoffer, MixingDesk,
        LambNeon, ProjectionScreen, StereoSpeakers, ThinLinen, HeraldCloth, AthleticCloth, IndigoTwill, TwilightGem,
    ];

    // Highest tier first, each map followed by its dungeon; a dungeon source names its ContentFinderCondition.
    private static readonly Source[] sources =
    [
        new(GargantuaskinMap, 0, dawntrailMapLoot),
        new(GargantuaskinMap, DungeonProfiles.VaultOneiron.ContentFinderConditionId, vaultLoot),
        new(BraaxskinMap, 0, dawntrailMapLoot),
        new(BraaxskinMap, DungeonProfiles.CenoteJaJaGural.ContentFinderConditionId, cenoteLoot),
    ];

    // A map's coffer and its dungeon together, the map's own loot first.
    private static readonly Dictionary<uint, uint[]> itemsByMap = sources
        .GroupBy(static source => source.MapItemId)
        .ToDictionary(static group => group.Key, static group => group.SelectMany(static source => source.Items).Distinct().ToArray());

    private static readonly uint[] allItems = sources.SelectMany(static source => source.Items).Distinct().ToArray();

    public sealed record Source(uint MapItemId, uint ContentFinderConditionId, uint[] Items)
    {
        public bool IsDungeon => ContentFinderConditionId != 0;
    }

    public static ReadOnlySpan<Source> Sources => sources;

    // Every item listed for any map or dungeon, for one batched price request.
    public static ReadOnlySpan<uint> AllItems => allItems;

    public static ReadOnlySpan<uint> For(uint mapItemId)
        => itemsByMap.TryGetValue(mapItemId, out var items) ? items : [];

    public static bool IsRare(uint itemId) => Array.IndexOf(allItems, itemId) >= 0;

    public static bool Counts(uint mapItemId, uint itemId)
        => itemsByMap.TryGetValue(mapItemId, out var items) && Array.IndexOf(items, itemId) >= 0;
}
