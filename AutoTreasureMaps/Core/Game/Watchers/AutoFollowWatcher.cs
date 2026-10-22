using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoTreasureMaps.Core.Game.Watchers;

// With the setting on, a party member that is not running starts following its leader by itself: when it joins a party
// it does not lead, and whenever the leader posts a dig spot or a world to buy on. So a member stopped by hand, or whose
// following ended, is back with the leader at the next spot, while a Stop still holds until then.
internal sealed class AutoFollowWatcher : IDisposable
{
    private bool wasMember;
    private long seenSpotAtMs;
    private long seenHopAtMs;

    public AutoFollowWatcher()
    {
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose() => Svc.Framework.Update -= OnUpdate;

    private void OnUpdate(IFramework framework)
    {
        var member = Svc.ClientState.IsLoggedIn && RunReadiness.FollowsLeader();
        var joined = member && !wasMember;
        wasMember = member;

        var spotAt = PartySpotLink.Latest?.ReceivedAtMs ?? 0;
        var hopAt = PartyHop.Pending?.ReceivedAtMs ?? 0;
        var posted = spotAt > seenSpotAtMs || hopAt > seenHopAtMs;
        seenSpotAtMs = Math.Max(seenSpotAtMs, spotAt);
        seenHopAtMs = Math.Max(seenHopAtMs, hopAt);

        var plugin = Plugin.Instance;
        if (!member || !(joined || posted) || !plugin.Configuration.AutoFollowLeader || plugin.Controller.Running)
        {
            return;
        }

        if (!RunReadiness.Check(plugin).CanStart)
        {
            return;
        }

        RunLog.Info($"Auto follow: {(joined ? "joined a party it does not lead" : "the leader posted where to go")}; starting to follow.");
        plugin.Controller.Start();
    }
}
