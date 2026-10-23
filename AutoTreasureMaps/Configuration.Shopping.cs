using Newtonsoft.Json;

namespace AutoTreasureMaps;

public sealed partial class Configuration
{
    // Between maps, each tomestone that has reached its own threshold has its list bought through GatherBuddy Reborn.
    public bool TomestoneShopping { get; set; } = false;

    // One list per tomestone, by its item id.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<TomestoneShoppingList> TomestoneShoppingLists { get; set; } = [];
}

[Serializable]
public sealed class TomestoneShoppingList
{
    public uint CurrencyItemId { get; set; }

    // 0 never buys this list.
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
