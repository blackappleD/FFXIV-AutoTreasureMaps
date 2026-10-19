namespace AutoTreasureMaps;

public sealed partial class Configuration
{
    public bool AutoDiscard { get; set; } = false;

    // Item ids the loot cleanup never discards, on top of the maps and each map's high-value loot it always keeps.
    public List<uint> AutoDiscardKeep { get; set; } = [];
}
