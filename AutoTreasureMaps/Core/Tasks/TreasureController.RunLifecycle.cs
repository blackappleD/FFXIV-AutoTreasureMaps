using AutoTreasureMaps.Core.Cleanup;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Travel;

namespace AutoTreasureMaps.Core.Tasks;

internal sealed partial class TreasureController
{
    private const int MaxFaultResumes = 3;
    private const int MaxFaultResumesPerRun = 6;
    private const long FaultResumeWindowMs = 5 * TimeUnits.MillisecondsPerMinute;

    private int faultResumeCount;
    private int runFaultResumeCount;
    private long faultWindowStartedAtMs;

    private void OnRunEnded(TreasureSession owningSession)
    {
        var faulted = ReferenceEquals(session, owningSession) && owningSession.EndedWithFault;
        if (faulted && !owningSession.CompletedByStopCondition && TryAutoResumeAfterFault(owningSession))
        {
            return;
        }

        if (faulted)
        {
            ECommons.DalamudServices.Svc.Chat.PrintError($"{AtmConstants.LogPrefix} The run stopped on an unexpected error. The log has the details.");
        }

        EndRun(owningSession);
    }

    private void EndRun(TreasureSession owningSession)
    {
        FinalizeRun(owningSession, sample: true);
        if (!ReferenceEquals(session, owningSession))
        {
            Diag("A run that is no longer live ended; it was recorded and the current run is left alone.");
            return;
        }

        // The last maps' loot never reaches an upkeep, so a run that met its stop condition discards it before the
        // after-run action, which may log out or close the game. The list goes with the run either way.
        if (NeedsFinalCleanup(owningSession))
        {
            Diag($"Run ended by its stop condition; discarding {LootCleanup.Pending.Count} kinds of noted loot before anything else.");
            progress.SetPhase(TreasurePhase.Finishing);
            RunTask(new AutoFinalCleanup(), () =>
            {
                LootCleanup.Clear();
                FinishEnding(owningSession);
            });
            return;
        }

        LootCleanup.Clear();
        FinishEnding(owningSession);
    }

    private void FinishEnding(TreasureSession owningSession)
    {
        if (!TryRunAfterAction(owningSession) && !TryTravelBack(owningSession, ClearRun))
        {
            ClearRun();
        }
    }

    // Logging out or closing the game needs no trip, and the inn action travels home on its own; a run that ended any
    // other way on a world it visited goes back to the one it started on.
    private bool TryTravelBack(TreasureSession ending, Action onDone)
    {
        if (ending.EndedWithFault || ending.StartWorldId == 0 || Worlds.Current == 0 || Worlds.Current == ending.StartWorldId)
        {
            return false;
        }

        Diag($"Run ended on {Worlds.NameOf(Worlds.Current)}; travelling back to {Worlds.NameOf(ending.StartWorldId)}, where it started.");
        progress.SetPhase(TreasurePhase.Finishing);
        RunTask(new AutoReturnToWorld(ending.StartWorldId), () =>
        {
            Diag("The trip back to the starting world finished.");
            onDone();
        });
        return true;
    }

    private static bool NeedsFinalCleanup(TreasureSession ending)
        => ending.CompletedByStopCondition && !ending.EndedWithFault && Plugin.Instance.Configuration.AutoDiscard && LootCleanup.HasPending;

    // The finished run stays on screen while its after-run action runs, and is cleared once that ends.
    private bool TryRunAfterAction(TreasureSession ending)
    {
        if (ending.AfterActionDispatched)
        {
            return false;
        }

        if (!ending.CompletedByStopCondition || ending.EndedWithFault)
        {
            Diag($"Run ended without meeting its stop condition (fault {ending.EndedWithFault}); no after-run action.");
            return false;
        }

        ending.AfterActionDispatched = true;
        var action = Plugin.Instance.Configuration.AfterRun;
        if (action == AfterRunAction.StayLoggedIn)
        {
            Diag("Run completed by its stop condition; the after-run action is StayLoggedIn, nothing to do.");
            return false;
        }

        if (ending.DidNothing)
        {
            Diag($"Run ended by its stop condition without doing any work; skipping after-run action {action}.");
            return false;
        }

        Diag($"Run completed by its stop condition; starting after-run action {action}.");
        progress.SetPhase(TreasurePhase.Finishing);
        AutoCommon task = action == AfterRunAction.ReturnToInn ? new AutoReturnToInn() : new AutoAfterRun(action);
        RunTask(task, () =>
        {
            Diag($"After-run action {action} finished.");
            ClearRun();
        });
        return true;
    }

    // Idempotent through Recorded, so an explicit Stop and a finished task can both call it.
    private void FinalizeRun(TreasureSession? ending, bool sample)
    {
        if (ending is null || ending.Recorded)
        {
            return;
        }

        ending.Recorded = true;
        ending.End();
        try
        {
            if (sample)
            {
                ending.Sample();
            }

            if (ending.DidNothing)
            {
                Diag("Run did no work; nothing recorded to history.");
                return;
            }

            var record = new RunRecord
            {
                StartedAtUtc = ending.StartedAt,
                EndedAtUtc = DateTime.UtcNow,
                DurationSeconds = ending.Elapsed.TotalSeconds,
                MapsCompleted = ending.MapsCompleted,
                MapsRun = ending.MapsRun,
                MapsTimedOut = ending.MapsTimedOut,
                MapsKnockedOut = ending.MapsKnockedOut,
                MapsDigFailed = ending.MapsDigFailed,
                MapsUnreachable = ending.MapsUnreachable,
                MapsFaulted = ending.MapsFaulted,
                PortalsSeen = ending.PortalsSeen,
                DungeonsRun = ending.DungeonsRun,
                DungeonsCleared = ending.DungeonsCleared,
                Gil = ending.Gil,
                ItemsGained = ending.ItemsGained,
                Tomestones = ending.Tomestones,
                MapsBought = ending.MapsBought,
                GilSpentOnMaps = ending.GilSpentOnMaps,
                MapsGathered = ending.MapsGathered,
                NotableItems = ending.NotableItems.Count == 0 ? null : new Dictionary<uint, int>(ending.NotableItems),
                Tallies = ending.Tallies.Count == 0 ? null : MapTally.Copy(ending.Tallies),
                EndedBy = ending.EndedBy ?? (ending.EndedWithFault ? RunEnd.Faulted : RunEnd.Stopped),
                Refusal = ending.EndRefusal,
                JobAbbreviation = ending.JobAbbreviation,
            };
            Plugin.Instance.History.Append(record);
            Diag($"Run recorded to history: {record.MapsRun} maps run, {record.MapsCompleted} looted, {record.Gil} gil, {record.MapsBought} bought for {record.GilSpentOnMaps} gil, ended {record.EndedBy}, over {record.Duration} as {record.JobAbbreviation}.");
        }
        catch (Exception exception)
        {
            Diag($"FinalizeRun failed to record history: {exception.Message}");
        }
    }

    private void ResetFaultBudget()
    {
        faultResumeCount = 0;
        runFaultResumeCount = 0;
        faultWindowStartedAtMs = 0;
    }

    // The window restarts once it lapses, so sparse faults over a long run each get a fresh budget; only a burst, a wedge
    // that faults again straight away, spends it. The per-run cap still ends a run whose faults keep coming, however far
    // apart they are.
    private bool TryAutoResumeAfterFault(TreasureSession owningSession)
    {
        if (!Plugin.Instance.Configuration.AutoResumeOnFault)
        {
            Diag("Run task faulted and auto-resume on fault is off; the run ends.");
            return false;
        }

        if (!CanRestart())
        {
            Diag("Run task faulted with nothing to resume; the run ends.");
            return false;
        }

        if (runFaultResumeCount >= MaxFaultResumesPerRun)
        {
            Diag($"Run task faulted after {runFaultResumeCount} restarts in this run; not resuming. The run ends.");
            return false;
        }

        var now = Environment.TickCount64;
        if (now - faultWindowStartedAtMs > FaultResumeWindowMs)
        {
            faultResumeCount = 0;
            faultWindowStartedAtMs = now;
        }

        if (faultResumeCount >= MaxFaultResumes)
        {
            Diag($"Run task faulted {faultResumeCount} times within {FaultResumeWindowMs / TimeUnits.MillisecondsPerMinute} minutes; not resuming. The run ends.");
            return false;
        }

        faultResumeCount++;
        runFaultResumeCount++;
        owningSession.ClearFault();
        Diag($"Run task ended on an unexpected fault; auto-resuming (resume {faultResumeCount}/{MaxFaultResumes} in this {FaultResumeWindowMs / TimeUnits.MillisecondsPerMinute} minute window, {runFaultResumeCount}/{MaxFaultResumesPerRun} in this run).");
        ECommons.DalamudServices.Svc.Chat.Print($"{AtmConstants.LogPrefix} The run stopped on an unexpected error; restarting it ({faultResumeCount}/{MaxFaultResumes}).");
        StartRun(owningSession);
        return true;
    }
}
