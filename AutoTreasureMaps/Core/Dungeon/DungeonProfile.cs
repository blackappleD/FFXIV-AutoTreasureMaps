using System.Numerics;

namespace AutoTreasureMaps.Core.Dungeon;

// Gate dungeons are cleared chamber by chamber through one of two (or three) gates; slot dungeons stay in one room and
// draw each round from a slot or roulette machine.
internal enum DungeonKind : byte { Gate, Slot }

// The circle a gate opens onto, by the gate's event object.
internal readonly record struct GateCircle(uint GateBaseId, Vector3 At);

// Everything one treasure dungeon differs in, as data. The ids come from the game's sheets; Verified is set once a run
// in game has confirmed them, and until then the run says so before it starts.
//
// Rounds is the number of chambers (gate) or draws (slot) a full clear goes through. Coffers are the event objects
// the chamber's treasure stands in; a coffer of the Treasure kind is found without a listed id. Advance names what
// moves the run on (the gates, the slot machine). Transit names the circle a gate dungeon walks into after a gate
// opens, and may be empty while it is not known yet. GateCircles places the circle behind each gate, read from the
// dungeon's layout, so the run walks straight to it instead of looking for it. Exit names the exit point the last
// chamber is left through; the duty list is the fallback where a dungeon has none. MapItemId is the timeworn map whose
// portal usually leads here, which the stats credit a dungeon to when the run did not see the portal itself.
// BonusNameIds are the BNpcName rows of the rare enemies that drop extra loot, and FleeingNameIds the ones among them
// that run off if not killed in time, so they are killed before the ones that stay;
// MandragoraNameIds are the mandragora party's, in the order their markers ask them to be killed. FirstNameIds are the
// adds the combat plugin's module ranks above the boss they come with, killed before the rest of the room.
// GoldSackBaseIds and SilverSackBaseIds are the event objects a bonus round scatters for the character to walk onto, a
// gold one worth three silver; both are empty where the run does not know them, and such a bonus round is left to the
// player. SackChargerBaseIds are the BNpcBase rows of the enemies that charge across the room during the round, each
// along a lane the run keeps out of while the charge is due.
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
    GateCircle[] GateCircles,
    uint[] ExitBaseIds,
    uint[] BonusNameIds,
    uint[] FleeingNameIds,
    uint[] MandragoraNameIds,
    uint[] FirstNameIds,
    uint[] GoldSackBaseIds,
    uint[] SilverSackBaseIds,
    uint[] SackChargerBaseIds,
    bool Verified)
{
    public bool IsCoffer(uint baseId) => Array.IndexOf(CofferBaseIds, baseId) >= 0;

    public bool IsAdvance(uint baseId) => Array.IndexOf(AdvanceBaseIds, baseId) >= 0;

    public bool IsTransit(uint baseId) => Array.IndexOf(TransitBaseIds, baseId) >= 0;

    // The circle behind the gate, or null where the profile does not place it.
    public Vector3? CircleBehind(uint gateBaseId)
    {
        for (var index = 0; index < GateCircles.Length; index++)
        {
            if (GateCircles[index].GateBaseId == gateBaseId)
            {
                return GateCircles[index].At;
            }
        }

        return null;
    }

    public bool IsExit(uint baseId) => Array.IndexOf(ExitBaseIds, baseId) >= 0;

    public bool IsBonus(uint nameId) => Array.IndexOf(BonusNameIds, nameId) >= 0;

    public bool IsFleeing(uint nameId) => Array.IndexOf(FleeingNameIds, nameId) >= 0;

    public bool IsFirst(uint nameId) => Array.IndexOf(FirstNameIds, nameId) >= 0;

    public bool HasSacks => GoldSackBaseIds.Length > 0 || SilverSackBaseIds.Length > 0;

    public bool IsGoldSack(uint baseId) => Array.IndexOf(GoldSackBaseIds, baseId) >= 0;

    public bool IsSilverSack(uint baseId) => Array.IndexOf(SilverSackBaseIds, baseId) >= 0;

    public bool IsSackCharger(uint baseId) => Array.IndexOf(SackChargerBaseIds, baseId) >= 0;

    // The mandragora's place in the kill order, or -1 for any other enemy.
    public int MandragoraOrder(uint nameId) => Array.IndexOf(MandragoraNameIds, nameId);
}
