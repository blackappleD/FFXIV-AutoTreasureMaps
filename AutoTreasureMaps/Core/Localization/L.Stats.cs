namespace AutoTreasureMaps.Core.Localization;

internal static partial class L
{
    internal static class Stats
    {
        public static readonly LocString Title = new("stats.title", "By map");
        public static readonly LocString Prices = new("stats.prices", "Lowest listings on {0}, from Universalis");
        public static readonly LocString PricesLoading = new("stats.pricesLoading", "Fetching prices from Universalis…");
        public static readonly LocPlural Maps = new("stats.maps", "{0} map", "{0} maps");
        public static readonly LocPlural Dungeons = new("stats.dungeons", "{0} dungeon", "{0} dungeons");
        public static readonly LocString Floor = new("stats.floor", "Floor {0} ×{1}");
        public static readonly LocString Worth = new("stats.worth", "Worth {0}");
        public static readonly LocString LootValue = new("stats.lootValue", "Rare loot worth");
    }
}
