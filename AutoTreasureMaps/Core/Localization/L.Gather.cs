namespace AutoTreasureMaps.Core.Localization;

internal static partial class L
{
    internal static class Gather
    {
        public static readonly LocString Group = new("gather.group", "Daily map");
        public static readonly LocString Enabled = new("gather.enabled", "Gather the daily map");
        public static readonly LocString EnabledHelp = new("gather.enabledHelp", "Once a day the game lets each character gather one timeworn map as a Miner or Botanist. When the run has no map left, it switches to the gatherer's gearset, gathers the map, switches back to your gearset and deciphers it.");
        public static readonly LocString Gatherer = new("gather.gatherer", "Gatherer");
        public static readonly LocString GathererHelp = new("gather.gathererHelp", "Which class gathers the map. Its gearset is found by class, wherever it sits in your gearset list.");
        public static readonly LocString GathererAutomaticName = new("gather.gatherer.automatic.name", "Automatic");
        public static readonly LocString GathererAutomaticDetail = new("gather.gatherer.automatic.detail", "Whichever of your Miner and Botanist gearsets has the higher level, the Botanist on a tie.");
        public static readonly LocString GathererMinerName = new("gather.gatherer.miner.name", "Miner");
        public static readonly LocString GathererMinerDetail = new("gather.gatherer.miner.detail", "Always gather as a Miner.");
        public static readonly LocString GathererBotanistName = new("gather.gatherer.botanist.name", "Botanist");
        public static readonly LocString GathererBotanistDetail = new("gather.gatherer.botanist.detail", "Always gather as a Botanist.");
        public static readonly LocString Visits = new("gather.visits", "Nodes per attempt");
        public static readonly LocString VisitsHelp = new("gather.visitsHelp", "How many nodes one attempt opens before it gives up until the next run. A node shows the map only by chance, so a node without it is gathered out to bring up the next one.");
        public static readonly LocString VisitsFormat = new("gather.visitsFormat", "%d nodes");
        public static readonly LocString TierNote = new("gather.tierNote", "The map gathered is the highest enabled tier the gatherer's level allows, skipping tiers already in your bags. A Miner skips one level 100 tier, as its only Miner nodes hold nothing but collectables. Map tiers below chooses which tiers are enabled.");

        public static readonly LocString LineAvailable = new("gather.line.available", "Daily map: available now");
        public static readonly LocString LineWaiting = new("gather.line.waiting", "Daily map: in {0}");
        public static readonly LocString LineOff = new("gather.line.off", "Daily map: off");
        public static readonly LocString LineChecking = new("gather.line.checking", "Daily map: checking…");
        public static readonly LocString HoursMinutes = new("gather.hoursMinutes", "{0} h {1} min");
        public static readonly LocString Minutes = new("gather.minutes", "{0} min");

        public static readonly LocString ReasonOff = new("gather.reason.off", "Gathering the daily map is switched off.");
        public static readonly LocString ReasonUnknown = new("gather.reason.unknown", "The daily map allowance has not been read from the server yet.");
        public static readonly LocString ReasonNotYet = new("gather.reason.notYet", "The next daily map can be gathered in {0}.");
        public static readonly LocString ReasonNoGearset = new("gather.reason.noGearset", "No Miner or Botanist gearset was found. Save one in the gearset list first.");
        public static readonly LocString ReasonNoMinerGearset = new("gather.reason.noMinerGearset", "No Miner gearset was found. Save one in the gearset list first.");
        public static readonly LocString ReasonNoBotanistGearset = new("gather.reason.noBotanistGearset", "No Botanist gearset was found. Save one in the gearset list first.");
        public static readonly LocString ReasonLevelTooLow = new("gather.reason.levelTooLow", "The gatherer's level is below every enabled map tier it could gather.");
        public static readonly LocString ReasonNoEnabledTier = new("gather.reason.noEnabledTier", "None of the map tiers a gatherer can find is enabled in Settings, Maps.");
        public static readonly LocString ReasonAlreadyHeld = new("gather.reason.alreadyHeld", "Your bags already hold every map tier the gatherer could gather, and only one of each fits.");
        public static readonly LocString ReasonBagsFull = new("gather.reason.bagsFull", "Your bags are full.");
        public static readonly LocString ReasonInCombat = new("gather.reason.inCombat", "The character stayed in combat, so the gearset could not be changed.");
        public static readonly LocString ReasonSwitchFailed = new("gather.reason.switchFailed", "The game did not switch to the gatherer's gearset.");
        public static readonly LocString ReasonTravelFailed = new("gather.reason.travelFailed", "Could not reach the gathering area.");
        public static readonly LocString ReasonNoNode = new("gather.reason.noNode", "No gathering node of the area could be found.");
        public static readonly LocString ReasonNoMap = new("gather.reason.noMap", "None of the nodes opened showed the map. The next run tries again.");
        public static readonly LocString ReasonLimitReached = new("gather.reason.limitReached", "The game says today's map has already been gathered.");
        public static readonly LocString ReasonAlreadyCarrying = new("gather.reason.alreadyCarrying", "You already carry that map, and only one fits.");
        public static readonly LocString ReasonRateZero = new("gather.reason.rateZero", "The map's gathering rate is 0%. The gatherer's Gathering stat is too low.");
        public static readonly LocString ReasonNotConfirmed = new("gather.reason.notConfirmed", "The game never confirmed the map was gathered.");
        public static readonly LocString ReasonAttacked = new("gather.reason.attacked", "The gatherer was attacked, so it left the area instead of fighting.");
        public static readonly LocString ReasonSwitchBackFailed = new("gather.reason.switchBackFailed", "Could not switch back to your gearset. Switch back by hand before going on.");

        public static readonly LocString ChatStarted = new("gather.chat.started", "Gathering the daily map as a {0} in {1}.");
        public static readonly LocString ChatGathered = new("gather.chat.gathered", "Gathered {0}.");
        public static readonly LocString ChatNoMap = new("gather.chat.noMap", "No daily map this time. {0}");
        public static readonly LocString ChatFailed = new("gather.chat.failed", "Could not gather the daily map. {0}");
        public static readonly LocString ChatRefused = new("gather.chat.refused", "Cannot gather the daily map right now. {0}");
        public static readonly LocString ChatNotRunning = new("gather.chat.notRunning", "No gather is under way.");
        public static readonly LocString ChatStopped = new("gather.chat.stopped", "Gather stopped.");

        public static readonly LocString CommandHelp = new("gather.commandHelp", "/atm gather - gather today's timeworn map once as a Miner or Botanist\n/atm gather stop - stop the gathering");
        public static readonly LocString PhaseGathering = new("gather.phase", "Gathering");
        public static readonly LocString MapsGathered = new("gather.mapsGathered", "Maps gathered");
        public static readonly LocString ReasonNoReturnGearset = new("gather.reason.noReturnGearset", "No gearset holds the class you are on, so the gather could not switch you back. Save one in the gearset list first.");
    }
}
