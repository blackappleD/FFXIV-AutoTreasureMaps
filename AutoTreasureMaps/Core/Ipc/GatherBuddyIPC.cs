using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace AutoTreasureMaps.Core.Ipc;

// GatherBuddy Reborn's vendor buy lists: a list is written by name (each entry an item and the count to hold in the bags,
// armoury and gear), then started, and GatherBuddy travels to the vendors and buys until the counts or the currency run out.
internal sealed class GatherBuddyIPC
{
    // VendorBuyListReplaceByName arrived in IPC version 8.
    private const int MinimumIpcVersion = 8;
    private const string Prefix = "GatherBuddyReborn.";
    private const string VersionFailed = AtmConstants.LogPrefix + " GatherBuddy Reborn Version failed";
    private const string ReplaceFailed = AtmConstants.LogPrefix + " GatherBuddy Reborn VendorBuyListReplaceByName failed";
    private const string StartFailed = AtmConstants.LogPrefix + " GatherBuddy Reborn VendorBuyListStart failed";
    private const string BusyFailed = AtmConstants.LogPrefix + " GatherBuddy Reborn VendorBuyListIsBusy failed";
    private const string StopFailed = AtmConstants.LogPrefix + " GatherBuddy Reborn VendorBuyListStop failed";
    private const string LastRunFailed = AtmConstants.LogPrefix + " GatherBuddy Reborn VendorBuyListLastRun failed";
    private const string StatusFailed = AtmConstants.LogPrefix + " GatherBuddy Reborn VendorBuyListStatusText failed";

    // GatherBuddyIpc.VendorBuyListNotReady: the vendor data is still being read.
    public const int ReplaceNotReady = -4;
    // VendorBuyListManager.StartResult.Started.
    public const int StartStarted = 0;
    // VendorBuyListManager.StartResult.NoPendingEntries: every item already reaches its count.
    public const int StartNoPendingEntries = 6;
    // VendorBuyListManager.RunOutcome.Completed and CompletedWithIssues.
    public const int OutcomeCompleted = 1;
    public const int OutcomeCompletedWithIssues = 2;

    private static GatherBuddyIPC? instance;

    private readonly ICallGateSubscriber<int> version;
    private readonly ICallGateSubscriber<string, (uint ItemId, uint TargetQuantity)[], int> replaceByName;
    private readonly ICallGateSubscriber<string, int> start;
    private readonly ICallGateSubscriber<bool> isBusy;
    private readonly ICallGateSubscriber<object> stop;
    private readonly ICallGateSubscriber<(int Outcome, bool HitCurrencyLimit)> lastRun;
    private readonly ICallGateSubscriber<string> statusText;

    private GatherBuddyIPC()
    {
        var pluginInterface = Svc.PluginInterface;
        version = pluginInterface.GetIpcSubscriber<int>(Prefix + "Version");
        replaceByName = pluginInterface.GetIpcSubscriber<string, (uint ItemId, uint TargetQuantity)[], int>(Prefix + "VendorBuyListReplaceByName");
        start = pluginInterface.GetIpcSubscriber<string, int>(Prefix + "VendorBuyListStart");
        isBusy = pluginInterface.GetIpcSubscriber<bool>(Prefix + "VendorBuyListIsBusy");
        stop = pluginInterface.GetIpcSubscriber<object>(Prefix + "VendorBuyListStop");
        lastRun = pluginInterface.GetIpcSubscriber<(int Outcome, bool HitCurrencyLimit)>(Prefix + "VendorBuyListLastRun");
        statusText = pluginInterface.GetIpcSubscriber<string>(Prefix + "VendorBuyListStatusText");
    }

    public static GatherBuddyIPC Instance => instance ??= new GatherBuddyIPC();

    public bool IsAvailable
        => replaceByName.HasFunction
        && start.HasFunction
        && isBusy.HasFunction
        && IpcGate.Invoke(version.HasFunction, version.InvokeFunc, 0, VersionFailed) >= MinimumIpcVersion;

    // The number of entries written, or a negative GatherBuddy code: -2 bad name, -3 a list is running, -4 vendor data
    // still loading. An item no vendor sells is left out of the count.
    public int ReplaceList(string listName, (uint ItemId, uint TargetQuantity)[] items)
        => IpcGate.Invoke(replaceByName.HasFunction, () => replaceByName.InvokeFunc(listName, items), int.MinValue, ReplaceFailed);

    // A VendorBuyListManager.StartResult, or -1 when no list has that name.
    public int StartList(string listName)
        => IpcGate.Invoke(start.HasFunction, () => start.InvokeFunc(listName), int.MinValue, StartFailed);

    public bool IsBusy() => IpcGate.Invoke(isBusy.HasFunction, isBusy.InvokeFunc, false, BusyFailed);

    public void Stop() => IpcGate.Run(stop.HasAction, stop.InvokeAction, StopFailed);

    public (int Outcome, bool HitCurrencyLimit) LastRun()
        => IpcGate.Invoke(lastRun.HasFunction, lastRun.InvokeFunc, (0, false), LastRunFailed);

    public string StatusText() => IpcGate.Invoke(statusText.HasFunction, statusText.InvokeFunc, string.Empty, StatusFailed);
}
