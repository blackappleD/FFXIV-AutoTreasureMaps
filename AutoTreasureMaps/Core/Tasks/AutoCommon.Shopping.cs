using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Shopping;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    // GatherBuddy teleports to each vendor and back through its menus; a list that has not finished by then is stopped.
    private const int ShoppingListWaitMs = 900_000;
    private const int ShoppingStartWaitMs = 5_000;
    private const int ShoppingVendorDataWaitMs = 30_000;
    private const int ShoppingPollFrames = 30;
    // A tomestone still over its threshold after a trip (an item no vendor sold, or one already at its count) would send
    // the run shopping after every map, so the next trip waits.
    private const int ShoppingRetryMs = 1_800_000;

    private long shoppingRetryAtMs;

    private bool UpkeepShoppingDue(Configuration configuration)
        => configuration.TomestoneShopping
        && Environment.TickCount64 >= shoppingRetryAtMs
        && TomestoneShopping.TryFindTrigger(configuration, out _, out _)
        && TomestoneShopping.Available;

    private async Task RunScheduledShopping(Configuration configuration)
    {
        if (!TomestoneShopping.TryFindTrigger(configuration, out var trigger, out var held))
        {
            return;
        }

        var threshold = TomestoneShopping.ListFor(configuration, trigger.ItemId).Threshold;
        Diag($"Shopping: {trigger.Name} at {held} (threshold {threshold}); buying the tomestone lists through GatherBuddy Reborn");
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Shopping.ChatStart, trigger.Name, held)}");

        foreach (var (currency, list) in TomestoneShopping.ActiveLists(configuration))
        {
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            await BuyTomestoneList(currency, list);
        }

        if (!CancelToken.IsCancellationRequested && TomestoneShopping.TryFindTrigger(configuration, out var still, out var left))
        {
            shoppingRetryAtMs = Environment.TickCount64 + ShoppingRetryMs;
            Warn($"Shopping: {still.Name} is still at {left} after the trip; the next one waits {ShoppingRetryMs / TimeUnits.MillisecondsPerMinute} minutes");
        }
    }

    private async Task BuyTomestoneList(TomestoneCurrency currency, TomestoneShoppingList list)
    {
        var gatherBuddy = GatherBuddyIPC.Instance;
        var name = TomestoneShopping.ListName(currency);
        var targets = TomestoneShopping.Targets(list);
        if (targets.Length == 0)
        {
            return;
        }

        var written = gatherBuddy.ReplaceList(name, targets);
        if (written == GatherBuddyIPC.ReplaceNotReady)
        {
            // The first call has GatherBuddy read its vendor data, which takes a few seconds.
            await WaitUntilTimed(() => (written = gatherBuddy.ReplaceList(name, targets)) != GatherBuddyIPC.ReplaceNotReady,
                ShoppingVendorDataWaitMs, "shopping-vendor-data", ShoppingPollFrames);
        }

        if (written < 0)
        {
            Warn($"Shopping: GatherBuddy Reborn refused the list '{name}' ({written})");
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Shopping.ChatListFailed, name)}");
            return;
        }

        if (written < targets.Length)
        {
            Warn($"Shopping: GatherBuddy Reborn found a vendor for {written} of the {targets.Length} items of '{name}'");
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Shopping.ChatSkipped, name, targets.Length - written)}");
        }

        if (written == 0)
        {
            return;
        }

        var started = gatherBuddy.StartList(name);
        if (started == GatherBuddyIPC.StartNoPendingEntries)
        {
            Diag($"Shopping: '{name}' has nothing left to buy");
            return;
        }

        if (started != GatherBuddyIPC.StartStarted)
        {
            Warn($"Shopping: GatherBuddy Reborn did not start '{name}' (StartResult {started}: {gatherBuddy.StatusText()})");
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Shopping.ChatListFailed, name)}");
            return;
        }

        Status = Loc.T(L.Status.Shopping, currency.Name);
        var before = TomestoneShopping.HeldCount(currency.ItemId);
        Diag($"Shopping: '{name}' started with {written} items, {currency.Name} at {before}");
        await WaitUntilTimed(gatherBuddy.IsBusy, ShoppingStartWaitMs, "shopping-start", ShoppingPollFrames);
        var finished = await WaitUntilTimed(() => !gatherBuddy.IsBusy(), ShoppingListWaitMs, "shopping-list", ShoppingPollFrames);
        if (!finished)
        {
            gatherBuddy.Stop();
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            Warn($"Shopping: '{name}' did not finish in {ShoppingListWaitMs / TimeUnits.MillisecondsPerMinute} minutes; stopped it");
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Shopping.ChatListFailed, name)}");
            return;
        }

        var (outcome, hitCurrencyLimit) = gatherBuddy.LastRun();
        var spent = before - TomestoneShopping.HeldCount(currency.ItemId);
        Diag($"Shopping: '{name}' ended with outcome {outcome}, out of currency {hitCurrencyLimit}, {spent} {currency.Name} spent ({gatherBuddy.StatusText()})");
        if (outcome is GatherBuddyIPC.OutcomeCompleted or GatherBuddyIPC.OutcomeCompletedWithIssues)
        {
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Shopping.ChatDone, name, spent, currency.Name)}");
            return;
        }

        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Shopping.ChatListFailed, name)}");
    }
}
