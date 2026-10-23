namespace AutoTreasureMaps.Core.Localization;

internal static partial class L
{
    internal static class Shopping
    {
        public static readonly LocString Cat = new("shopping.cat", "Shopping");
        public static readonly LocString CatSub = new("shopping.catSub", "Spend tomestones between maps through GatherBuddy Reborn's vendor lists.");
        public static readonly LocString Group = new("shopping.group", "Tomestone shopping");
        public static readonly LocString Enable = new("shopping.enable", "Buy with tomestones");
        public static readonly LocString EnableHelp = new("shopping.enableHelp", "Each tomestone stands alone. Between maps, every tomestone that has reached its own threshold has its items written to a GatherBuddy Reborn vendor list of its own and bought, and then the run goes back to the maps. Each item is bought in the quantity set below, on top of what you already hold, as far as the tomestones last.");
        public static readonly LocString Off = new("shopping.off", "Tomestone shopping is off. Nothing is bought.");
        public static readonly LocString Independent = new("shopping.independent", "The three tomestones are independent: each one's list is bought only when that tomestone reaches its own threshold.");
        public static readonly LocString NeedsGatherBuddy = new("shopping.needsGatherBuddy", "Needs GatherBuddy Reborn, loaded and up to date. Without it nothing is bought.");
        public static readonly LocString GatherBuddyReady = new("shopping.gatherBuddyReady", "GatherBuddy Reborn is ready. Each tomestone gets its own list there: {0}.");
        public static readonly LocString Threshold = new("shopping.threshold", "Threshold");
        public static readonly LocString ThresholdHelp = new("shopping.thresholdHelp", "Buy this tomestone's list once you hold at least this many. Off: this list is never bought.");
        public static readonly LocString ThresholdFormat = new("shopping.thresholdFormat", "at %d");
        public static readonly LocString ThresholdOff = new("shopping.thresholdOff", "off");
        public static readonly LocString Held = new("shopping.held", "You hold {0}.");
        public static readonly LocString AddItem = new("shopping.addItem", "Add an item");
        public static readonly LocString AddItemHelp = new("shopping.addItemHelp", "The items a vendor sells for this tomestone alone, with no other item or currency asked. Pick one and press Add; add as many as you like.");
        public static readonly LocString SearchHint = new("shopping.searchHint", "Search items");
        public static readonly LocString Loading = new("shopping.loading", "Reading the vendors…");
        public static readonly LocString NoOffers = new("shopping.noOffers", "No vendor sells anything for this tomestone.");
        public static readonly LocString OfferLabel = new("shopping.offerLabel", "{0}  ·  {1}");
        public static readonly LocString Items = new("shopping.items", "Items to buy");
        public static readonly LocString ItemsHelp = new("shopping.itemsHelp", "Bought on every trip: this many more than your bags, armoury and gear hold at the time.");
        public static readonly LocString NoItems = new("shopping.noItems", "No items yet, so this tomestone's list is skipped.");
        public static readonly LocString QuantityFormat = new("shopping.quantityFormat", "×%d");
        public static readonly LocString ChatStart = new("shopping.chatStart", "You hold {1} {0} (threshold {2}); buying its list through GatherBuddy Reborn.");
        public static readonly LocString ChatListFailed = new("shopping.chatListFailed", "GatherBuddy Reborn could not buy the list \"{0}\". The log has the details.");
        public static readonly LocString ChatSkipped = new("shopping.chatSkipped", "GatherBuddy Reborn found no vendor for {1} item(s) of \"{0}\", so they were left out.");
        public static readonly LocString ChatDone = new("shopping.chatDone", "\"{0}\" is done; {1} {2} spent.");
    }
}
