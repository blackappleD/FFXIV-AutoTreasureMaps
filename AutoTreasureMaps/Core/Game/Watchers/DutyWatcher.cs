using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Tasks;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoTreasureMaps.Core.Game.Watchers;

internal sealed class DutyWatcher : IDisposable
{
    private const int ResumeSettleMs = 5_000;

    private long leftContentAtMs;
    private HuntDuty loggedHuntDuty;

    private enum HuntDuty : byte { None, MapStep, Director, Dungeon }

    public DutyWatcher()
    {
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
    }

    private void OnUpdate(IFramework _)
    {
        var controller = Plugin.Instance.Controller;

        if (InContent())
        {
            leftContentAtMs = 0;
            if (Plugin.Instance.Configuration.AutoPauseInContent && controller.SessionSnapshot is not null && !HuntHoldsTheDuty(controller))
            {
                controller.Pause(PauseReason.InContent);
            }

            return;
        }

        loggedHuntDuty = HuntDuty.None;
        if (controller.PauseReason != PauseReason.InContent) return;

        if (Svc.Objects.LocalPlayer is null
         || Svc.Condition[ConditionFlag.BetweenAreas]
         || Svc.Condition[ConditionFlag.BetweenAreas51])
        {
            leftContentAtMs = 0;
            return;
        }

        if (leftContentAtMs == 0)
        {
            leftContentAtMs = Environment.TickCount64;
            return;
        }
        if (Environment.TickCount64 - leftContentAtMs < ResumeSettleMs) return;

        leftContentAtMs = 0;
        controller.Resume();
    }

    // The game may count a dug-up coffer's hunt as a duty. That is the run's own work, not content to wait out, so the
    // pause is skipped while the map run is at the coffer in the map's zone or the hunt's director runs; a duty that
    // takes the character to another zone still pauses. A treasure dungeon the run knows is its own work too. Which of
    // them held it is logged once per stretch, so a test run shows whether the flag is set at all.
    private bool HuntHoldsTheDuty(TreasureController controller)
    {
        var progress = controller.Progress;
        var inMapZone = progress.HasMap && Svc.ClientState.TerritoryType == progress.Spot.TerritoryId;
        var reason = DungeonRegistry.IsDungeon(Svc.ClientState.TerritoryType)
            ? HuntDuty.Dungeon
            : inMapZone && controller.Phase is TreasurePhase.Digging or TreasurePhase.Opening or TreasurePhase.Fighting or TreasurePhase.Looting
                ? HuntDuty.MapStep
                : TreasureHuntDirector.IsActive() ? HuntDuty.Director : HuntDuty.None;
        if (reason != HuntDuty.None && reason != loggedHuntDuty)
        {
            RunLog.Info(reason switch
            {
                HuntDuty.MapStep => $"Duty: bound by duty during the map run's {controller.Phase} step; not pausing.",
                HuntDuty.Dungeon => "Duty: bound by duty in a treasure dungeon the run clears; not pausing.",
                _ => "Duty: bound by duty while the treasure hunt director runs; not pausing.",
            });
        }

        loggedHuntDuty = reason;
        return reason != HuntDuty.None;
    }

    internal static bool InContent()
        => Svc.Condition[ConditionFlag.BoundByDuty]
        || Svc.Condition[ConditionFlag.BoundByDuty56]
        || Svc.Condition[ConditionFlag.BoundByDuty95];
}
