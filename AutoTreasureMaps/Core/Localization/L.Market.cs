namespace AutoTreasureMaps.Core.Localization;

internal static partial class L
{
    internal static class Market
    {
        public static readonly LocString Group = new("market.group", "Market board");
        public static readonly LocString Enabled = new("market.enabled", "Buy maps on the market board");
        public static readonly LocString EnabledHelp = new("market.enabledHelp", "When your bags hold no enabled map and no daily map can be gathered, the run travels to a market board, buys the cheapest map under its tier's price cap, then deciphers and runs it.");
        public static readonly LocString Off = new("market.off", "Off. Runs use only the maps already in your bags.");
        public static readonly LocString Budget = new("market.budget", "Budget per run");
        public static readonly LocString BudgetHelp = new("market.budgetHelp", "The most gil one run spends on maps, market tax included. Once it is spent, the run stops buying.");
        public static readonly LocString KeepGil = new("market.keepGil", "Gil to keep");
        public static readonly LocString KeepGilHelp = new("market.keepGilHelp", "A map is never bought if it would leave you with less gil than this.");
        public static readonly LocString Board = new("market.board", "Board");
        public static readonly LocString BoardHelp = new("market.boardHelp", "The market board the run travels to. A board a few steps away is used wherever you stand.");
        public static readonly LocString BoardAutoName = new("market.board.auto.name", "Automatic");
        public static readonly LocString BoardAutoDetail = new("market.board.auto.detail", "Limsa Lominsa Lower Decks, or Old Sharlayan while you are there: both boards are a short walk from the city aetheryte.");
        public static readonly LocString BoardAetheryteDetail = new("market.board.aetheryte.detail", "The board is a short walk from the city aetheryte.");
        public static readonly LocString BoardAethernetDetail = new("market.board.aethernet.detail", "The board is reached with an aethernet ride inside the city.");
        public static readonly LocString BoardDistrictDetail = new("market.board.district.detail", "The board stands in another district of the city, reached through the aethernet.");
        public static readonly LocString CrossWorld = new("market.crossWorld", "Buy on the cheapest world");
        public static readonly LocString CrossWorldHelp = new("market.crossWorldHelp", "Before each purchase, compare the map's price across your data centre on Universalis and travel with Lifestream to the world that sells it cheapest. The board there is read again, and the map is dug on that world. Without Lifestream, maps are bought where you stand.");
        public static readonly LocString CrossWorldSavings = new("market.crossWorldSavings", "Least saving for a trip");
        public static readonly LocString CrossWorldSavingsHelp = new("market.crossWorldSavingsHelp", "Travel only when the other world sells the map at least this much cheaper than your current world.");
        public static readonly LocString CrossWorldNoLifestream = new("market.crossWorldNoLifestream", "Lifestream is not loaded, so maps are bought on the world you stand on.");
        public static readonly LocString BuyAhead = new("market.buyAhead", "Buy a map ahead");
        public static readonly LocString BuyAheadHelp = new("market.buyAheadHelp", "After buying a map, decipher it at the board and buy a second one into your bags, so the next map needs no trip to the board. The second map counts against the budget like any other.");
        public static readonly LocString Caps = new("market.caps", "Price caps");
        public static readonly LocString CapsNote = new("market.capsNote", "The most a run pays for one map of each tier, before the market tax. A tier without a cap is never bought, and neither is a tier switched off below. Only tradable tiers are listed.");
        public static readonly LocString CapHelp = new("market.capHelp", "The highest price per map accepted for this tier, before the market tax. 0 leaves the tier unbought.");
        public static readonly LocString NotSet = new("market.notSet", "Not set");
        public static readonly LocString GilFormat = new("market.gilFormat", "%d gil");

        public static readonly LocString ReasonOff = new("market.reason.off", "Market buying is off. Settings, Maps turns it on.");
        public static readonly LocString ReasonUnsupported = new("market.reason.unsupported", "The market board's functions were not found in this game version.");
        public static readonly LocString ReasonFreeTrial = new("market.reason.freeTrial", "Free trial accounts cannot use the market board.");
        public static readonly LocString ReasonInDuty = new("market.reason.inDuty", "The market board cannot be used while you are bound by a duty.");
        public static readonly LocString ReasonStopped = new("market.reason.stopped", "Buying stopped for this run: the game said there was not enough gil.");
        public static readonly LocString ReasonNoCaps = new("market.reason.noCaps", "No enabled, tradable map tier has a price cap. Settings, Maps sets them.");
        public static readonly LocString ReasonAllHeld = new("market.reason.allHeld", "Your bags already hold a map of every tier with a price cap, and only one of each fits.");
        public static readonly LocString ReasonBudgetSpent = new("market.reason.budgetSpent", "The run's market budget is spent.");
        public static readonly LocString ReasonNotEnoughGil = new("market.reason.notEnoughGil", "Not enough gil: a map at its price cap would leave less than the gil to keep.");
        public static readonly LocString ReasonBagFull = new("market.reason.bagFull", "Your bags are full.");
        public static readonly LocString ReasonBoardLocked = new("market.reason.boardLocked", "The chosen market board is out of reach: its city aetheryte is not attuned, or the board is not unlocked yet.");

        public static readonly LocString FailBoard = new("market.fail.board", "The market board could not be reached or opened.");
        public static readonly LocString FailThrottled = new("market.fail.throttled", "The market board kept asking to wait. Try again in a little while.");
        public static readonly LocString FailInterfered = new("market.fail.interfered", "Another search took over the market board.");
        public static readonly LocString FailListingsChanged = new("market.fail.listingsChanged", "The listings kept changing before a purchase went through.");
        public static readonly LocString FailNotLanded = new("market.fail.notLanded", "The game accepted the purchase, but the map never reached your bags.");
        public static readonly LocString FailNoAnswer = new("market.fail.noAnswer", "The market board stopped answering.");
        public static readonly LocString FailNotEnoughGil = new("market.fail.notEnoughGil", "The game says you do not have enough gil for the map.");
        public static readonly LocString FailTimedOut = new("market.fail.timedOut", "It took too long and was stopped.");
        public static readonly LocString FailUnexpected = new("market.fail.unexpected", "An unexpected error stopped it. The Console has the details.");
        public static readonly LocString EmptyNothingUnderCap = new("market.empty.nothingUnderCap", "No map was listed at or under its price cap within the budget.");

        public static readonly LocString ChatStarted = new("market.chat.started", "Heading to the market board.");
        public static readonly LocString ChatBought = new("market.chat.bought", "Bought {0} for {1} gil.");
        public static readonly LocString ChatNoneUnderCap = new("market.chat.noneUnderCap", "No {0} is listed at or under {1} gil.");
        public static readonly LocString ChatCheapestWorld = new("market.chat.cheapestWorld", "{0} is cheapest on {1} at {2} gil; travelling there to buy it.");
        public static readonly LocString ChatBoughtAhead = new("market.chat.boughtAhead", "Bought a second map ahead; the next map needs no trip to the board.");
        public static readonly LocString ChatOverBudget = new("market.chat.overBudget", "{0} is listed under its cap, but not within the budget and the gil to keep.");
        public static readonly LocString ChatUniqueHeld = new("market.chat.uniqueHeld", "The game says you already have {0}, so this run skips that tier.");
        public static readonly LocString ChatStoppedGil = new("market.chat.stoppedGil", "Not enough gil for the map, so buying stops for this run.");
        public static readonly LocString ChatFailed = new("market.chat.failed", "Could not buy a map. {0}");
        public static readonly LocString ChatNothingBought = new("market.chat.nothingBought", "No map bought. {0}");
        public static readonly LocString ChatRefused = new("market.chat.refused", "Cannot buy a map right now. {0}");
        public static readonly LocString ChatNotTradable = new("market.chat.notTradable", "{0} cannot be bought: it is untradable or an undersea map.");
        public static readonly LocString ChatNoCap = new("market.chat.noCap", "{0} has no price cap. Settings, Maps sets one.");
        public static readonly LocString ChatInBags = new("market.chat.inBags", "{0} is already in your bags, and only one fits.");

        public static readonly LocString BuyMap = new("market.buyMap", "Buy a map");
        public static readonly LocString BuyMapHint = new("market.buyMapHint", "Buy the next map under its price cap on the market board.");
        public static readonly LocString TooltipLooted = new("market.tooltip.looted", "Gil looted");
        public static readonly LocString TooltipSpent = new("market.tooltip.spent", "Gil spent on maps");
        public static readonly LocString TooltipBought = new("market.tooltip.bought", "Maps bought");
    }
}
