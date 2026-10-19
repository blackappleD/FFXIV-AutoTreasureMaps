using AutoTreasureMaps.Core.Stats;

namespace AutoTreasureMaps.Core.Dungeon;

// Ids from the game's TerritoryType, ContentFinderCondition and EObjName sheets. Every gate dungeon has its own run of
// eight or twelve "treasure vault gate" event objects (two per gate wall) and a few "treasure coffer" objects next to
// them, and Dawntrail's are the run that starts at 2013860. Both profiles have been checked in game.
internal static class DungeonProfiles
{
    // G17, Timeworn Br'aaxskin Map (and the Presumably Special Timeworn Map): five chambers, two gates to each of the
    // first four. A chamber's coffer calls its guards when first touched and opens once they fall; the gate taken opens
    // onto a circle that flies the character to the next chamber, and the fifth holds a boss and the exit point.
    public static readonly DungeonProfile CenoteJaJaGural = new(
        Key: "cenote-ja-ja-gural",
        MapItemId: RareLoot.BraaxskinMap,
        TerritoryId: 1209,
        ContentFinderConditionId: 993,
        Kind: DungeonKind.Gate,
        Rounds: 5,
        CofferBaseIds: [2013860, 2013861, 2013862, 2013863],
        AdvanceBaseIds: [2013864, 2013865, 2013866, 2013867, 2013868, 2013869, 2013870, 2013871],
        TransitBaseIds: [],
        // From the layout (y6c1 planmap): the event range behind each gate, where touching the circle flies the
        // character on.
        GateCircles:
        [
            new(2013864, new(-36.9f, -400.1f, 340.1f)),
            new(2013865, new(36.9f, -400.1f, 340.1f)),
            new(2013866, new(-36.9f, -400.3f, 155.2f)),
            new(2013867, new(36.8f, -400.3f, 155.1f)),
            new(2013868, new(123.2f, -290.3f, -17.9f)),
            new(2013869, new(196.9f, -290.3f, -17.9f)),
            new(2013870, new(-233.8f, -169.3f, -181.9f)),
            new(2013871, new(-160.0f, -169.3f, -181.9f)),
        ],
        // The final chamber's exit point.
        ExitBaseIds: [2000139],
        // Alpaca of fortune, uolon of fortune; Turali onion, eggplant, garlic, tomato, Tuligora queen.
        BonusNameIds: [13240, 13241],
        MandragoraNameIds: [13242, 13243, 13244, 13245, 13246],
        GoldSackBaseIds: [],
        SilverSackBaseIds: [],
        Verified: true);

    // G18, Timeworn Gargantuaskin Map: one room, five draws of the hypnoslot machine, each paying out a gold, silver or
    // bronze coffer once its enemies fall.
    public static readonly DungeonProfile VaultOneiron = new(
        Key: "vault-oneiron",
        MapItemId: RareLoot.GargantuaskinMap,
        TerritoryId: 1279,
        ContentFinderConditionId: 1060,
        Kind: DungeonKind.Slot,
        Rounds: 5,
        CofferBaseIds: [2014741, 2014742, 2014743],
        AdvanceBaseIds: [2014790],
        TransitBaseIds: [],
        GateCircles: [],
        ExitBaseIds: [],
        // Goldy cat, vaultkeeper; vault onion, eggplant, garlic, tomato, queen.
        BonusNameIds: [14014, 14015],
        MandragoraNameIds: [14016, 14017, 14018, 14019, 14020],
        // The bonus round's gold and silver sacks; it asks for a hundred glowing sacks in ninety seconds.
        GoldSackBaseIds: [2014791],
        SilverSackBaseIds: [2014792],
        Verified: true);
}
