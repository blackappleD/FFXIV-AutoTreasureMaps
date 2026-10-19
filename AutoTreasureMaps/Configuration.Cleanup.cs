namespace AutoTreasureMaps;

public sealed partial class Configuration
{
    public bool AutoDiscard { get; set; } = false;

    // An item looted in a larger quantity than this is kept rather than discarded; 0 sets no limit.
    public int AutoDiscardMaxQuantity { get; set; } = 99;

    // Item ids the loot cleanup never discards, on top of the maps and each map's high-value loot it always keeps.
    public List<uint> AutoDiscardKeep { get; set; } = [];
}
