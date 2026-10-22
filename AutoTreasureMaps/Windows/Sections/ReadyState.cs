using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Tasks;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Sections;

internal static class ReadyState
{
    public enum Kind { SetupNeeded, NoMaps, Blocked, Ready, Running, Paused }

    // Check is what the readiness check found while no run goes, and default while one does.
    public readonly record struct Info(Kind Kind, Vector4 Accent, Vector4 AccentSoft, FontAwesomeIcon Icon, string Title, string Detail, RunCheck Check);

    private static int cachedFrame = -1;
    private static Info cached;
    private static CachedText readyDetail;

    public static Info Resolve(TreasureController controller)
    {
        var frame = ImGui.GetFrameCount();
        if (frame == cachedFrame)
        {
            return cached;
        }

        cached = Compute(controller);
        cachedFrame = frame;
        return cached;
    }

    private static Info Compute(TreasureController controller)
    {
        if (controller.Running)
        {
            if (controller.Paused)
            {
                var detail = controller.PauseReason == PauseReason.InContent
                    ? Loc.T(L.Treasure.DetailPausedInContent)
                    : Loc.T(L.Treasure.DetailPausedManual);
                return new Info(Kind.Paused, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Pause, Loc.T(L.Treasure.TitlePaused), detail, default);
            }

            return new Info(Kind.Running, Styling.AccentBlue, Styling.AccentBlueSoft, FontAwesomeIcon.Map, Loc.T(L.Treasure.TitleRunning), PhaseLabel(controller.Phase), default);
        }

        var check = RunReadiness.Check(Plugin.Instance);
        return check.Blocker switch
        {
            RunBlocker.None when check.Follows => new Info(Kind.Ready, Styling.AccentMint, Styling.AccentMintSoft, FontAwesomeIcon.Play,
                Loc.T(L.Run.PhaseReady), Loc.T(L.Treasure.DetailFollow), check),
            RunBlocker.None => new Info(Kind.Ready, Styling.AccentMint, Styling.AccentMintSoft, FontAwesomeIcon.Play,
                Loc.T(L.Run.PhaseReady), readyDetail.Get(check.Available, static key => Loc.Plural(L.Treasure.DetailReady, (int)key)), check),
            RunBlocker.NeedsPlugins => new Info(Kind.SetupNeeded, Styling.AccentRose, Styling.AccentRoseSoft, FontAwesomeIcon.ExclamationTriangle,
                Loc.T(L.Treasure.TitleSetupNeeded), Loc.T(L.Treasure.DetailSetupNeeded), check),
            RunBlocker.NoMaps => new Info(Kind.NoMaps, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Map,
                Loc.T(L.Treasure.TitleSetupNeeded), Loc.T(check.Reason), check),
            _ => new Info(Kind.Blocked, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Ban,
                Loc.T(L.Treasure.TitleBlocked), Loc.T(check.Reason), check),
        };
    }

    public static string ShortLabel(Kind kind) => kind switch
    {
        Kind.Running                    => Loc.T(L.Shell.StatusRunning),
        Kind.Paused                     => Loc.T(L.Shell.StatusPaused),
        Kind.SetupNeeded or Kind.NoMaps => Loc.T(L.Shell.StatusSetupNeeded),
        Kind.Ready                      => Loc.T(L.Run.PhaseReady),
        _                               => Loc.T(L.Shell.StatusIdle),
    };

    public static string PhaseLabel(TreasurePhase phase) => phase switch
    {
        TreasurePhase.Preparing   => Loc.T(L.Run.PhasePreparing),
        TreasurePhase.Deciphering => Loc.T(L.Run.PhaseDeciphering),
        TreasurePhase.Buying      => Loc.T(L.Run.PhaseBuying),
        TreasurePhase.Travelling  => Loc.T(L.Run.PhaseTravelling),
        TreasurePhase.Digging     => Loc.T(L.MapRun.PhaseDigging),
        TreasurePhase.Opening     => Loc.T(L.MapRun.PhaseOpening),
        TreasurePhase.Fighting    => Loc.T(L.Run.PhaseFighting),
        TreasurePhase.Looting     => Loc.T(L.MapRun.PhaseLooting),
        TreasurePhase.Upkeep      => Loc.T(L.Progress.PhaseUpkeep),
        TreasurePhase.Finishing   => Loc.T(L.Run.PhaseFinishing),
        TreasurePhase.Gathering   => Loc.T(L.Gather.PhaseGathering),
        _                         => Loc.T(L.Run.PhaseStandingBy),
    };
}
