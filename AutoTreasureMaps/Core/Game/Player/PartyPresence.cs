using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;

namespace AutoTreasureMaps.Core.Game.Player;

internal static class PartyPresence
{
    // The local party list misses a cross-world party (a Party Finder group in the overworld), so the condition is read too.
    public static bool InParty()
        => Svc.Party.Length > 1 || Svc.Condition[ConditionFlag.ParticipatingInCrossWorldPartyOrAlliance];

    // The local party list only: a cross-world party is not in it and reads as present, so a caller still retries on a
    // timer of its own.
    public static bool AllInZone()
    {
        var party = Svc.Party;
        var territoryId = Svc.ClientState.TerritoryType;
        for (var index = 0; index < party.Length; index++)
        {
            if (party[index] is { } member && member.Territory.RowId != territoryId)
            {
                return false;
            }
        }

        return true;
    }
}
