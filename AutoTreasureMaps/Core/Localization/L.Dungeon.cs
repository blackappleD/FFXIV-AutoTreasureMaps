namespace AutoTreasureMaps.Core.Localization;

internal static partial class L
{
    internal static class Dungeon
    {
        public static readonly LocString CommandHelp = new("dungeon.commandHelp", "/atm dungeon clears the treasure dungeon you stand in (solo only), /atm dungeon record starts writing what the dungeon shows to a file and /atm dungeon record stop ends it, and /atm dungeon nearby lists the objects around you in the console.");

        public static readonly LocString ChatStarted = new("dungeon.chat.started", "Clearing {0}.");
        public static readonly LocString ChatUnverified = new("dungeon.chat.unverified", "This dungeon has not been checked in game yet. Stay close, and if it goes wrong, /atm dungeon record while you clear it by hand helps fix it.");
        public static readonly LocString ChatEnded = new("dungeon.chat.ended", "{0}: {1} in round {2}, {3} gil and {4} items.");
        public static readonly LocString ChatNotInDungeon = new("dungeon.chat.notInDungeon", "You are not in a treasure dungeon the plugin knows. It knows Cenote Ja Ja Gural and Vault Oneiron so far.");
        public static readonly LocString ChatInParty = new("dungeon.chat.inParty", "Treasure dungeons are only cleared solo for now. Leave the party first.");
        public static readonly LocString ChatStuck = new("dungeon.chat.stuck", "The dungeon shows nothing the plugin knows how to act on, so the rest is up to you.");
        public static readonly LocString ChatLure = new("dungeon.chat.lure", "The Gambler's Lure is up. Make your choice; the run goes on once its window closes.");
        public static readonly LocString ChatBonusRound = new("dungeon.chat.bonusRound", "A bonus round began. Pick up the glowing sacks yourself; the run goes on once it ends.");
        public static readonly LocString ChatEnteringPortal = new("dungeon.chat.enteringPortal", "A portal appeared. Entering it to clear the treasure dungeon.");
        public static readonly LocString ChatPortalInParty = new("dungeon.chat.portalInParty", "A portal appeared, but you are in a party, so the dungeon is left to you.");
        public static readonly LocString ChatUnknownDungeon = new("dungeon.chat.unknownDungeon", "This treasure dungeon is not one the plugin can clear yet, so it is up to you. The run carries on once you are back outside.");

        public static readonly LocString ChatRecordStarted = new("dungeon.chat.recordStarted", "Recording the dungeon to {0}. Clear it by hand, then /atm dungeon record stop.");
        public static readonly LocString ChatRecording = new("dungeon.chat.recording", "Already recording to {0}.");
        public static readonly LocString ChatRecordStopped = new("dungeon.chat.recordStopped", "Recording saved to {0}.");
        public static readonly LocString ChatNotRecording = new("dungeon.chat.notRecording", "Nothing is being recorded.");

        public static readonly LocString OutcomeCleared = new("dungeon.outcome.cleared", "Cleared");
        public static readonly LocString OutcomeExpelled = new("dungeon.outcome.expelled", "Sent out");
        public static readonly LocString OutcomeDied = new("dungeon.outcome.died", "Knocked out");
        public static readonly LocString OutcomeLeft = new("dungeon.outcome.left", "Left the dungeon");
        public static readonly LocString OutcomeStuck = new("dungeon.outcome.stuck", "Handed back to you");
        public static readonly LocString OutcomeInParty = new("dungeon.outcome.inParty", "Not solo");

        public static readonly LocString Cat = new("dungeon.cat", "Dungeons");
        public static readonly LocString CatSub = new("dungeon.catSub", "Gates and the Gambler's Lure inside treasure dungeons");
        public static readonly LocString GroupPortal = new("dungeon.group.portal", "Portal");
        public static readonly LocString EnterPortal = new("dungeon.enterPortal", "Enter the portal");
        public static readonly LocString EnterPortalHelp = new("dungeon.enterPortalHelp", "When a coffer leaves a portal behind, or a dig turns one up, walk into it and clear the treasure dungeon, then carry on with the next map. Solo only; in a party the portal is left to you.");
        public static readonly LocString GroupGates = new("dungeon.group.gates", "Gates");
        public static readonly LocString GatePolicy = new("dungeon.gatePolicy", "Gate to try");
        public static readonly LocString GatePolicyHelp = new("dungeon.gatePolicyHelp", "Which gate the run opens in a dungeon that makes you choose one. It applies to every gate dungeon unless one is set on its own below.");
        public static readonly LocString GateRandomName = new("dungeon.gate.random.name", "Random");
        public static readonly LocString GateRandomDetail = new("dungeon.gate.random.detail", "Pick one of the gates at random each time.");
        public static readonly LocString GateLeftName = new("dungeon.gate.left.name", "Left");
        public static readonly LocString GateLeftDetail = new("dungeon.gate.left.detail", "Always the leftmost gate, as seen from the chamber.");
        public static readonly LocString GateRightName = new("dungeon.gate.right.name", "Right");
        public static readonly LocString GateRightDetail = new("dungeon.gate.right.detail", "Always the rightmost gate, as seen from the chamber.");
        public static readonly LocString GateDefaultName = new("dungeon.gate.default.name", "As above");
        public static readonly LocString GateDefaultDetail = new("dungeon.gate.default.detail", "Follow the gate to try set above.");
        public static readonly LocString FollowHint = new("dungeon.followHint", "Follow a glowing gate");
        public static readonly LocString FollowHintHelp = new("dungeon.followHintHelp", "When the game lights one of the gates, open that one whatever the setting above says.");
        public static readonly LocString PerDungeonHelp = new("dungeon.perDungeonHelp", "The gate to try in this dungeon only.");
        public static readonly LocString GroupLure = new("dungeon.group.lure", "Gambler's Lure");
        public static readonly LocString LurePolicy = new("dungeon.lurePolicy", "When the lure is set");
        public static readonly LocString LurePolicyHelp = new("dungeon.lurePolicyHelp", "A coffer can come with the Gambler's Lure, a high-or-low game that can multiply its treasure or lose all of it.");
        public static readonly LocString LureAskName = new("dungeon.lure.ask.name", "Let me choose");
        public static readonly LocString LureAskDetail = new("dungeon.lure.ask.detail", "The run waits while you play it or open the coffer yourself.");
        public static readonly LocString LureOpenName = new("dungeon.lure.open.name", "Open the coffer");
        public static readonly LocString LureOpenDetail = new("dungeon.lure.open.detail", "Take the treasure as it is and skip the game.");
    }
}
