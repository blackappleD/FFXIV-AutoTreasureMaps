namespace AutoTreasureMaps.Core.Dungeon;

// Rows of the game's LogMessage sheet that the treasure dungeons send. Row ids are the same in every client language.
// The gate rows are shared by every gate dungeon; which one a dungeon sends is checked in game with /atm dungeon record.
internal static class DungeonLogMessages
{
    // The Gambler's Lure: the first parameter of LureRound is the round, 1 to 5.
    // A guess that ties the two cards: the lure is "not fully disarmed" and the cards are dealt again for the same challenge.
    public const uint LureTied = 6965;
    public const uint LureRound = 6966;
    public const uint LureSprung = 6967;
    public const uint LureDeclined = 6968;
    public const uint LureDisarmed = 6969;
    public const uint LureStopped = 6970;
    public const uint LureAllDisarmed = 6971;
    public const uint LureFailed = 6972;
    // The lure answers that the character is already tempting another.
    public const uint LureTemptedAnother = 6973;

    // The first parameter names the gate: 1 left, 2 right, 3 centre.
    public const uint GateGlowsOfThree = 6975;
    public const uint GateGlows = 6976;
    public const uint GateLocked = 6978;
    public const uint GateShut = 6979;
    public const uint GateHandPlaced = 6997;
    // The first parameter is the chamber the gate leads to, counted from 1 for the second chamber.
    public const uint GateOpenedOfSeven = 6998;
    public const uint GateTrapExpelled = 6999;
    public const uint DrawnInExpelled = 9354;
    public const uint WipedExpelled = 9355;
    public const uint GateOpenedOfFive = 9365;

    public const uint VaultExpelled = 11260;
    public const uint VaultWipedExpelled = 11261;
    public const uint BonusRoundStarted = 11262;
    public const uint BonusRoundEnded = 11265;
    public const uint SlotGlows = 11266;
    public const uint SlotOperated = 11267;
    public const uint SlotSpinning = 11268;
    // The first parameter is the notion drawn: 1 lesser, 2 greater, 3 grand, 4 final.
    public const uint SlotEnvisions = 11269;
    public const uint AllEnemiesDefeated = 11270;
    public const uint BonusRoundCoffer = 11274;

    public static ReadOnlySpan<uint> GateOpened => [GateOpenedOfSeven, GateOpenedOfFive];

    public static ReadOnlySpan<uint> GateGlowHints => [GateGlowsOfThree, GateGlows];

    public static ReadOnlySpan<uint> Expelled => [GateTrapExpelled, DrawnInExpelled, WipedExpelled, VaultExpelled, VaultWipedExpelled];

    public static string? NameOf(uint logMessageId) => logMessageId switch
    {
        LureTied => nameof(LureTied),
        LureRound => nameof(LureRound),
        LureSprung => nameof(LureSprung),
        LureDeclined => nameof(LureDeclined),
        LureDisarmed => nameof(LureDisarmed),
        LureStopped => nameof(LureStopped),
        LureAllDisarmed => nameof(LureAllDisarmed),
        LureFailed => nameof(LureFailed),
        LureTemptedAnother => nameof(LureTemptedAnother),
        GateGlowsOfThree => nameof(GateGlowsOfThree),
        GateGlows => nameof(GateGlows),
        GateLocked => nameof(GateLocked),
        GateShut => nameof(GateShut),
        GateHandPlaced => nameof(GateHandPlaced),
        GateOpenedOfSeven => nameof(GateOpenedOfSeven),
        GateTrapExpelled => nameof(GateTrapExpelled),
        DrawnInExpelled => nameof(DrawnInExpelled),
        WipedExpelled => nameof(WipedExpelled),
        GateOpenedOfFive => nameof(GateOpenedOfFive),
        VaultExpelled => nameof(VaultExpelled),
        VaultWipedExpelled => nameof(VaultWipedExpelled),
        BonusRoundStarted => nameof(BonusRoundStarted),
        BonusRoundEnded => nameof(BonusRoundEnded),
        SlotGlows => nameof(SlotGlows),
        SlotOperated => nameof(SlotOperated),
        SlotSpinning => nameof(SlotSpinning),
        SlotEnvisions => nameof(SlotEnvisions),
        AllEnemiesDefeated => nameof(AllEnemiesDefeated),
        BonusRoundCoffer => nameof(BonusRoundCoffer),
        _ => null,
    };
}
