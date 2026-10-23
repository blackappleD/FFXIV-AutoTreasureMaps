namespace AutoTreasureMaps.Core.Localization;

internal static partial class L
{
    internal static class Common
    {
        public static readonly LocString Close = new("common.close", "Close");
        public static readonly LocString Cancel = new("common.cancel", "Cancel");
        public static readonly LocString Clear = new("common.clear", "Clear");
        public static readonly LocString Pause = new("common.pause", "Pause");
        public static readonly LocString Resume = new("common.resume", "Resume");
        public static readonly LocString StopRun = new("common.stopRun", "Stop the run");
        public static readonly LocString Working = new("common.working", "Working…");
        public static readonly LocString DragAdjustHint = new("common.dragAdjustHint", "Drag to adjust · Ctrl+click to type");
        public static readonly LocString NoMatches = new("common.noMatches", "Nothing matches “{0}”.");
    }

    internal static class Shell
    {
        public static readonly LocString NavTreasure = new("shell.nav.treasure", "Treasure");
        public static readonly LocString NavSettings = new("shell.nav.settings", "Settings");
        public static readonly LocString NavHistory = new("shell.nav.history", "History");
        public static readonly LocString NavPlugins = new("shell.nav.plugins", "Plugins");
        public static readonly LocString NavLog = new("shell.nav.log", "Console");
        public static readonly LocString NavChangelog = new("shell.nav.changelog", "Changelog");
        public static readonly LocString NavAbout = new("shell.nav.about", "About");
        public static readonly LocString StatusRunning = new("shell.status.running", "Running");
        public static readonly LocString StatusPaused = new("shell.status.paused", "Paused");
        public static readonly LocString StatusSetupNeeded = new("shell.status.setupNeeded", "Setup needed");
        public static readonly LocString StatusIdle = new("shell.status.idle", "Idle");
        public static readonly LocString Minimize = new("shell.minimize", "Minimize to the title strip");
        public static readonly LocString Restore = new("shell.restore", "Restore the window");
        public static readonly LocString ResumeBlocked = new("shell.resumeBlocked", "Resumes automatically once you leave the duty");
        public static readonly LocString GreetingMorning = new("shell.greeting.morning", "Good morning");
        public static readonly LocString GreetingAfternoon = new("shell.greeting.afternoon", "Good afternoon");
        public static readonly LocString GreetingEvening = new("shell.greeting.evening", "Good evening");
        public static readonly LocString GreetingNight = new("shell.greeting.night", "Late night");
    }

    internal static class Treasure
    {
        public static readonly LocString TitleSetupNeeded = new("treasure.title.setupNeeded", "Setup needed");
        public static readonly LocString DetailSetupNeeded = new("treasure.detail.setupNeeded", "Install the required plugins before your first run.");
        public static readonly LocString TitleBlocked = new("treasure.title.blocked", "Not ready");
        public static readonly LocPlural DetailReady = new("treasure.detail.ready", "{0} map ready to run", "{0} maps ready to run");
        public static readonly LocString DetailFollow = new("treasure.detail.follow", "In a party: Start follows the leader to each dig spot it posts, with no map of your own.");
        public static readonly LocString TitleRunning = new("treasure.title.running", "Hunting treasure");
        public static readonly LocString TitlePaused = new("treasure.title.paused", "Paused");
        public static readonly LocString DetailPausedInContent = new("treasure.detail.pausedInContent", "Resumes once you leave the duty");
        public static readonly LocString DetailPausedManual = new("treasure.detail.pausedManual", "Resume whenever you're ready");
        public static readonly LocString OpenPlugins = new("treasure.openPlugins", "Open plugins");
        public static readonly LocString NoRunsYet = new("treasure.noRunsYet", "No runs yet");
        public static readonly LocString StatsAppearHere = new("treasure.statsAppearHere", "your stats will appear here");
        public static readonly LocString LastRun = new("treasure.lastRun", "Last run  ·  {0} maps");
        public static readonly LocString SentenceEnd = new("treasure.sentence.end", ".");

        public static readonly LocString CurrentMap = new("treasure.currentMap", "Current map");
        public static readonly LocString CurrentMapEmpty = new("treasure.currentMapEmpty", "No deciphered map yet. Decipher a timeworn map from your inventory and its dig spot shows up here.");

        public static readonly LocString WhenDone = new("treasure.whenDone", "When the run ends");
        public static readonly LocString WhenDoneHelp = new("treasure.whenDoneHelp", "What to do once a run ends on its own. A manual Stop or a fault never triggers it.");
        public static readonly LocString AfterStayName = new("treasure.after.stay.name", "Stay where you are");
        public static readonly LocString AfterStayDetail = new("treasure.after.stay.detail", "Just stop. You're left standing wherever the last coffer was opened.");
        public static readonly LocString AfterInnName = new("treasure.after.inn.name", "Return to the inn");
        public static readonly LocString AfterInnDetail = new("treasure.after.inn.detail", "Travel to your Grand Company city and enter the inn room.");
        public static readonly LocString AfterLogoutName = new("treasure.after.logout.name", "Log out to title");
        public static readonly LocString AfterLogoutDetail = new("treasure.after.logout.detail", "Log out to the title screen.");
        public static readonly LocString AfterCloseName = new("treasure.after.close.name", "Close the game");
        public static readonly LocString AfterCloseDetail = new("treasure.after.close.detail", "Close FFXIV entirely (via XIVLauncher's /xlkill).");

        public static readonly LocString ExpansionArr = new("treasure.expansion.arr", "A Realm Reborn");
        public static readonly LocString ExpansionHw = new("treasure.expansion.hw", "Heavensward");
        public static readonly LocString ExpansionSb = new("treasure.expansion.sb", "Stormblood");
        public static readonly LocString ExpansionShb = new("treasure.expansion.shb", "Shadowbringers");
        public static readonly LocString ExpansionEw = new("treasure.expansion.ew", "Endwalker");
        public static readonly LocString ExpansionDt = new("treasure.expansion.dt", "Dawntrail");

        public static readonly LocString Start = new("treasure.start", "START");
        public static readonly LocString Stop = new("treasure.stop", "STOP");
        public static readonly LocString PauseCaps = new("treasure.pause", "PAUSE");
        public static readonly LocString ResumeCaps = new("treasure.resume", "RESUME");
        public static readonly LocString InContent = new("treasure.inContent", "in content");
        public static readonly LocString ReasonInstall = new("treasure.reason.install", "install the required plugins");
        public static readonly LocString ReasonNoMaps = new("treasure.reason.noMaps", "no maps to run");
        public static readonly LocString ReasonHeldMap = new("treasure.reason.heldMap", "the held map cannot be run");
        public static readonly LocString ReasonNotReady = new("treasure.reason.notReady", "not ready yet");
        public static readonly LocPlural StartMaps = new("treasure.startMaps", "{0} map to run", "{0} maps to run");
        public static readonly LocString StartFollow = new("treasure.startFollow", "Follow the party leader");
        public static readonly LocString StateRunning = new("treasure.state.running", "running");
        public static readonly LocString StatePaused = new("treasure.state.paused", "paused");
        public static readonly LocString StopSub = new("treasure.stopSub", "{0} · {1}");
        public static readonly LocString StopAfterMap = new("treasure.stopAfterMap", "AFTER MAP");
        public static readonly LocString StopAfterMapSub = new("treasure.stopAfterMapSub", "stop after this one");
        public static readonly LocString StopAfterMapArmedSub = new("treasure.stopAfterMapArmedSub", "set · click to undo");
        public static readonly LocString StopAfterMapTooltip = new("treasure.stopAfterMapTooltip", "Stop once this map is done");
        public static readonly LocString StopAfterMapArmedTooltip = new("treasure.stopAfterMapArmedTooltip", "Stops once this map is done. Click to keep going.");
    }

    internal static class Run
    {
        public static readonly LocString PhasePreparing = new("run.phase.preparing", "Getting ready");
        public static readonly LocString PhaseDeciphering = new("run.phase.deciphering", "Deciphering");
        public static readonly LocString PhaseBuying = new("run.phase.buying", "Buying a map");
        public static readonly LocString PhaseTravelling = new("run.phase.travelling", "Travelling");
        public static readonly LocString PhaseFighting = new("run.phase.fighting", "Fighting");
        public static readonly LocString PhaseFinishing = new("run.phase.finishing", "Finishing up");
        public static readonly LocString PhaseStandingBy = new("run.phase.standingBy", "Standing by");
        public static readonly LocString PhaseReady = new("run.phase.ready", "Ready");
        public static readonly LocString PhasePaused = new("run.phase.paused", "Paused");
        public static readonly LocString PhasePausedInContent = new("run.phase.pausedInContent", "Paused (in content)");
        public static readonly LocString StopAfterMapChip = new("run.stopAfterMapChip", "Last map");
        public static readonly LocString SomewhereElse = new("run.somewhereElse", "Somewhere else");
        public static readonly LocPlural MapsDone = new("run.mapsDone", "{0} map done", "{0} maps done");
        public static readonly LocString MapsOfLimit = new("run.mapsOfLimit", "{0} of {1} maps done");
        public static readonly LocString TileElapsed = new("run.tile.elapsed", "Elapsed");
        public static readonly LocString TileLooted = new("run.tile.looted", "Looted");
        public static readonly LocString TileGil = new("run.tile.gil", "Gil");
        public static readonly LocString TilePortals = new("run.tile.portals", "Portals");
        public static readonly LocPlural Failed = new("run.failed", "{0} failed", "{0} failed");
        public static readonly LocString MinutesLeft = new("run.minutesLeft", "{0} min left");
        public static readonly LocString TooltipMapsRun = new("run.tooltip.mapsRun", "Maps run: {0}");
        public static readonly LocString TooltipCount = new("run.tooltip.count", "{0}: {1}");
        public static readonly LocString TooltipNotable = new("run.tooltip.notable", "Notable items:");
        public static readonly LocString TooltipItem = new("run.tooltip.item", "{0} ×{1}");

        public static readonly LocString SettingsGroup = new("run.settings.group", "Run");
        public static readonly LocString MapLimit = new("run.settings.mapLimit", "Stop after maps");
        public static readonly LocString MapLimitHelp = new("run.settings.mapLimitHelp", "End the run once this many maps have been run, whatever came of them. At 0 it goes on until no enabled map is left.");
        public static readonly LocString MinuteLimit = new("run.settings.minuteLimit", "Stop after minutes");
        public static readonly LocString MinuteLimitHelp = new("run.settings.minuteLimitHelp", "End the run once it has gone this long, pauses not counted. A map under way is finished first. At 0 there is no time limit.");
        public static readonly LocString FailureLimit = new("run.settings.failureLimit", "Stop after failed maps in a row");
        public static readonly LocString FailureLimitHelp = new("run.settings.failureLimitHelp", "End the run once this many maps in a row end with nothing dug up, the spot out of reach or an error, so it never loops on a bad spot.");
        public static readonly LocString NoLimit = new("run.settings.noLimit", "No limit");
        public static readonly LocString AlsoStops = new("run.settings.alsoStops", "A run also stops once no enabled map is left, when a map is refused (a portal map, an undersea spot, a duty, or a party member who stays out of the area for a minute) and after a second knockout.");

        public static readonly LocPlural ChatStarted = new("run.chat.started", "Run started with {0} map to go.", "Run started with {0} maps to go.");
        public static readonly LocString ChatCannotStart = new("run.chat.cannotStart", "Cannot start the run. {0}");
        public static readonly LocString ChatStopped = new("run.chat.stopped", "Stopped.");
        public static readonly LocString ChatNothingToStop = new("run.chat.nothingToStop", "Nothing to stop.");
        public static readonly LocPlural ChatStopAfterMapOn = new("run.chat.stopAfterMapOn", "The run stops once this map is done, its treasure dungeon included ({0} map run so far).", "The run stops once this map is done, its treasure dungeon included ({0} maps run so far).");
        public static readonly LocString ChatStopAfterMapOff = new("run.chat.stopAfterMapOff", "Taken back: the run goes on after this map.");
        public static readonly LocString ChatStopAfterMapSingle = new("run.chat.stopAfterMapSingle", "This run is a single map or dungeon and stops after it anyway.");
        public static readonly LocString ChatPartyWait = new("run.chat.partyWait", "A party member is not in the area, so the game will not allow the dig. Waiting up to {0} seconds for the party.");
        public static readonly LocString ChatFollowStarted = new("run.chat.followStarted", "Party mode: following the party leader to each dig spot it posts.");
        public static readonly LocString ChatFollowNeedsParty = new("run.chat.followNeedsParty", "Party mode needs a party on your own world. The party leader runs the maps; press Start on the other members to follow it.");
        public static readonly LocString ChatFollowEnded = new("run.chat.followEnded", "The party or its leader is gone, or no spot came for a long while, so following ends.");
        public static readonly LocString ChatFollowWorld = new("run.chat.followWorld", "The leader is buying maps on {0}; travelling there and waiting for its invite.");
        public static readonly LocString ChatRegroupFailed = new("run.chat.regroupFailed", "The party did not join again after the trip to buy maps, so the run goes on alone.");
        public static readonly LocString ChatRegroupCrossWorld = new("run.chat.regroupCrossWorld", "The party formed again as a cross-world party, whose members cannot fight the leader's guards. Open the party list and change it to a party on this world.");
        public static readonly LocString ChatOutOfMaps = new("run.chat.outOfMaps", "Run over: no maps left to run.");
        public static readonly LocString ChatOutOfMapsReason = new("run.chat.outOfMapsReason", "Run over: no maps left to run. {0}");
        public static readonly LocPlural ChatMapLimit = new("run.chat.mapLimit", "Run over: {0} map run, the limit you set.", "Run over: {0} maps run, the limit you set.");
        public static readonly LocPlural ChatTimeLimit = new("run.chat.timeLimit", "Run over: the {0} minute limit you set is up.", "Run over: the {0} minute limit you set is up.");
        public static readonly LocPlural ChatFailureStreak = new("run.chat.failureStreak", "Run over: {0} map in a row failed, so the run stops before it loops on a bad spot.", "Run over: {0} maps in a row failed, so the run stops before it loops on a bad spot.");
        public static readonly LocPlural ChatStopAfterMapDone = new("run.chat.stopAfterMapDone", "Run over: stopped after the map as you asked, {0} map run.", "Run over: stopped after the map as you asked, {0} maps run.");
        public static readonly LocString ChatKnockedOutTwice = new("run.chat.knockedOutTwice", "Run over: knocked out twice in one run.");
        public static readonly LocString ChatRefused = new("run.chat.refused", "Run over: the map was refused, for the reason above.");
        public static readonly LocString ChatSupplyFailed = new("run.chat.supplyFailed", "Run over: the next map could not be readied.");
        public static readonly LocString ChatNavmeshInput = new("run.chat.navmeshInput", "The pathfinder has \"Cancel current path on player movement input\" turned on, so any movement key you press stops the run's travel. Turn it off in the pathfinder's settings for smooth runs.");

        public static readonly LocString EndStopped = new("run.end.stopped", "Stopped");
        public static readonly LocString EndOutOfMaps = new("run.end.outOfMaps", "Out of maps");
        public static readonly LocString EndMapLimit = new("run.end.mapLimit", "Map limit");
        public static readonly LocString EndTimeLimit = new("run.end.timeLimit", "Time limit");
        public static readonly LocString EndFailureStreak = new("run.end.failureStreak", "Failures in a row");
        public static readonly LocString EndKnockedOutTwice = new("run.end.knockedOutTwice", "Knocked out twice");
        public static readonly LocString EndRefused = new("run.end.refused", "Map refused");
        public static readonly LocString EndSupplyFailed = new("run.end.supplyFailed", "No map readied");
        public static readonly LocString EndStillKnockedOut = new("run.end.stillKnockedOut", "Could not get up");
        public static readonly LocString EndFaulted = new("run.end.faulted", "Error");
        public static readonly LocString EndSingleMap = new("run.end.singleMap", "One map");
        public static readonly LocString EndDungeon = new("run.end.dungeon", "Treasure dungeon");
        public static readonly LocString EndDungeonHandedBack = new("run.end.dungeonHandedBack", "Dungeon left to you");
        public static readonly LocString EndPartyEnded = new("run.end.partyEnded", "Following ended");
        public static readonly LocString EndStopAfterMap = new("run.end.stopAfterMap", "Stopped after the map");
    }

    internal static class History
    {
        public static readonly LocString Title = new("history.title", "History");
        public static readonly LocString Empty = new("history.empty", "Your finished runs will show up here.");
        public static readonly LocPlural Summary = new("history.summary", "{0} recorded run  ·  {1} in total  ·  {2} maps/h average", "{0} recorded runs  ·  {1} in total  ·  {2} maps/h average");
        public static readonly LocString TileMaps = new("history.tile.maps", "Maps");
        public static readonly LocString TileGil = new("history.tile.gil", "Gil");
        public static readonly LocString TilePortals = new("history.tile.portals", "Portals seen");
        public static readonly LocPlural TileRunsCount = new("history.tile.runsCount", "{0} run", "{0} runs");
        public static readonly LocString TilePerMap = new("history.tile.perMap", "{0} per map");
        public static readonly LocString TileCost = new("history.tile.cost", "Cost");
        public static readonly LocString TileRevenue = new("history.tile.revenue", "Revenue");
        public static readonly LocString TileNet = new("history.tile.net", "Net {0}");
        public static readonly LocString Items = new("history.items", "Items gained");
        public static readonly LocString TileTomestones = new("history.tile.tomestones", "Tomestones");
        public static readonly LocString EndedBy = new("history.endedBy", "Ended");
        public static readonly LocString NoRuns = new("history.noRuns", "No runs recorded yet. Finish (or stop) a run and it'll show up here.");
        public static readonly LocString MapsPerRun = new("history.mapsPerRun", "Maps per run");
        public static readonly LocString RecentRuns = new("history.recentRuns", "Recent runs");
        public static readonly LocPlural ChartRange = new("history.chartRange", "last run  ·  oldest to newest", "last {0} runs  ·  oldest to newest");
        public static readonly LocString ChartPeak = new("history.chartPeak", "peak {0}");
        public static readonly LocString ChartTooltip = new("history.chartTooltip", "{0}  ·  {1} maps  ·  {2}");
        public static readonly LocString RowDetail = new("history.rowDetail", "{0}  ·  {1}");
        public static readonly LocString TooltipRate = new("history.tooltip.rate", "Rate: {0} maps/h");
        public static readonly LocString JustNow = new("history.time.justNow", "just now");
        public static readonly LocString MinutesAgo = new("history.time.minutesAgo", "{0}m ago");
        public static readonly LocString HoursAgo = new("history.time.hoursAgo", "{0}h ago");
        public static readonly LocString DaysAgo = new("history.time.daysAgo", "{0}d ago");
        public static readonly LocString ClearHistory = new("history.clear", "Clear history");
        public static readonly LocString ClearQuestion = new("history.clearQuestion", "Delete all recorded runs and the map log?");
        public static readonly LocString ClearYes = new("history.clearYes", "Yes, clear");
        public static readonly LocString MapLogTab = new("history.mapLogTab", "Map log");
        public static readonly LocString NoMapLog = new("history.noMapLog", "No maps logged yet. Every map a run finishes is added here.");
        public static readonly LocString LogNumber = new("history.log.number", "No.");
        public static readonly LocString LogMap = new("history.log.map", "Map");
        public static readonly LocString LogPortal = new("history.log.portal", "Portal");
        public static readonly LocString LogFloor = new("history.log.floor", "Floor");
        public static readonly LocString LogSpecial = new("history.log.special", "Special round");
        public static readonly LocString LogSackRound = new("history.log.sackRound", "Sack round {0}/100");
        public static readonly LocString LogDeaths = new("history.log.deaths", "Deaths");
        public static readonly LocString LogDuration = new("history.log.duration", "Time");
        public static readonly LocString LogLoot = new("history.log.loot", "Rare loot");
        public static readonly LocString LogYes = new("history.log.yes", "Yes");
        public static readonly LocString LogLootItem = new("history.log.lootItem", "{0}×{1}");
        public static readonly LocString LogLootSeparator = new("history.log.lootSeparator", ", ");
        public static readonly LocString LogAllMaps = new("history.log.allMaps", "All maps ({0})");
        public static readonly LocString LogMapFilter = new("history.log.mapFilter", "{0} ({1})");
        public static readonly LocString LogPage = new("history.log.page", "Page {0} of {1} · {2} maps");
        public static readonly LocString LogPreviousPage = new("history.log.previousPage", "Previous page");
        public static readonly LocString LogNextPage = new("history.log.nextPage", "Next page");
    }

    internal static class Plugins
    {
        public static readonly LocString Title = new("plugins.title", "Plugins");
        public static readonly LocString AllInstalled = new("plugins.allInstalled", "All required plugins are installed and loaded.");
        public static readonly LocPlural Missing = new("plugins.missing", "{0} required plugin is missing.", "{0} required plugins are missing.");
        public static readonly LocString Required = new("plugins.required", "Required");
        public static readonly LocString Optional = new("plugins.optional", "Optional");
        public static readonly LocString Installed = new("plugins.installed", "Installed");
        public static readonly LocString Install = new("plugins.install", "Install");
        public static readonly LocString Installing = new("plugins.installing", "Installing…");
        public static readonly LocString RepoHint = new("plugins.repoHint", "Repo: {0}\nLeft-click to open repo URL · right-click to copy");
        public static readonly LocString Footer = new("plugins.footer",
            "Install adds the plugin's source repository to Dalamud and queues an install. If one-click install fails (URL drift, network), right-click a plugin name to copy its repo URL and add it manually via /xlsettings -> Experimental -> Custom Plugin Repositories.");
        public static readonly LocString PurposeVnavmesh = new("plugins.purpose.vnavmesh", "Pathfinding, flying, and movement to each dig spot.");
        public static readonly LocString PurposeBossMod = new("plugins.purpose.bossMod", "Auto-rotation, targeting, and dodging while fighting coffer guards.");
        public static readonly LocString PurposeLifestream = new("plugins.purpose.lifestream", "World visits, to buy maps on the cheapest world of the data centre.");
    }

    internal static class Log
    {
        public static readonly LocString Title = new("log.title", "Console");
        public static readonly LocPlural Entries = new("log.entries", "{0} line in the buffer", "{0} lines in the buffer");
        public static readonly LocString Empty = new("log.empty", "Nothing logged yet. Start a run and every step the plugin takes shows up here.");
        public static readonly LocString NoMatches = new("log.noMatches", "No lines match the current filters.");
        public static readonly LocString Footer = new("log.footer", "Every line also goes to the Dalamud log (/xllog) with the {0} prefix. When reporting a bug, press Copy log and paste the result into the issue.");
        public static readonly LocString SearchHint = new("log.searchHint", "Search messages and sources");
        public static readonly LocString CopyAll = new("log.copyAll", "Copy log");
        public static readonly LocPlural CopyFiltered = new("log.copyFiltered", "Copy {0} line", "Copy {0} lines");
        public static readonly LocString CopyTooltip = new("log.copyTooltip", "Copies the lines shown below with a header naming the plugin version, Dalamud version and zone, ready to paste into a bug report.");
        public static readonly LocString Copied = new("log.copied", "Copied");
        public static readonly LocPlural CopiedLines = new("log.copiedLines", "Copied {0} line to the clipboard", "Copied {0} lines to the clipboard");
        public static readonly LocString Clear = new("log.clear", "Clear");
        public static readonly LocString ConfirmClear = new("log.confirmClear", "Click again to clear");
        public static readonly LocString Close = new("log.close", "Close");
        public static readonly LocString LevelVerbose = new("log.level.verbose", "Verbose");
        public static readonly LocString LevelDebug = new("log.level.debug", "Debug");
        public static readonly LocString LevelInfo = new("log.level.info", "Info");
        public static readonly LocString LevelWarning = new("log.level.warning", "Warnings");
        public static readonly LocString LevelError = new("log.level.error", "Errors");
        public static readonly LocString LevelTooltip = new("log.levelTooltip", "Click to show or hide these lines. Shift-click to show only this level.");
        public static readonly LocString SourceChip = new("log.sourceChip", "Source: {0}");
        public static readonly LocString SourceChipTooltip = new("log.sourceChipTooltip", "Click to stop filtering by source.");
        public static readonly LocString JumpLatest = new("log.jumpLatest", "Jump to latest");
        public static readonly LocPlural NewLines = new("log.newLines", "{0} new line", "{0} new lines");
        public static readonly LocString Showing = new("log.showing", "Showing {0} of {1}");
        public static readonly LocString ResetFilters = new("log.resetFilters", "Reset filters");
        public static readonly LocString Shortcuts = new("log.shortcuts", "Ctrl+F search · Ctrl+C copy · Shift-click selects a range · Double-click copies a line");
        public static readonly LocPlural Selected = new("log.selected", "{0} line selected", "{0} lines selected");
        public static readonly LocString CopySelection = new("log.copySelection", "Copy selection");
        public static readonly LocString ClearSelection = new("log.clearSelection", "Clear selection");
        public static readonly LocString CopyLine = new("log.copyLine", "Copy line");
        public static readonly LocString CopyToEnd = new("log.copyToEnd", "Copy from here to the end");
        public static readonly LocString OnlySource = new("log.onlySource", "Show only {0}");
        public static readonly LocString Repeated = new("log.repeated", "Repeated {0} times in a row");
        public static readonly LocString HasDetails = new("log.hasDetails", "Has a stack trace. Select the line to read it.");
    }

    internal static class Changelog
    {
        public static readonly LocString Title = new("changelog.title", "What's new");
        public static readonly LocString Subtitle = new("changelog.subtitle", "Every update, newest first.");
        public static readonly LocString Version = new("changelog.version", "Version {0}");
        public static readonly LocString Latest = new("changelog.latest", "Latest");
        public static readonly LocString New = new("changelog.new", "New");
        public static readonly LocPlural Changes = new("changelog.changes", "{0} change", "{0} changes");

        public static readonly LocString[] Release1300 =
        [
            new("changelog.r1300.1", "The History totals now show Cost (the gil spent on maps) and Revenue (the gil looted, plus the rare loot's worth when loot prices are on, with the net below it), and they count the run under way map by map. Contributed by blackappleD"),
            new("changelog.r1300.2", "Each map's loot list in the History stats now starts folded: click the map's card to open it. Contributed by blackappleD"),
            new("changelog.r1300.3", "Mitigate tankbusters now answers a treasure dungeon boss's tankbuster in time with your short mitigation plus one bigger cooldown, strongest first. While it is on, it handles your personal mitigations itself, so a pack of mobs gets at most one bigger cooldown every 20 seconds instead of all of them. Contributed by blackappleD"),
            new("changelog.r1300.4", "Fixed the Gambler's Lure: a tied guess no longer reads as the end of the lure, so the run waits for the new cards instead of walking off to the locked gate. Challenge by the loot now takes the treasure as it is when the coffer holds rare loot you keep, and only that choice guesses for you. Contributed by blackappleD"),
            new("changelog.r1300.5", "With Kill the mandragora party in order off, mandragoras pulled in by an area attack are now left for last while other enemies are up, and a coffer or gate left targeted no longer stalls a fight. Contributed by blackappleD"),
            new("changelog.r1300.6", "Auto-discard now also discards looted copies of an item that does not stack when your bags already held one, and an item link in chat can be added to the cleanup whitelist from its right-click menu. Contributed by blackappleD"),
            new("changelog.r1300.7", "The chat now says the run is buying on another world only when it really travels there; when no other world qualifies, the run waits out the busy market board instead. Contributed by blackappleD"),
        ];

        public static readonly LocString[] Release1200 =
        [
            new("changelog.r1200.1", "Added Chat commands to the settings: list your own commands, one per line (such as /li home or /ays m), and they run when a run finishes on its own, before the after-run action, never after Stop or a failure"),
            new("changelog.r1200.2", "Fixed a run waiting at a hub aetheryte forever when none of the aethernet stops into a zone are attuned, as with the Idyllshire gates to the Hinterlands: it now stops with a message saying a stop needs attuning"),
            new("changelog.r1200.3", "The main window now moves only when you drag its top bar, so a drag inside it no longer moves it by accident; turn on Drag from anywhere in the settings for the old behaviour"),
        ];

        public static readonly LocString[] Release1100 =
        [
            new("changelog.r1100.1", "Added treasure dungeons (experimental): with Enter the portal on under Dungeons (off by default), a solo run walks into the portal a map turns up, clears Cenote Ja Ja Gural or the Vault Oneiron, then moves on to the next map; /atm dig does the same. Contributed by blackappleD"),
            new("changelog.r1100.2", "In treasure dungeons the run follows the glowing gate hint or a gate side you pick, fights a boss's adds before the boss, collects the bonus round's sacks and leaves the mandragora party alone unless you ask; Attack bonus enemies first can now be set per dungeon. Contributed by blackappleD"),
            new("changelog.r1100.3", "Added a Gambler's Lure choice for treasure dungeons: Let me choose (the default) waits out the whole lure and suggests the better guess in chat, Open the coffer skips the game, and Challenge by the loot plays it on each card's odds, more carefully when the coffer holds rare loot you keep. Contributed by blackappleD"),
            new("changelog.r1100.4", "Added Buy on the cheapest world (off by default): with Lifestream installed, the run checks Universalis and buys maps on another world of your data centre when that saves enough gil, skips worlds whose listings are over 12 hours old, and goes back to its starting world when the run ends. Contributed by blackappleD"),
            new("changelog.r1100.5", "Added Buy a map ahead and Buy a spare before the first dig (both off by default), so back-to-back maps need fewer trips to the market board, and a busy market board is now waited out instead of ending the visit. Contributed by blackappleD"),
            new("changelog.r1100.6", "Coffer guards go down faster, as the combat preset now spreads area attacks over the whole wave, and the new Mitigate tankbusters setting (off by default) uses a tank's short mitigation near the end of a heavy cast. Contributed by blackappleD"),
            new("changelog.r1100.7", "Added per-map stats on the Treasure and History pages, and a Map log in History with each map's portal, dungeon floor, deaths, time and rare loot; tomestones are now counted on their own, like gil. Contributed by blackappleD"),
            new("changelog.r1100.8", "Added Loot cleanup (off by default): it discards the low-value items coffers and dungeons drop, never anything you already held, keeps whitelisted and notable items, and lets you pick per map and per dungeon which rare loot to keep. Contributed by blackappleD"),
            new("changelog.r1100.9", "Hovering a map name in the map settings or the market price caps shows the game's item tooltip, each map shows the patch it arrived in, and the /atm help lists one command per line. Contributed by blackappleD"),
            new("changelog.r1100.10", "Fixed digs attempted in mid-air after a flying dismount, and, on the Chinese client, coffers left unopened after their guards fell and missing characters in map, city and loot names. Contributed by blackappleD"),
            new("changelog.r1100.11", "Fixed long sessions ending in a crash or a Start button that stops working; moving around no longer sets up new game hooks each time"),
        ];

        public static readonly LocString[] Release1000 =
        [
            new("changelog.r1000.1", "Runs your timeworn maps from start to finish: deciphers each one, flies to the dig spot, digs up the coffer, fights off its guards and loots it"),
            new("changelog.r1000.2", "Reads the exact dig spot of the map you hold, shows it on the Treasure page and flags it on your map"),
            new("changelog.r1000.3", "Lets you choose which map tiers a run uses and in what order; undersea maps stay off and 8-player maps carry a warning"),
            new("changelog.r1000.4", "Gathers your daily timeworn map as a Miner or Botanist, switching gearsets there and back"),
            new("changelog.r1000.5", "Buys maps on the market board under a price cap per tier, a budget per run and a gil reserve you set"),
            new("changelog.r1000.6", "Ends a run when you are out of maps, after a set number of maps or minutes, or after repeated failures"),
            new("changelog.r1000.7", "Repairs, eats and takes city breaks between maps, with random reaction delays from the Humanizer"),
            new("changelog.r1000.8", "Records every run in History: maps, coffers, portals seen, gil gained and gil spent"),
            new("changelog.r1000.9", "Leaves treasure dungeon portals alone for now"),
        ];
    }

    internal static class About
    {
        public static readonly LocString SupportTitle = new("about.support.title", "Made with love and care");
        public static readonly LocString SupportBody = new("about.support.body", "This plugin is a one-person project, built in my free time because I love this game and its community. Keeping it updated takes a lot of those hours. If it has helped you, supporting me on Patreon or buying me a coffee means I can keep giving it that time. Thank you for being here.");
        public static readonly LocString SupportButton = new("about.support.button", "Support on Patreon");
        public static readonly LocString PatreonHint = new("about.support.hint", "Open Patreon · right-click to copy");
        public static readonly LocString CoffeeButton = new("about.support.coffeeButton", "Buy me a coffee");
        public static readonly LocString CoffeeHint = new("about.support.coffeeHint", "Open Buy Me a Coffee · right-click to copy");
        public static readonly LocString LinkHint = new("about.linkHint", "Click to open · right-click to copy");
        public static readonly LocString MadeBy = new("about.madeBy", "Made by {0}");
        public static readonly LocString Version = new("about.version", "v {0}");
        public static readonly LocString Community = new("about.community", "Community");
        public static readonly LocString DiscordTitle = new("about.discordTitle", "Join the Discord");
        public static readonly LocString DiscordBody = new("about.discordBody", "Get help, report bugs, share ideas and hear about updates first.");
        public static readonly LocString GitHubTitle = new("about.githubTitle", "View on GitHub");
        public static readonly LocString GitHubBody = new("about.githubBody", "Browse the source code and every release.");
        public static readonly LocString ReminderTitle = new("about.reminder.title", "A little reminder");
        public static readonly LocString FactsTitle = new("about.facts.title", "Did you know?");
        public static readonly LocString QuotesTitle = new("about.quotes.title", "Words to live by");
        public static readonly LocString JokesTitle = new("about.jokes.title", "Just for fun");

        public static readonly LocString[] Reminders =
        [
            new("about.reminder.1", "Been at it a while? Roll your shoulders and take one slow breath."),
            new("about.reminder.2", "Hydration check. When did you last drink some water?"),
            new("about.reminder.3", "Blink a few times and let your eyes rest for a moment."),
            new("about.reminder.4", "Stand up, stretch, and shake out your hands. Future you says thanks."),
            new("about.reminder.5", "Sit up and settle in comfortably. Your back will thank you later."),
            new("about.reminder.6", "Remember to eat something today. You matter more than any score."),
            new("about.reminder.7", "Eyes feel tired? Look at something far away for twenty seconds."),
            new("about.reminder.8", "Whatever you're chasing, you're allowed to take a break whenever."),
            new("about.reminder.9", "You're doing great. Be a little kinder to yourself today."),
            new("about.reminder.10", "A glass of water and a quick stretch can reset a long session."),
            new("about.reminder.11", "Unclench your jaw and drop your shoulders. There you go."),
            new("about.reminder.12", "Rest is part of the journey too. Step away whenever you need to."),
        ];

        public static readonly LocString[] Facts =
        [
            new("about.facts.1", "Honey never spoils. Jars over 3,000 years old have been found still edible."),
            new("about.facts.2", "Octopuses have three hearts and blue blood."),
            new("about.facts.3", "A day on Venus is longer than a whole year on Venus."),
            new("about.facts.4", "Bananas are berries, but strawberries aren't."),
            new("about.facts.5", "There are more possible chess games than atoms in the observable universe."),
            new("about.facts.6", "Sharks have been around longer than trees have."),
            new("about.facts.7", "A group of flamingos is called a flamboyance."),
            new("about.facts.8", "Honeybees can recognize individual human faces."),
            new("about.facts.9", "Wombat droppings are cube shaped."),
            new("about.facts.10", "The Eiffel Tower can grow over 15 cm taller on a hot day."),
            new("about.facts.11", "Hot water can sometimes freeze faster than cold water."),
            new("about.facts.12", "A bolt of lightning is roughly five times hotter than the surface of the Sun."),
        ];

        public static readonly LocString[] Quotes =
        [
            new("about.quotes.1", "Done is better than perfect. You can always polish later."),
            new("about.quotes.2", "Small steps every day add up to surprising distances."),
            new("about.quotes.3", "Comparison is the thief of joy. Run your own race."),
            new("about.quotes.4", "Progress, not perfection."),
            new("about.quotes.5", "You don't have to be great to start, but you have to start to be great."),
            new("about.quotes.6", "Be patient with yourself. Growth takes time."),
            new("about.quotes.7", "The best time to begin was yesterday. The second best is right now."),
            new("about.quotes.8", "Celebrate the small wins. They count too."),
            new("about.quotes.9", "Slow progress is still progress."),
            new("about.quotes.10", "Your only real competition is who you were yesterday."),
        ];

        public static readonly LocString[] Jokes =
        [
            new("about.jokes.1", "Why don't scientists trust atoms? Because they make up everything."),
            new("about.jokes.2", "I would tell you a chemistry joke, but I know I wouldn't get a reaction."),
            new("about.jokes.3", "Why did the scarecrow win an award? He was outstanding in his field."),
            new("about.jokes.4", "I'm reading a book about anti-gravity. It's impossible to put down."),
            new("about.jokes.5", "Why don't skeletons fight each other? They don't have the guts."),
            new("about.jokes.6", "What do you call fake spaghetti? An impasta."),
            new("about.jokes.7", "Why did the bicycle fall over? It was two tired."),
            new("about.jokes.8", "What do you call cheese that isn't yours? Nacho cheese."),
            new("about.jokes.9", "I'm on a seafood diet. I see food, and I eat it."),
            new("about.jokes.10", "I only know 25 letters of the alphabet. I don't know y."),
        ];
    }

    internal static class Settings
    {
        public static readonly LocString Title = new("settings.title", "Settings");
        public static readonly LocString Language = new("settings.language", "Language");
        public static readonly LocString LanguageHelp = new("settings.languageHelp", "The language of this plugin's windows. Zone and item names always follow the game client.");
        public static readonly LocString SearchHint = new("settings.searchHint", "Search...");

        public static readonly LocString CatGeneral = new("settings.cat.general", "General");
        public static readonly LocString CatGeneralSub = new("settings.cat.generalSub", "Window and behavior preferences.");

        public static readonly LocString GeneralWindow = new("settings.general.window", "Window");
        public static readonly LocString OpenOnLogin = new("settings.general.openOnLogin", "Open on login");
        public static readonly LocString OpenOnLoginHelp = new("settings.general.openOnLoginHelp", "Pop the main window automatically the next time you log in.");
        public static readonly LocString DragAnywhere = new("settings.general.dragAnywhere", "Drag from anywhere");
        public static readonly LocString DragAnywhereHelp = new("settings.general.dragAnywhereHelp", "Move the window by dragging anywhere inside it. When off, only the top bar moves it.");
        public static readonly LocString GeneralBehavior = new("settings.general.behavior", "Behavior");
        public static readonly LocString AutoPause = new("settings.general.autoPause", "Auto-pause in content");
        public static readonly LocString AutoPauseHelp = new("settings.general.autoPauseHelp", "Pause the run while you are inside a duty, trial, raid, or any other instanced content, then resume it once you are back outside. Your run and session stats are kept.");
        public static readonly LocString LootPrices = new("settings.general.lootPrices", "Estimate loot prices");
        public static readonly LocString LootPricesHelp = new("settings.general.lootPricesHelp", "Price the rare loot on the stat cards and in the tooltips from Universalis. While a run is going or the stats are on screen, the plugin sends Universalis your home data centre's name and the rare loot's item ids, at most every half hour. Off by default.");
        public static readonly LocString Tankbuster = new("settings.general.tankbuster", "Mitigate tankbusters");
        public static readonly LocString TankbusterHelp = new("settings.general.tankbusterHelp", "While a run is on and you play a tank, this plugin takes the personal mitigations over from the combat plugin, which then uses none by your HP. An enemy cast of 1.5 s or more aimed at you gets your job's short mitigation (Bloodwhetting, Holy Sheltron, The Blackest Night or Heart of Corundum) in the last 6 seconds of the cast. A treasure dungeon boss's tankbuster gets the short mitigation and one bigger cooldown, strongest first (Damnation before Rampart), as soon as their cooldowns allow. By your HP, the short mitigation is used under 80%, or under 50% while a boss is casting tankbusters, so it is ready for them. A bigger cooldown is used outside a tankbuster only when three or more enemies are hitting you and your HP is under 40%. Off by default.");
        public static readonly LocString AutoFollowLeader = new("settings.general.autoFollowLeader", "Start automatically as a party member");
        public static readonly LocString AutoFollowLeaderHelp = new("settings.general.autoFollowLeaderHelp", "Presses Start for you on a character that is in a party on your own world but does not lead it, so it runs as the leader's member (going to each dig spot the leader posts) without you starting it by hand. It starts as you join the party, and whenever the leader posts its next dig spot or a world to buy on while this plugin is not running. A Stop holds until the leader's next post. Off by default.");

        public static readonly LocString PostRunGroup = new("settings.postRun.group", "Chat commands");
        public static readonly LocString PostRunLabel = new("settings.postRun.label", "Run when the run finishes");
        public static readonly LocString PostRunHelp = new("settings.postRun.help", "One command per line, each starting with a slash. They run when a run ends on its own, before the after-run action, and never after Stop or a failure.");
        public static readonly LocString PostRunCaption = new("settings.postRun.caption", "Lines that do not start with a slash are skipped, so nothing is ever said in chat. Try /li home to head home or /ays m to hand off to AutoRetainer.");
    }

    internal static class Plugin
    {
        public static readonly LocString CommandHelp = new("plugin.commandHelp", "Toggle the TreasureHelper window.\n/atm start - run your maps until a stop condition: decipher, travel, dig, fight and loot\n/atm stop - stop the run\n/atm stopafter - stop once the map under way is done, or take that back\n/atm pause - pause or resume the run\n/atm dig - run the held map: travel, dig, fight the guards and loot the coffer\n/atm decipher [map] - decipher the next timeworn map in your bags, or the one named\n/atm buy [map] - buy the next map under its price cap on the market board, or the one named\n/atm spot - show the held map's dig spot\n/atm flag - flag the dig spot on the map\n/atm goto spot - travel to the dig spot\n/atm goto <territory> <x> <y> <z> - travel to a point (debug helper)\n/atm goto stop - stop that travel\n/atm tiers - list every map tier in the console\n/atm target - dump the current target's BaseId\n/atm config - open the settings\n/atm stats - open the history\n/atm log - open the console\n/atm deps - open the plugins page\n/atm changelog - open the changelog\n/atm about - open the about page");
        public static readonly LocString CommandHelpAlias = new("plugin.commandHelpAlias", "Alias for /atm.");
    }
}
