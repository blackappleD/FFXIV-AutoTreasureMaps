using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AutoTask = clib.Services.AutoTask;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon : AutoTask
{
    private const int DelayPollFrames = 2;
    private const int WaitPollFrames = 30;
    private const int NavmeshPollFrames = 120;

    private TreasurePhase currentPhase = TreasurePhase.Idle;

    internal TreasurePhase CurrentPhase
    {
        get => currentPhase;
        private set
        {
            if (currentPhase == value)
            {
                return;
            }

            currentPhase = value;
            OnPhaseChanged(value);
        }
    }

    protected void Diag(string message, [CallerFilePath] string callerFile = "") => RunLog.Info(message, callerFile);

    protected void Warn(string message, [CallerFilePath] string callerFile = "") => RunLog.Warning(message, callerFile);

    // Lets a run loop surface the run's phase the moment it changes instead of polling for it.
    private protected virtual void OnPhaseChanged(TreasurePhase phase)
    {
    }

    protected new async Task DelayMs(int milliseconds)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            await NextFrame(DelayPollFrames);
        }
    }

    // True once the drawn wait ran out; false when the run was cancelled or abortIf tripped first.
    // A task that keeps pace with another character takes no waits of its own: the one it follows has taken them.
    private protected virtual bool KeepsPace => false;

    protected async Task<bool> HumanDelay(HumanAction action, string label, Func<bool>? abortIf = null)
    {
        var delayMs = KeepsPace ? 0 : Humanizer.DrawDelayMs(action);
        if (delayMs <= 0)
        {
            return !CancelToken.IsCancellationRequested;
        }

        RunLog.Debug($"{label}: waiting {delayMs} ms before {Humanizer.Describe(action)}");
        var deadline = Environment.TickCount64 + delayMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested || (abortIf is not null && abortIf()))
            {
                return false;
            }

            await NextFrame(DelayPollFrames);
        }

        return !CancelToken.IsCancellationRequested;
    }

    // Pinned every frame because the movement library overwrites Status with raw coordinates mid-teleport.
    protected async Task RunWithStatusPinned(string label, Func<Task> work)
    {
        Status = label;
        IFramework.OnUpdateDelegate pin = _ => Status = label;
        Svc.Framework.Update += pin;
        try
        {
            await work();
        }
        finally
        {
            Svc.Framework.Update -= pin;
        }
    }

    // A step run as its own task reports into its own Status, and the windows only read the run's.
    protected async Task RunWithStatusFrom(AutoTask source, Func<Task> work)
    {
        IFramework.OnUpdateDelegate mirror = _ => Status = source.Status;
        Svc.Framework.Update += mirror;
        try
        {
            await work();
        }
        finally
        {
            Svc.Framework.Update -= mirror;
        }
    }

    protected async Task<bool> WaitUntilTimed(Func<bool> condition, int timeoutMs, string scope, int checkFrames = WaitPollFrames)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var threw = false;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            if (ConditionHolds(condition, scope, ref threw))
            {
                return true;
            }

            await NextFrame(checkFrames);
        }

        Diag($"WAIT TIMEOUT: '{scope}' not satisfied within {timeoutMs / TimeUnits.MillisecondsPerSecond}s");
        return false;
    }

    // After a teleport the destination mesh is still building, and pathfind queries issued now race it and fault.
    protected async Task WaitForNavmeshReady(int timeoutMs, int pollFrames = NavmeshPollFrames)
    {
        var navmesh = NavmeshIPC.Instance;
        if (navmesh.IsReady())
        {
            return;
        }

        var deadline = Environment.TickCount64 + timeoutMs;
        while (!navmesh.IsReady())
        {
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            if (Environment.TickCount64 >= deadline)
            {
                Diag($"WAIT TIMEOUT: navmesh not ready within {timeoutMs / TimeUnits.MillisecondsPerSecond}s; proceeding anyway");
                return;
            }

            var progress = navmesh.BuildProgress();
            Status = progress is >= 0f and <= 1f
                ? Loc.T(L.Status.NavmeshLoadingPercent, (int)MathF.Round(progress * 100f))
                : Loc.T(L.Status.NavmeshLoading);
            await NextFrame(pollFrames);
        }
    }

    private bool ConditionHolds(Func<bool> condition, string scope, ref bool threw)
    {
        try
        {
            return condition();
        }
        catch (Exception exception)
        {
            if (!threw)
            {
                Warn($"WaitUntilTimed '{scope}' condition threw (treating as unsatisfied; will retry until timeout): {exception.Message}");
                threw = true;
            }

            return false;
        }
    }
}
