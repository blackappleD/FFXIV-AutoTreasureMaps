namespace AutoTreasureMaps.Core.Localization;

internal static partial class L
{
    internal static class MapRun
    {
        public static readonly LocString Run = new("mapRun.run", "Run this map");
        public static readonly LocString RunHint = new("mapRun.runHint", "Travel to the dig spot, dig up the coffer, defeat its guards and loot it.");

        public static readonly LocString PhaseDigging = new("mapRun.phase.digging", "Digging");
        public static readonly LocString PhaseOpening = new("mapRun.phase.opening", "Opening the coffer");
        public static readonly LocString PhaseLooting = new("mapRun.phase.looting", "Looting");

        public static readonly LocString FactStep = new("mapRun.fact.step", "Step");
        public static readonly LocString FactHuntTime = new("mapRun.fact.huntTime", "Hunt time");
        public static readonly LocString HuntTimeLeft = new("mapRun.huntTimeLeft", "{0} left");
        public static readonly LocString HuntTimeUnknown = new("mapRun.huntTimeUnknown", "Not read yet");
        public static readonly LocString Wave = new("mapRun.wave", "Wave {0}");
        public static readonly LocPlural GuardsLeft = new("mapRun.guardsLeft", "{0} guard left", "{0} guards left");

        public static readonly LocString LastMap = new("mapRun.lastMap", "Last map");
        public static readonly LocString LastMapNone = new("mapRun.lastMapNone", "None yet");
        public static readonly LocString Gil = new("mapRun.gil", "{0} gil");
        public static readonly LocPlural Items = new("mapRun.items", "{0} item", "{0} items");
        public static readonly LocPlural Tomestones = new("mapRun.tomestones", "{0} tomestone", "{0} tomestones");
        public static readonly LocString PortalSeen = new("mapRun.portalSeen", "Portal seen");

        public static readonly LocString OutcomeLooted = new("mapRun.outcome.looted", "Looted");
        public static readonly LocString OutcomeTimedOut = new("mapRun.outcome.timedOut", "Timed out");
        public static readonly LocString OutcomeDied = new("mapRun.outcome.died", "Knocked out");
        public static readonly LocString OutcomeDigFailed = new("mapRun.outcome.digFailed", "Nothing dug up");
        public static readonly LocString OutcomeUnreachable = new("mapRun.outcome.unreachable", "Could not reach it");
        public static readonly LocString OutcomeRefused = new("mapRun.outcome.refused", "Refused");
        public static readonly LocString OutcomeCancelled = new("mapRun.outcome.cancelled", "Stopped");
        public static readonly LocString OutcomeFaulted = new("mapRun.outcome.faulted", "Stopped on an error");

        public static readonly LocString RefusedUnresolved = new("mapRun.refused.unresolved", "The held map's dig spot could not be read. /atm spot writes the details to the console.");
        public static readonly LocString RefusedPortalMap = new("mapRun.refused.portalMap", "The held map is a thief's map that leads to a portal. Portals are not handled yet, so dig it up by hand.");
        public static readonly LocString RefusedUndersea = new("mapRun.refused.undersea", "This map's dig spot lies under water, and the plugin cannot dive yet.");
        public static readonly LocString RefusedNotUnlocked = new("mapRun.refused.notUnlocked", "Dig is not unlocked on this character yet.");
        public static readonly LocString RefusedInDuty = new("mapRun.refused.inDuty", "Maps cannot be dug up while you are bound by a duty.");
        public static readonly LocString RefusedPartyNotInArea = new("mapRun.refused.partyNotInArea", "Every party member must be in the area before the game allows a dig.");
        public static readonly LocString RefusedBoundByDuty = new("mapRun.refused.boundByDuty", "The game refused the dig because you are bound by another duty.");
        public static readonly LocString RefusedPartyBoundByDuty = new("mapRun.refused.partyBoundByDuty", "The game refused the dig because a party member is bound by another duty.");
        public static readonly LocString RefusedPortalDug = new("mapRun.refused.portalDug", "The dig turned up a teleportation portal. Turn on Dungeons > Enter the portal to have the run take it solo, or enter it by hand.");
        public static readonly LocString RefusedCofferPrompt = new("mapRun.refused.cofferPrompt", "The coffer's confirmation did not read as expected, so it was declined.");
        public static readonly LocString RefusedNotCombatClass = new("mapRun.refused.notCombatClass", "Switch to a combat job first: a crafter or gatherer cannot fight off the coffer's guards.");

        public static readonly LocString ChatStarted = new("mapRun.chat.started", "Running {0} in {1}.");
        public static readonly LocString ChatRefused = new("mapRun.chat.refused", "Cannot run the map. {0}");
        public static readonly LocPlural ChatLooted = new("mapRun.chat.looted", "{1}: coffer looted for {2} gil and {0} item.", "{1}: coffer looted for {2} gil and {0} items.");
        public static readonly LocString ChatEnded = new("mapRun.chat.ended", "{0}: {1}.");
        public static readonly LocString ChatEndedReason = new("mapRun.chat.endedReason", "{0}: {1}. {2}");
        public static readonly LocString ChatPortal = new("mapRun.chat.portal", "A portal appeared. It is left alone, so enter it by hand if you want the dungeon.");

        public static readonly LocString Loot = new("mapRun.loot", "Loot");
        public static readonly LocString Roll = new("mapRun.roll", "Need or Greed");
        public static readonly LocString RollHelp = new("mapRun.rollHelp", "How the run answers the game's Need or Greed window for the coffer's items that are rolled for.");
        public static readonly LocString RollNeedName = new("mapRun.roll.need.name", "Need");
        public static readonly LocString RollNeedDetail = new("mapRun.roll.need.detail", "Roll Need on every item that allows it, and Greed where Need is not allowed.");
        public static readonly LocString RollGreedName = new("mapRun.roll.greed.name", "Greed");
        public static readonly LocString RollGreedDetail = new("mapRun.roll.greed.detail", "Roll Greed on every item.");
        public static readonly LocString RollPassName = new("mapRun.roll.pass.name", "Pass");
        public static readonly LocString RollPassDetail = new("mapRun.roll.pass.detail", "Pass on every item, so the rest of the party can have it.");
        public static readonly LocString RollManualName = new("mapRun.roll.manual.name", "Leave it to me");
        public static readonly LocString RollManualDetail = new("mapRun.roll.manual.detail", "Do not roll. The window stays open for you to answer.");
    }
}
