namespace AutoTreasureMaps.Core.Localization;

internal static partial class L
{
    internal static class Cleanup
    {
        public static readonly LocString Cat = new("cleanup.cat", "Loot cleanup");
        public static readonly LocString CatSub = new("cleanup.catSub", "Discard the loot you do not keep, so the bags never fill up during a run.");
        public static readonly LocString Group = new("cleanup.group", "Auto-discard");
        public static readonly LocString Enable = new("cleanup.enable", "Auto-discard loot");
        public static readonly LocString EnableHelp = new("cleanup.enableHelp", "Every item a coffer or a treasure dungeon gives is noted. Between maps, before walking into a portal, and once a run finishes, the noted items that are not on the whitelist are discarded from your bags. Maps and each map's high-value loot are never discarded.");
        public static readonly LocString Safety = new("cleanup.safety", "Only treasure-hunt loot is ever discarded. Items you got any other way are never touched, and neither is any item you already had before the coffer opened, even if a coffer gives more of it.");
        public static readonly LocString Off = new("cleanup.off", "Auto-discard is off. Nothing is noted or discarded.");
        public static readonly LocString MaxQuantity = new("cleanup.maxQuantity", "Keep large stacks");
        public static readonly LocString MaxQuantityHelp = new("cleanup.maxQuantityHelp", "An item looted in a larger quantity than this is kept instead of discarded, such as a big stack of whetstones from a treasure dungeon. 0 sets no limit.");
        public static readonly LocString MaxQuantityFormat = new("cleanup.maxQuantityFormat", "over %d");
        public static readonly LocString Whitelist = new("cleanup.whitelist", "Whitelist");
        public static readonly LocString AddItem = new("cleanup.addItem", "Add an item");
        public static readonly LocString AddItemHelp = new("cleanup.addItemHelp", "Search any item in the game by name or item ID. You can also right-click an item in your inventory and choose \"Add to cleanup whitelist\".");
        public static readonly LocString SearchHint = new("cleanup.searchHint", "Item name or ID");
        public static readonly LocString NoMatch = new("cleanup.noMatch", "No item matches.");
        public static readonly LocPlural MoreMatches = new("cleanup.moreMatches", "{0} more match; type more to narrow it down.", "{0} more matches; type more to narrow them down.");
        public static readonly LocString ResultLabel = new("cleanup.resultLabel", "{0}  ({1})");
        public static readonly LocString KeptItems = new("cleanup.keptItems", "Kept items");
        public static readonly LocString KeptItemsHelp = new("cleanup.keptItemsHelp", "Never discarded.");
        public static readonly LocString NoKept = new("cleanup.noKept", "The whitelist is empty, so all noted loot is discarded.");
        public static readonly LocString AlwaysKept = new("cleanup.alwaysKept", "Always kept");
        public static readonly LocString AlwaysKeptHelp = new("cleanup.alwaysKeptHelp", "Timeworn maps and each map's high-value loot, built into the plugin.");
        public static readonly LocString MenuAdd = new("cleanup.menuAdd", "Add to cleanup whitelist");
        public static readonly LocString MenuRemove = new("cleanup.menuRemove", "Remove from cleanup whitelist");
        public static readonly LocString ChatKept = new("cleanup.chatKept", "{0} is on the cleanup whitelist and will not be discarded.");
        public static readonly LocString ChatUnkept = new("cleanup.chatUnkept", "{0} was taken off the cleanup whitelist.");
        public static readonly LocPlural ChatDone = new("cleanup.chatDone", "Discarded {0} slot of loot.", "Discarded {0} slots of loot.");
        public static readonly LocString ChatGaveUp = new("cleanup.chatGaveUp", "Could not discard {0}; it was taken off the list.");
        public static readonly LocString ChatCapped = new("cleanup.chatCapped", "Stopped after {0} discards; the rest waits for the next cleanup.");
        public static readonly LocString PendingTitle = new("cleanup.pendingTitle", "Loot to discard");
        public static readonly LocPlural PendingKinds = new("cleanup.pendingKinds", "{0} kind", "{0} kinds");
        public static readonly LocString DiscardNow = new("cleanup.discardNow", "Discard now");
        public static readonly LocString Discarding = new("cleanup.discarding", "Discarding…");
        public static readonly LocString Keep = new("cleanup.keep", "Keep: add to the whitelist");
        public static readonly LocString ItemLine = new("cleanup.itemLine", "{0} ×{1}");
    }
}
