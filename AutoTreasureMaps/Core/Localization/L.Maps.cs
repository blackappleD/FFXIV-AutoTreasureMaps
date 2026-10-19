namespace AutoTreasureMaps.Core.Localization;

internal static partial class L
{
    internal static class Maps
    {
        public static readonly LocString Cat = new("maps.cat", "Maps");
        public static readonly LocString CatSub = new("maps.catSub", "Which timeworn maps a run deciphers, and in which order.");
        public static readonly LocString Order = new("maps.order", "Order");
        public static readonly LocString OrderLabel = new("maps.orderLabel", "Decipher order");
        public static readonly LocString OrderHelp = new("maps.orderHelp", "Which enabled map is deciphered next when your bags hold more than one tier.");
        public static readonly LocString OrderHighestName = new("maps.order.highest.name", "Highest tier first");
        public static readonly LocString OrderHighestDetail = new("maps.order.highest.detail", "Start with the newest expansion's maps and work down.");
        public static readonly LocString OrderLowestName = new("maps.order.lowest.name", "Lowest tier first");
        public static readonly LocString OrderLowestDetail = new("maps.order.lowest.detail", "Start with the oldest maps and clear them out of your bags first.");
        public static readonly LocString Tiers = new("maps.tiers", "Map tiers");
        public static readonly LocString TiersNote = new("maps.tiersNote", "Switch off any tier the run should leave in your bags. /atm decipher with a map's name still deciphers any tier by hand.");
        public static readonly LocString FullPartyNote = new("maps.fullPartyNote", "Maps tagged Full party are tuned for eight players. They can be done alone, but the guards may be too much for your job and gear.");
        public static readonly LocString UnderseaNote = new("maps.underseaNote", "Undersea maps are dug on the sea floor. They stay off until the plugin can dive.");
        public static readonly LocString FullPartyBadge = new("maps.fullPartyBadge", "Full party");
        public static readonly LocString UnderseaBadge = new("maps.underseaBadge", "Undersea");
        public static readonly LocString PatchBadge = new("maps.patchBadge", "Patch {0}");
        public static readonly LocString SoloHelp = new("maps.soloHelp", "A solo map: its guards are tuned for one player.");
        public static readonly LocString FullPartyHelp = new("maps.fullPartyHelp", "Tuned for a full party of eight. Alone, the guards may be too much for your job and gear.");
        public static readonly LocString UnderseaHelp = new("maps.underseaHelp", "Every dig spot of this map lies under water, so it stays off until the plugin can dive.");

        public static readonly LocString BagTitle = new("maps.bagTitle", "Maps in your bags");
        public static readonly LocString StateEnabled = new("maps.state.enabled", "Enabled");
        public static readonly LocString StateDisabled = new("maps.state.disabled", "Disabled");
        public static readonly LocString StateLocked = new("maps.state.locked", "Locked");
        public static readonly LocString Next = new("maps.next", "Next");
        public static readonly LocString DecipherNext = new("maps.decipherNext", "Decipher next");
        public static readonly LocString DecipherNextHint = new("maps.decipherNextHint", "Decipher {0} now.");
    }
}
