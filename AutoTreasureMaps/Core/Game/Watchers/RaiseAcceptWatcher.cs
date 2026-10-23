using AutoTreasureMaps.Core.Game.Ops;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Throttlers;

namespace AutoTreasureMaps.Core.Game.Watchers;

// With the setting on, a raise cast on the knocked-out character is taken at once while a run is on, wherever the run
// is waiting: beside the coffer, in a dungeon room or on the way. A paused run, such as one held in a duty the player
// runs by hand, leaves it to the player.
internal sealed class RaiseAcceptWatcher : IDisposable
{
    private const string ThrottleKey = "atm-raise-accept";
    private const int ThrottleMs = 1_000;

    public RaiseAcceptWatcher()
    {
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose() => Svc.Framework.Update -= OnUpdate;

    private static void OnUpdate(IFramework framework)
    {
        var plugin = Plugin.Instance;
        if (!plugin.Configuration.AutoAcceptRaise || !plugin.Controller.Running || plugin.Controller.Paused
            || !Svc.Condition[ConditionFlag.Unconscious] || Svc.Objects.LocalPlayer is not { } player
            || !RaiseOps.RaisePending(player) || !EzThrottler.Throttle(ThrottleKey, ThrottleMs))
        {
            return;
        }

        RunLog.Info("Revive: a raise is on offer; accepting it.");
        if (!RaiseOps.AcceptRaise())
        {
            RunLog.Info("Revive: the game did not take the accept; trying again shortly.");
        }
    }
}
