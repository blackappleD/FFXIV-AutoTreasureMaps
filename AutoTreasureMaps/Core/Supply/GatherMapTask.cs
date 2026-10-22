using AutoTreasureMaps.Core.External;
using AutoTreasureMaps.Core.Gather;
using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Tasks;
using AutoTreasureMaps.Core.Travel;
using ECommons.DalamudServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Supply;

// One attempt at the daily map: in a run it hands over a deciphered map; from /atm gather it only gathers. The plan is
// made afresh, since the bags or the gearsets may have changed since the run checked.
internal sealed class GatherMapTask(bool forRun) : MapSupplyTask
{
    private const string StopWord = "stop";
    private const int DecipherTimeoutMs = 120_000;

    public static void HandleCommand(string arguments, Plugin plugin)
    {
        var automation = clib.Services.Svc.Automation;
        if (arguments.Equals(StopWord, StringComparison.OrdinalIgnoreCase))
        {
            if (automation.CurrentTask is not GatherMapTask)
            {
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Gather.ChatNotRunning)}");
                return;
            }

            automation.Stop();
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Gather.ChatStopped)}");
            return;
        }

        if (CommandRefusal(plugin) is { } refusal)
        {
            RunLog.Info($"Gather refused: {refusal.Key}.");
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Gather.ChatRefused, Loc.T(refusal))}");
            return;
        }

        automation.Start(new GatherMapTask(forRun: false));
    }

    protected override async Task<MapSupplyAnswer> Supply()
    {
        var plugin = Plugin.Instance;
        Status = Loc.T(L.Status.CheckingDailyMap);
        var refusal = DailyMapPlanner.Plan(plugin, honourSwitch: forRun, honourAllowance: false, out var plan, out _);
        if (refusal != DailyMapRefusal.None)
        {
            return Refused(DailyMapPlanner.Reason(refusal, default));
        }

        var reading = await RefreshAllowance("gather start");
        if (CancelToken.IsCancellationRequested)
        {
            return MapSupplyAnswer.Cancelled;
        }

        if (reading.State != AllowanceState.Available)
        {
            var allowanceRefusal = reading.State == AllowanceState.Waiting ? DailyMapRefusal.AllowanceNotYet : DailyMapRefusal.AllowanceUnknown;
            return Refused(DailyMapPlanner.Reason(allowanceRefusal, reading));
        }

        if (!GatherOps.HasFreeBagSlot())
        {
            return Failed(L.Gather.ReasonBagsFull);
        }

        Diag($"Supply: gathering the daily map as {plan.Gatherer} (level {plan.ClassLevel}, gearset {plan.GearsetIndex + 1}), group of level {plan.Group.Level}.");
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Gather.ChatStarted, Loc.T(DailyMapPlanner.GathererName(plan.Gatherer)), TerritoryNames.Of(plan.Area.TerritoryId))}");
        var answer = await GatherDailyMap(plan, Math.Max(1, plugin.Configuration.DailyMapNodeVisits));
        if (answer.MapItemId != 0 || answer.Outcome == GatherOutcome.AllowanceUsed)
        {
            MapAllowance.MarkUsed();
        }

        if (answer.Outcome != GatherOutcome.Cancelled)
        {
            MapAllowance.Request("after a gather attempt");
            plugin.Controller.SessionSnapshot?.NoteDailyGatherTried();
        }

        // A map gathered before the switch back failed still counts.
        if (answer.MapItemId != 0)
        {
            plugin.Controller.SessionSnapshot?.NoteMapGathered();
        }

        switch (answer.Outcome)
        {
            case GatherOutcome.Cancelled:
                return MapSupplyAnswer.Cancelled;
            case GatherOutcome.NoMap or GatherOutcome.AllowanceUsed:
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Gather.ChatNoMap, Loc.T(answer.Reason))}");
                return MapSupplyAnswer.Empty(answer.Reason);
            case GatherOutcome.Failed:
                return Failed(answer.Reason);
        }

        var mapName = plugin.Catalog.TryGetByTimewornItem(answer.MapItemId, out var tier) ? tier.Name : answer.MapItemId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Gather.ChatGathered, mapName)}");
        return forRun ? await DecipherGathered() : MapSupplyAnswer.Supplied;
    }

    // The bag step's own task, so the decipher runs exactly as it does for a map that was already in the bags.
    private async Task<MapSupplyAnswer> DecipherGathered()
    {
        var decipher = new BagDecipherTask();
        await RunWithStatusFrom(decipher, () => RunCancellable(decipher, DecipherTimeoutMs, "gather-decipher"));
        if (decipher.Fault is { } fault)
        {
            ExceptionDispatchInfo.Throw(fault);
        }

        return decipher.Answer;
    }

    private static LocString? CommandRefusal(Plugin plugin)
    {
        if (!Svc.ClientState.IsLoggedIn || Svc.Objects.LocalPlayer is null)
        {
            return L.Decipher.NotLoggedIn;
        }

        if (plugin.Controller.SessionSnapshot is not null)
        {
            return L.Decipher.RunActive;
        }

        if (clib.Services.Svc.Automation.CurrentTask is not null)
        {
            return L.Decipher.TaskRunning;
        }

        if (!ExternalPlugins.AllRequiredInstalled())
        {
            return L.Treasure.DetailSetupNeeded;
        }

        var refusal = DailyMapPlanner.Plan(plugin, honourSwitch: false, honourAllowance: false, out _, out _);
        return refusal == DailyMapRefusal.None ? null : DailyMapPlanner.Reason(refusal, default);
    }

    private MapSupplyAnswer Refused(LocString reason)
    {
        Diag($"Supply: the daily map cannot be gathered ({reason.Key}).");
        if (!forRun)
        {
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Gather.ChatRefused, Loc.T(reason))}");
        }

        return MapSupplyAnswer.Empty(reason);
    }

    private static MapSupplyAnswer Failed(LocString reason)
    {
        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Gather.ChatFailed, Loc.T(reason))}");
        return MapSupplyAnswer.Failed(reason);
    }
}
