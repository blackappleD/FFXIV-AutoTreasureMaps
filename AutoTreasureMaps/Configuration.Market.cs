using Newtonsoft.Json;

namespace AutoTreasureMaps;

public sealed partial class Configuration
{
    public bool MarketBuyingEnabled { get; set; } = false;

    // Gil per map before the market tax, by map rank. A tier missing here is never bought.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<byte, int> MarketPriceCaps { get; set; } = [];

    // Market tax included.
    public int MarketRunBudget { get; set; } = 100_000;

    public int MarketKeepGil { get; set; } = 100_000;

    public MarketBoardChoice MarketBoard { get; set; } = MarketBoardChoice.Automatic;

    // Prices the rare loot from Universalis for the stat cards and tooltips.
    public bool LootPriceEstimates { get; set; } = false;

    // Buys on the world of the data centre Universalis lists cheapest, through Lifestream, and digs there.
    public bool MarketCrossWorld { get; set; } = false;

    // Gil a trip to another world must save on the map, against the cheapest listing on the current one.
    public int MarketCrossWorldMinSavings { get; set; } = 5_000;

    // After a purchase, decipher the map at the board and buy a second one into the bags, so the next map needs no trip.
    public bool MarketBuyAhead { get; set; } = false;

    // A run that starts with a deciphered map but no spare buys the spare before its first dig.
    public bool MarketSpareFirst { get; set; } = false;
}
