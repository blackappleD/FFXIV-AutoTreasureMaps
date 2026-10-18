using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace AutoTreasureMaps.Core.Ipc;

// Lifestream changes worlds for the run: TPAndChangeWorld teleports to an aetheryte that can visit worlds, then visits.
internal sealed class LifestreamIPC
{
    private const string TravelFailed = AtmConstants.LogPrefix + " Lifestream TPAndChangeWorld failed";
    private const string IsBusyFailed = AtmConstants.LogPrefix + " Lifestream IsBusy failed";
    private const string CanVisitFailed = AtmConstants.LogPrefix + " Lifestream CanVisitSameDC failed";
    private const string AbortFailed = AtmConstants.LogPrefix + " Lifestream Abort failed";

    private static LifestreamIPC? instance;

    private readonly ICallGateSubscriber<string, bool, string?, bool, int?, bool?, bool?, object> travel;
    private readonly ICallGateSubscriber<bool> isBusy;
    private readonly ICallGateSubscriber<string, bool> canVisitSameDc;
    private readonly ICallGateSubscriber<object> abort;

    private LifestreamIPC()
    {
        var pluginInterface = Svc.PluginInterface;
        travel = pluginInterface.GetIpcSubscriber<string, bool, string?, bool, int?, bool?, bool?, object>("Lifestream.TPAndChangeWorld");
        isBusy = pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        canVisitSameDc = pluginInterface.GetIpcSubscriber<string, bool>("Lifestream.CanVisitSameDC");
        abort = pluginInterface.GetIpcSubscriber<object>("Lifestream.Abort");
    }

    public static LifestreamIPC Instance => instance ??= new LifestreamIPC();

    public bool IsAvailable => travel.HasAction && isBusy.HasFunction;

    public bool IsBusy() => IpcGate.Invoke(isBusy.HasFunction, isBusy.InvokeFunc, false, IsBusyFailed);

    public bool CanVisit(string worldName) => IpcGate.Invoke(canVisitSameDc.HasFunction, () => canVisitSameDc.InvokeFunc(worldName), false, CanVisitFailed);

    // No secondary teleport: the run travels on to the market board itself.
    public void ChangeWorld(string worldName)
        => IpcGate.Run(travel.HasAction, () => travel.InvokeAction(worldName, false, null, true, null, false, false), TravelFailed);

    public void Abort() => IpcGate.Run(abort.HasAction, abort.InvokeAction, AbortFailed);
}
