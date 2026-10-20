using AutoTreasureMaps.Core.Dungeon;

namespace AutoTreasureMaps.Core.Stats;

// The rare loot of each map and of the treasure dungeon behind its portal, as huijiwiki lists it (see
// THIRD-PARTY-NOTICES.md): the rare items section of the map's page and the drops section of the dungeon's. Only these
// items are tallied per map and priced into the gil a run made; everything else stays in the plain item count. The loot
// cleanup keeps them unless the player picks one to discard.
internal static class RareLoot
{
    // G17 Timeworn Br'aaxskin Map and G18 Timeworn Gargantuaskin Map.
    public const uint BraaxskinMap = 43557;
    public const uint GargantuaskinMap = 46185;

    private const uint ItTakesTwo = 43606;
    private const uint RibbonedParasol = 44257;
    private const uint CatEyeGlasses = 45005;
    private const uint CorduroyFelt = 45072;
    private const uint DumbbellRack = 46105;
    private const uint TrainingBench = 46106;
    private const uint EtchedInMemoryRoll = 46147;
    private const uint SummerlightLinen = 46261;
    private const uint Dam = 46773;
    private const uint FunAndGamesRoll = 48225;
    private const uint ClothOfLegend = 50830;

    private const uint Honeysuckler = 43572;
    private const uint TheLawnblazer = 44494;
    private const uint DoingTheWave = 44506;
    private const uint SlimFrameGlasses = 45006;
    private const uint FigmentalWeaponCoffer = 45070;
    private const uint YanHorn = 46829;
    private const uint TacticalGoggles = 46842;
    private const uint ProfessorialGlasses = 46845;
    private const uint Torch = 46848;
    private const uint MiniYan = 47964;
    private const uint GimmeKitten = 47965;
    private const uint CoeurlEyeglasses = 48152;
    private const uint TacoDelightRoll = 50795;
    private const uint BladeUnbrokenRoll = 50797;
    private const uint ForHopeAndHappinessRoll = 52357;
    private const uint EverkeepMixingBooth = 49846;
    private const uint UnfriendlyYanNeon = 49847;
    private const uint EverkeepMonitor = 51262;
    private const uint EverkeepStereo = 51263;
    private const uint Sweatcloth = 52630;
    private const uint IndigoTwillCloth = 52631;
    private const uint TwilightGemstone = 41805;

    // Both map pages list the same rare loot.
    private static readonly uint[] dawntrailMapLoot =
    [
        ItTakesTwo, RibbonedParasol, CatEyeGlasses, CorduroyFelt, DumbbellRack, TrainingBench,
        EtchedInMemoryRoll, SummerlightLinen, Dam, FunAndGamesRoll, ClothOfLegend,
    ];

    private static readonly uint[] cenoteLoot =
    [
        Honeysuckler, TheLawnblazer, DoingTheWave, CatEyeGlasses, SlimFrameGlasses, EtchedInMemoryRoll, TacticalGoggles, ProfessorialGlasses, Torch,
        CoeurlEyeglasses, TacoDelightRoll, BladeUnbrokenRoll, ForHopeAndHappinessRoll, FigmentalWeaponCoffer, EverkeepMixingBooth, UnfriendlyYanNeon, EverkeepMonitor,
        EverkeepStereo, SummerlightLinen, ClothOfLegend, Sweatcloth, IndigoTwillCloth, TwilightGemstone,
    ];

    // The vault drops all the cenote does, and the Yan Horn and two minions on top.
    private static readonly uint[] vaultLoot =
    [
        Honeysuckler, TheLawnblazer, DoingTheWave, CatEyeGlasses, SlimFrameGlasses, EtchedInMemoryRoll, YanHorn, TacticalGoggles, ProfessorialGlasses,
        Torch, MiniYan, GimmeKitten, CoeurlEyeglasses, TacoDelightRoll, BladeUnbrokenRoll, ForHopeAndHappinessRoll, FigmentalWeaponCoffer, EverkeepMixingBooth,
        UnfriendlyYanNeon, EverkeepMonitor, EverkeepStereo, SummerlightLinen, ClothOfLegend, Sweatcloth, IndigoTwillCloth, TwilightGemstone,
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
