using Newtonsoft.Json;

namespace AutoTreasureMaps;

public sealed partial class Configuration
{
    // Between maps, once any tomestone reaches its threshold, every tomestone's list is bought through GatherBuddy Reborn.
    public bool TomestoneShopping { get; set; } = false;

    // One list per tomestone, by its item id.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<TomestoneShoppingList> TomestoneShoppingLists { get; set; } = [];
}

[Serializable]
public sealed class TomestoneShoppingList
{
    public uint CurrencyItemId { get; set; }

    // 0 never starts a shopping trip, though the list is still bought when another tomestone starts one.
    public int Threshold { get; set; }

    public List<TomestoneShoppingItem> Items { get; set; } = [];
}

[Serializable]
public sealed class TomestoneShoppingItem
{
    public uint ItemId { get; set; }

    // Bought on each trip, on top of what the bags, armoury and gear already hold.
    public int Quantity { get; set; } = 1;
}
