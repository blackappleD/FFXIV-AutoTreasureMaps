namespace AutoTreasureMaps.Core.Localization;

internal static partial class L
{
    internal static class Cleanup
    {
        public static readonly LocString Cat = new("cleanup.cat", "Loot cleanup");
        public static readonly LocString CatSub = new("cleanup.catSub", "Discard the loot you do not keep, so the bags never fill up during a run.");
        public static readonly LocString Group = new("cleanup.group", "Auto-discard");
        public static readonly LocString Enable = new("cleanup.enable", "Auto-discard loot");
        public static readonly LocString EnableHelp = new("cleanup.enableHelp", "Every item a coffer or a treasure dungeon gives is noted. Between maps, before walking into a portal, and once a run finishes, the noted items that are not kept are discarded from your bags. Maps and anything on the whitelist are never discarded, rare loot is kept unless you pick it below, and notable items are kept unless you turn that off. The noted list belongs to the run and the character: it is forgotten when the run stops or ends and when you log out.");
        public static readonly LocString Safety = new("cleanup.safety", "Only an item the bags held none of before the coffer opened can be discarded, unless it does not stack (each copy then has a slot of its own), and never more copies than the coffer or dungeon gave. A map or dungeon you paused the run in after its coffer opened is left alone, since what you did meanwhile would look like its loot.");
        public static readonly LocString Off = new("cleanup.off", "Auto-discard is off. Nothing is noted or discarded.");
        public static readonly LocString MaxQuantity = new("cleanup.maxQuantity", "Keep large stacks");
        public static readonly LocString HighQualityName = new("cleanup.highQualityName", "{0} (HQ)");
        public static readonly LocString KeepNotable = new("cleanup.keepNotable", "Keep notable items");
        public static readonly LocString KeepNotableHelp = new("cleanup.keepNotableHelp", "Never discard an item of green rarity or above, a unique item, equipment, a furnishing, or an item with a use other than a meal or a medicine (minions, mounts, orchestrion rolls, cards, hairstyles and the like). This keeps the rare loot of the maps the list below does not cover. On by default.");
        public static readonly LocString MaxQuantityHelp = new("cleanup.maxQuantityHelp", "An item looted in a larger quantity than this is kept instead of discarded, such as a big stack of whetstones from a treasure dungeon. 0 sets no limit.");
        public static readonly LocString MaxQuantityFormat = new("cleanup.maxQuantityFormat", "over %d");
        public static readonly LocString Whitelist = new("cleanup.whitelist", "Whitelist");
        public static readonly LocString AddItem = new("cleanup.addItem", "Add an item");
        public static readonly LocString AddItemHelp = new("cleanup.addItemHelp", "Search any item in the game by name or item ID. You can also right-click an item in your inventory or an item link in chat and choose \"Add to cleanup whitelist\".");
        public static readonly LocString SearchHint = new("cleanup.searchHint", "Item name or ID");
        public static readonly LocString NoMatch = new("cleanup.noMatch", "No item matches.");
        public static readonly LocPlural MoreMatches = new("cleanup.moreMatches", "{0} more match; type more to narrow it down.", "{0} more matches; type more to narrow them down.");
        public static readonly LocString ResultLabel = new("cleanup.resultLabel", "{0}  ({1})");
        public static readonly LocString KeptItems = new("cleanup.keptItems", "Kept items");
        public static readonly LocString KeptItemsHelp = new("cleanup.keptItemsHelp", "Never discarded.");
        public static readonly LocString NoKept = new("cleanup.noKept", "The whitelist is empty, so all noted loot is discarded.");
        public static readonly LocString Rare = new("cleanup.rare", "Rare loot");
        public static readonly LocString RareHelp = new("cleanup.rareHelp", "Click an item to switch it: lit up is kept, dimmed is discarded. An item listed under more than one map or dungeon switches under all of them.");
        public static readonly LocString RareWarning = new("cleanup.rareWarning", "Rare loot is kept by default. The lists below cover only the maps and dungeons shown; on any other map, Keep notable items is what keeps its minions, orchestrion rolls and furnishings, so leave it on unless you whitelist them. Some rare items do not stack, so each one takes a bag slot of its own, and keeping them all can fill up your bags.");
        public static readonly LocString RareKept = new("cleanup.rareKept", "{0}/{1} kept");
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
