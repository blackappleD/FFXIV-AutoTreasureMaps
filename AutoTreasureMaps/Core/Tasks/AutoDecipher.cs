using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

// Deciphers one map on its own, outside a run: from the Treasure page's button or /atm decipher.
internal sealed class AutoDecipher(TreasureTier tier, DecipherRoute route) : AutoCommon
{
    // Test hook for the one-call route that skips the game's list: /atm decipher direct [map].
    private const string DirectWord = "direct";

    public static void HandleCommand(string arguments, Plugin plugin)
    {
        var route = DecipherRoute.Menu;
        var query = arguments;
        if (CommandWords.StartsWith(query, DirectWord))
        {
            route = DecipherRoute.Direct;
            query = CommandWords.After(query, DirectWord);
        }

        if (query.Length == 0)
        {
            StartNext(plugin, route);
            return;
        }

        if (Refused(DecipherReadiness.Blocker(plugin)))
        {
            return;
        }

        switch (DecipherReadiness.Find(plugin.Catalog, plugin.Configuration, query, out var pick, out var named))
        {
            case MapQueryResult.Found:
                Start(pick.Tier, route);
                break;
            case MapQueryResult.NotInBags:
                Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Decipher.ChatNotInBags, named.Name)}");
                break;
            default:
                Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Decipher.ChatUnknownTier, query)}");
                break;
        }
    }

    public static void StartNext(Plugin plugin, DecipherRoute route = DecipherRoute.Menu)
    {
        if (Refused(DecipherReadiness.Blocker(plugin)))
        {
            return;
        }

        if (Refused(DecipherReadiness.PickNext(plugin.Catalog, plugin.Configuration, out var pick)))
        {
            return;
        }

        Start(pick.Tier, route);
    }

    protected override async Task Execute()
    {
        Status = Loc.T(L.Status.Deciphering, tier.Name);
        var issue = await DecipherMap(tier, route);
        if (issue == DecipherIssue.None)
        {
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Decipher.ChatDone, tier.Name)}");
            return;
        }

        if (issue == DecipherIssue.Cancelled || CancelToken.IsCancellationRequested)
        {
            Diag("Decipher: cancelled.");
            return;
        }

        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Decipher.ChatFailed, tier.Name, Loc.T(DecipherIssues.Reason(issue)))}");
    }

    private static void Start(in TreasureTier tier, DecipherRoute route)
    {
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Decipher.ChatStarted, tier.Name)}");
        clib.Services.Svc.Automation.Start(new AutoDecipher(tier, route));
    }

    private static bool Refused(DecipherIssue issue)
    {
        if (issue == DecipherIssue.None)
        {
            return false;
        }

        RunLog.Info($"Decipher refused: {issue}.");
        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Decipher.ChatRefused, Loc.T(DecipherIssues.Reason(issue)))}");
        return true;
    }
}
