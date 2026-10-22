using AutoTreasureMaps.Core.Cleanup;
using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.External;
using AutoTreasureMaps.Core.Gather;
using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;
using clib.Services;

namespace AutoTreasureMaps.Core.Tasks;

internal sealed partial class TreasureController
{
    private const int SessionSampleIntervalMs = 1_000;

    private readonly TreasureProgress progress = new();

    private TreasureSession? session;
    private Func<TreasureSession, AutoCommon>? runTaskFactory;
    private AutoCommon? currentTask;
    private long nextSessionSampleAtMs;

    public bool Running => Svc.Automation.Running || Paused;

    public string Status => PauseReason switch
    {
        PauseReason.InContent => "Paused while you are in content",
        PauseReason.Manual    => "Paused",
        _                     => Svc.Automation.CurrentTask?.Status ?? "Idle",
    };

    public TreasurePhase Phase => progress.Phase;

    public TreasureProgress Progress => progress;

    public TreasureSession? SessionSnapshot => session;

    // Set while the run is a party member following its leader in party mode.
    public bool Following { get; private set; }

    public bool CanStopAfterMap => session is { ManyMaps: true, EndedBy: null };

    public bool StopAfterMap => session?.StopAfterMap == true;

    // Asks the run to end once the map under way is over, or takes that back. A run of one map or one dungeon ends after
    // it anyway.
    public void ToggleStopAfterMap()
    {
        // A run that has ended but is still on its after-run action or cleanup has nothing left to stop after.
        if (session is null || session.EndedBy is not null)
        {
            ECommons.DalamudServices.Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatNothingToStop)}");
            return;
        }

        if (!CanStopAfterMap)
        {
            ECommons.DalamudServices.Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatStopAfterMapSingle)}");
            return;
        }

        session.ToggleStopAfterMap();
        Diag(session.StopAfterMap ? "Stop after this map requested." : "Stop after this map taken back.");
        var line = session.StopAfterMap
            ? Loc.Plural(L.Run.ChatStopAfterMapOn, session.MapsRun)
            : Loc.T(L.Run.ChatStopAfterMapOff);
        ECommons.DalamudServices.Svc.Chat.Print($"{AtmConstants.LogPrefix} {line}");
    }

    private static void Diag(string message)
        => RunLog.Info(message);

    // Maps until a stop condition: the held one first, then whatever the supply chain puts in hand.
    public void Start()
    {
        if (session is not null)
        {
            Diag("Start ignored: a run is already going.");
            return;
        }

        if (!RequiredPluginsReady())
        {
            return;
        }

        // In a party every member but the leader follows it rather than running maps of its own.
        if (RunReadiness.FollowsLeader())
        {
            if (AutoFollowRun.CanStart())
            {
                ECommons.DalamudServices.Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatFollowStarted)}");
                BeginRun(new TreasureSession(), owningSession => new AutoFollowRun(owningSession, progress), "following the party leader");
                Following = true;
            }

            return;
        }

        var check = RunReadiness.Check(Plugin.Instance);
        if (!check.CanStart)
        {
            Diag($"Start refused: {check.Blocker}.");
            ECommons.DalamudServices.Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatCannotStart, Loc.T(check.Reason))}");
            return;
        }

        ECommons.DalamudServices.Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.Plural(L.Run.ChatStarted, check.Available)}");
        if (Plugin.Instance.Configuration.GatherDailyMap)
        {
            MapAllowance.Request("run start");
        }

        // Start pressed inside a treasure dungeon asks for that dungeon to be cleared first, so it counts as the run's own.
        var newSession = new TreasureSession { ManyMaps = true };
        if (DungeonRegistry.IsDungeon(ECommons.DalamudServices.Svc.ClientState.TerritoryType))
        {
            newSession.MarkOwnDungeon();
        }

        BeginRun(newSession, owningSession => new AutoTreasureRun(owningSession, progress), $"maps until a stop condition, {check.Available} to go");
    }

    // One held map from travel to loot, under the same pause, stop and fault handling as any run. The caller has
    // checked MapRunReadiness first.
    public void StartHeldMap()
    {
        if (Running)
        {
            Diag("Map run ignored: a run is already going.");
            return;
        }

        if (!RequiredPluginsReady())
        {
            return;
        }

        BeginRun(new TreasureSession(), owningSession => new AutoMapRun(owningSession, progress), "the held map");
    }

    // The treasure dungeon the character stands in, under the same pause, stop and fault handling as any run.
    public void StartDungeon()
    {
        if (Running)
        {
            Diag("Dungeon run ignored: a run is already going.");
            return;
        }

        if (!RequiredPluginsReady() || !AutoDungeonRun.CanStart())
        {
            return;
        }

        BeginRun(new TreasureSession(), owningSession => new AutoDungeonRun(owningSession, progress), "the treasure dungeon");
    }

    // travelBack is set when the player stops the run, so a run that bought on another world goes back to its own; an
    // unload or a GM alert leaves the character where it stands.
    public void Stop(bool travelBack = false)
    {
        var ending = session;
        var wasPaused = Paused;
        var taskWasRunning = Svc.Automation.Running;
        ending?.MarkStopped();
        currentTask = null;
        PauseReason = PauseReason.None;
        Svc.Automation.Stop();
        // The noted loot belongs to this run, so it goes with it.
        LootCleanup.Clear();
        if (ending is not null || taskWasRunning)
        {
            ReleaseHelpers();
        }

        // Pause already credited the run, and anything done since was the player's own play.
        FinalizeRun(ending, sample: !wasPaused);
        ClearRun();
        if (ending is not null)
        {
            Diag("Stop requested; session cleared.");
            if (travelBack)
            {
                TryTravelBack(ending, static () => { });
            }
        }
    }

    public void Tick()
    {
        if (session is null || session.Recorded || Paused)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now < nextSessionSampleAtMs)
        {
            return;
        }

        nextSessionSampleAtMs = now + SessionSampleIntervalMs;
        session.Sample();
    }

    private static bool RequiredPluginsReady()
    {
        if (ExternalPlugins.AllRequiredInstalled())
        {
            return true;
        }

        var missing = ExternalPlugins.MissingRequiredNames();
        Diag($"Start aborted: required plugins missing ({missing}).");
        ECommons.DalamudServices.Svc.Chat.PrintError($"{AtmConstants.LogPrefix} Cannot start: install all required plugins first ({missing}).");
        return false;
    }

    // Every Resume and fault restart builds the run task afresh from the same factory.
    private void BeginRun(TreasureSession newSession, Func<TreasureSession, AutoCommon> taskFactory, string plan)
    {
        PauseReason = PauseReason.None;
        ResetFaultBudget();
        session = newSession;
        runTaskFactory = taskFactory;
        Diag($"Run starting: {plan}, job {newSession.JobAbbreviation}.");
        NavmeshSettings.WarnIfInputCancelsMovement();
        StartRun(newSession);
    }

    private void StartRun(TreasureSession owningSession)
    {
        progress.Reset();
        progress.SetPhase(TreasurePhase.Preparing);
        RunTask(runTaskFactory!(owningSession), () => OnRunEnded(owningSession));
    }

    private bool CanRestart() => runTaskFactory is not null;

    // The movement library fires OnCompleted off the game thread: its await of the task does not return to the framework
    // scheduler, and the runtime moves the continuation to the thread pool. Recording a run reads the object table, which
    // Dalamud allows only on the game thread, so the hand-off is moved back there.
    private void RunTask(AutoCommon task, Action onCompleted)
    {
        currentTask = task;
        Svc.Automation.Start(task, OnCompleted: () => _ = ECommons.DalamudServices.Svc.Framework.RunOnFrameworkThread(() => HandOff(task, onCompleted)));
    }

    private void HandOff(AutoCommon task, Action onCompleted)
    {
        if (!ReferenceEquals(currentTask, task))
        {
            Diag($"{task.GetType().Name} finished but is no longer the current task (stopped, paused, or superseded); skipping hand-off.");
            return;
        }

        currentTask = null;
        onCompleted();
    }

    private void ClearRun()
    {
        Following = false;
        session = null;
        runTaskFactory = null;
        progress.Reset();
    }

    // A stopped task unwinds on a later frame, and after an unload that frame may never come, so the combat preset and
    // the pathfinder are released here as well.
    private static void ReleaseHelpers()
    {
        BossModIPC.Instance.ClearActive();
        BossModIPC.Instance.ReleaseMovement();
        NavmeshIPC.Instance.Stop();
    }
}

internal enum TreasurePhase { Idle, Preparing, Deciphering, Buying, Travelling, Digging, Opening, Fighting, Looting, Upkeep, Finishing, Paused, Gathering }
