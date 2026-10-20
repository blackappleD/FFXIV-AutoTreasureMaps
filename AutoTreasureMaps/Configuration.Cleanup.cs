namespace AutoTreasureMaps;

public sealed partial class Configuration
{
    public bool AutoDiscard { get; set; } = false;

    // An item looted in a larger quantity than this is kept rather than discarded; 0 sets no limit.
    public int AutoDiscardMaxQuantity { get; set; } = 99;

    // Items of notable rarity, unique items, equipment and items that unlock something are kept as well.
    public bool AutoDiscardKeepNotable { get; set; } = true;

    // Item ids the loot cleanup never discards, on top of the maps and each map's rare loot it always keeps.
    public List<uint> AutoDiscardKeep { get; set; } = [];

    // Rare loot item ids the player picked to discard; every other rare item is kept.
    public List<uint> AutoDiscardRare { get; set; } = [];
}
