using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Party;
using ECommons.DalamudServices;
using System.Numerics;

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

    // A party mode needs the local list, where every member's zone and position can be read; a cross-world party has
    // none of that.
    public static bool LocalParty() => Svc.Party.Length > 1;

    public static IPartyMember? Leader()
    {
        var party = Svc.Party;
        var index = (int)party.PartyLeaderIndex;
        return party.Length > 1 && index >= 0 && index < party.Length ? party[index] : null;
    }

    public static bool IsLocal(IPartyMember member)
        => Svc.Objects.LocalPlayer is { } player && member.EntityId == player.EntityId;

    public static bool LeadsTheParty() => Leader() is { } leader && IsLocal(leader);

    // The farthest other member from the point, in the zone the character stands in; a member in another zone counts as
    // infinitely far. Zero for a character alone.
    public static float FarthestMemberFrom(Vector3 point)
    {
        var party = Svc.Party;
        var territoryId = Svc.ClientState.TerritoryType;
        var farthest = 0f;
        for (var index = 0; index < party.Length; index++)
        {
            if (party[index] is not { } member || IsLocal(member))
            {
                continue;
            }

            farthest = member.Territory.RowId != territoryId
                ? float.PositiveInfinity
                : Math.Max(farthest, Vector3.Distance(member.Position, point));
        }

        return farthest;
    }
}
