using clib.Services;

namespace AutoTreasureMaps.Core.Tasks;

internal enum PauseReason { None, Manual, InContent }

internal sealed partial class TreasureController
{
    public PauseReason PauseReason { get; private set; } = PauseReason.None;

    public bool Paused => PauseReason != PauseReason.None;

    public bool CanPause => session is not null && Phase is not (TreasurePhase.Idle or TreasurePhase.Paused or TreasurePhase.Finishing);

    public void Pause(PauseReason reason)
    {
        if (reason == PauseReason.None)
        {
            return;
        }

        if (Paused)
        {
            if (reason == PauseReason.Manual && PauseReason == PauseReason.InContent)
            {
                PauseReason = PauseReason.Manual;
                Diag("Auto-pause promoted to a manual pause; leaving content will no longer resume.");
            }

            return;
        }

        if (!CanPause)
        {
            if (reason == PauseReason.Manual)
            {
                ECommons.DalamudServices.Svc.Chat.Print($"{AtmConstants.LogPrefix} Nothing to pause.");
            }

            return;
        }

        var pausing = session!;
        PauseReason = reason;
        if (pausing.ActiveMap is { CofferOpened: true } openedMap)
        {
            openedMap.CleanupBlocked = true;
        }

        if (pausing.ActiveDungeon is { } dungeon)
        {
            dungeon.CleanupBlocked = true;
        }

        progress.SetPhase(TreasurePhase.Paused);
        pausing.Sample();
        pausing.BeginPause();
        currentTask = null;
        Svc.Automation.Stop();
        ReleaseHelpers();

        Diag($"Run paused ({reason}); session kept at {pausing.MapsCompleted} maps.");
        ECommons.DalamudServices.Svc.Chat.Print(reason == PauseReason.InContent
            ? $"{AtmConstants.LogPrefix} Paused: you are in instanced content. The run resumes once you are back outside."
            : $"{AtmConstants.LogPrefix} Paused. Your run and session stats are kept until you resume or stop.");
    }

    public void Resume()
    {
        if (!Paused)
        {
            return;
        }

        var resuming = session;
        if (resuming is null || !CanRestart())
        {
            Diag("Resume requested with no session or nothing to resume; stopping instead.");
            Stop();
            return;
        }

        PauseReason = PauseReason.None;
        resuming.EndPause();
        Diag("Resuming the run.");
        ECommons.DalamudServices.Svc.Chat.Print($"{AtmConstants.LogPrefix} Resuming the run.");
        StartRun(resuming);
    }

    public void TogglePause()
    {
        if (Paused)
        {
            Resume();
            return;
        }

        Pause(PauseReason.Manual);
    }
}
