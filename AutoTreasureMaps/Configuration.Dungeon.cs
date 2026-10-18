namespace AutoTreasureMaps;

// Which gate a gate dungeon tries. Default stands for "as the default says" in a dungeon's own override.
public enum GatePolicy : byte
{
    Random,
    Left,
    Right,
    Default,
}

// The Gambler's Lure: the coffer's high-or-low game. Opening the coffer takes the treasure as it is and skips the game.
public enum LurePolicy : byte
{
    AskMe,
    OpenCoffer,
}

public sealed partial class Configuration
{
    public GatePolicy DungeonGatePolicy { get; set; } = GatePolicy.Random;

    // Keyed by the dungeon profile's key; a dungeon not in it, or set to Default, follows DungeonGatePolicy.
    public Dictionary<string, GatePolicy> DungeonGatePolicyOverrides { get; set; } = [];

    // A glowing gate is the game's own hint, so it wins over the policy while this is on.
    public bool DungeonFollowGateHint { get; set; } = true;

    public LurePolicy DungeonLurePolicy { get; set; } = LurePolicy.AskMe;

    public GatePolicy GatePolicyFor(string dungeonKey)
        => DungeonGatePolicyOverrides.TryGetValue(dungeonKey, out var policy) && policy != GatePolicy.Default ? policy : DungeonGatePolicy;
}
