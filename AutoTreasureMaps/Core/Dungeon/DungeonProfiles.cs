using AutoTreasureMaps.Core.Stats;

namespace AutoTreasureMaps.Core.Dungeon;

// Ids from the game's TerritoryType, ContentFinderCondition and EObjName sheets. Every gate dungeon has its own run of
// eight or twelve "treasure vault gate" event objects (two per gate wall) and a few "treasure coffer" objects next to
// them, and Dawntrail's are the run that starts at 2013860. Neither profile has been checked in game yet.
internal static class DungeonProfiles
{
    // G17, Timeworn Br'aaxskin Map (and the Presumably Special Timeworn Map): five chambers, two gates to each.
    public static readonly DungeonProfile CenoteJaJaGural = new(
        Key: "cenote-ja-ja-gural",
        MapItemId: HighValueLoot.BraaxskinMap,
        TerritoryId: 1209,
        ContentFinderConditionId: 993,
        Kind: DungeonKind.Gate,
        Rounds: 5,
        CofferBaseIds: [2013860, 2013861, 2013862, 2013863],
        AdvanceBaseIds: [2013864, 2013865, 2013866, 2013867, 2013868, 2013869, 2013870, 2013871],
        TransitBaseIds: [],
        ExitBaseIds: [],
        Verified: false);

    // G18, Timeworn Gargantuaskin Map: one room, five draws of the hypnoslot machine, each paying out a gold, silver or
    // bronze coffer once its enemies fall.
    public static readonly DungeonProfile VaultOneiron = new(
        Key: "vault-oneiron",
        MapItemId: HighValueLoot.GargantuaskinMap,
        TerritoryId: 1279,
        ContentFinderConditionId: 1060,
        Kind: DungeonKind.Slot,
        Rounds: 5,
        CofferBaseIds: [2014741, 2014742, 2014743],
        AdvanceBaseIds: [2014790],
        TransitBaseIds: [],
        ExitBaseIds: [],
        Verified: false);
}
