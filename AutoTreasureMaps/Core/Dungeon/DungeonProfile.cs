namespace AutoTreasureMaps.Core.Dungeon;

// Gate dungeons are cleared chamber by chamber through one of two (or three) gates; slot dungeons stay in one room and
// draw each round from a slot or roulette machine.
internal enum DungeonKind : byte { Gate, Slot }

// Everything one treasure dungeon differs in, as data. The ids come from the game's sheets; Verified is set once a run
// in game has confirmed them, and until then the run says so before it starts.
//
// Rounds is the number of chambers (gate) or draws (slot) a full clear goes through. Coffers are the event objects
// the chamber's treasure stands in; a coffer of the Treasure kind is found without a listed id. Advance names what
// moves the run on (the gates, the slot machine). Transit names the circle a gate dungeon walks into after a gate
// opens, and may be empty while it is not known yet. Exit names an exit point to use when the game will not let the
// duty be left from the duty list. MapItemId is the timeworn map whose portal usually leads here, which the stats
// credit a dungeon to when the run did not see the portal itself.
internal sealed record DungeonProfile(
    string Key,
    uint MapItemId,
    uint TerritoryId,
    uint ContentFinderConditionId,
    DungeonKind Kind,
    byte Rounds,
    uint[] CofferBaseIds,
    uint[] AdvanceBaseIds,
    uint[] TransitBaseIds,
    uint[] ExitBaseIds,
    bool Verified)
{
    public bool IsCoffer(uint baseId) => Array.IndexOf(CofferBaseIds, baseId) >= 0;

    public bool IsAdvance(uint baseId) => Array.IndexOf(AdvanceBaseIds, baseId) >= 0;

    public bool IsTransit(uint baseId) => Array.IndexOf(TransitBaseIds, baseId) >= 0;

    public bool IsExit(uint baseId) => Array.IndexOf(ExitBaseIds, baseId) >= 0;
}
