using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;

namespace AutoTreasureMaps.Core.Game;

// A dig spot the party leader posted in party chat: the zone and the point on the ground, in world units.
internal readonly record struct PostedSpot(uint TerritoryId, uint MapId, Vector2 At, long ReceivedAtMs, string Sender);

// In party mode the leader flags each dig spot and posts the flag in party chat as it sets off; every other member reads
// the link and travels to the spot on its own, so all of them leave together. Only a map link the party leader posted
// in the party channel counts. Read and written only on the framework thread.
internal sealed unsafe class PartySpotLink : IDisposable
{
    private const string PostCommand = "/p <flag>";

    public PartySpotLink()
    {
        Svc.Chat.ChatMessage += OnChatMessage;
    }

    public static PostedSpot? Latest { get; private set; }

    public static void Forget() => Latest = null;

    public void Dispose() => Svc.Chat.ChatMessage -= OnChatMessage;

    // Flags the spot without opening the map, and posts the flag.
    public static bool Post(DigSpot spot)
    {
        var agent = AgentMap.Instance();
        if (agent is null)
        {
            return false;
        }

        agent->SetFlagMapMarker(spot.TerritoryId, spot.MapId, spot.Position);
        Chat.SendMessage(PostCommand);
        return true;
    }

    private static void OnChatMessage(IHandleableChatMessage message)
    {
        if (message.LogKind != XivChatType.Party || PartyPresence.Leader() is not { } leader || PartyPresence.IsLocal(leader))
        {
            return;
        }

        var sender = message.Sender.TextValue;
        var leaderName = leader.Name.TextValue;
        if (leaderName.Length == 0 || !sender.Contains(leaderName, StringComparison.Ordinal))
        {
            return;
        }

        if (PartyHop.TryNote(message.Message.TextValue, leaderName))
        {
            RunLog.Info($"Party: {leaderName} is taking the party to {PartyHop.Pending?.WorldName} to buy maps.");
            return;
        }

        foreach (var payload in message.Message.Payloads)
        {
            if (payload is MapLinkPayload link)
            {
                Latest = new PostedSpot(link.TerritoryType.RowId, link.Map.RowId, new Vector2(link.RawX / 1000f, link.RawY / 1000f), Environment.TickCount64, leaderName);
                RunLog.Info($"Party: {leaderName} posted a spot in territory {link.TerritoryType.RowId} at ({link.RawX / 1000f:F1}, {link.RawY / 1000f:F1}).");
                return;
            }
        }
    }
}
