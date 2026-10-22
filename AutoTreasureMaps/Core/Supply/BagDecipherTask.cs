using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Supply;

// The bag step's work: the character is freed for the decipher first, since the game refuses it mounted, in combat or
// mid-action, and the pick is read again in case the bags changed since the run checked them.
internal sealed class BagDecipherTask : MapSupplyTask
{
    private const string Scope = "supply-bags";
    private const int PlayerReadyWaitMs = 15_000;

    protected override async Task<MapSupplyAnswer> Supply()
    {
        var plugin = Plugin.Instance;
        var pickIssue = DecipherReadiness.PickNext(plugin.Catalog, plugin.Configuration, out var pick);
        if (pickIssue != DecipherIssue.None)
        {
            Diag($"Supply: nothing to decipher from the bags ({pickIssue}).");
            return MapSupplyAnswer.Empty(DecipherIssues.Reason(pickIssue));
        }

        var tier = pick.Tier;
        Status = Loc.T(L.Status.Deciphering, tier.Name);
        await FightOffAttackers(Scope);
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            await SafeDismount($"{Scope}-dismount");
        }

        if (!NpcInteraction.PlayerReady())
        {
            await WaitUntilTimed(NpcInteraction.PlayerReady, PlayerReadyWaitMs, "decipher player ready");
        }

        if (CancelToken.IsCancellationRequested)
        {
            return MapSupplyAnswer.Cancelled;
        }

        Diag($"Supply: deciphering {tier.Name} from the bags ({pick.Count} held).");
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Decipher.ChatStarted, tier.Name)}");
        var issue = await DecipherMap(tier, DecipherRoute.Menu);
        if (issue == DecipherIssue.None)
        {
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Decipher.ChatDone, tier.Name)}");
            return MapSupplyAnswer.Supplied;
        }

        if (issue == DecipherIssue.Cancelled || CancelToken.IsCancellationRequested)
        {
            return MapSupplyAnswer.Cancelled;
        }

        var reason = DecipherIssues.Reason(issue);
        if (issue is DecipherIssue.NoMaps or DecipherIssue.NoEnabledMaps)
        {
            return MapSupplyAnswer.Empty(reason);
        }

        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Decipher.ChatFailed, tier.Name, Loc.T(reason))}");
        return MapSupplyAnswer.Failed(reason);
    }
}
