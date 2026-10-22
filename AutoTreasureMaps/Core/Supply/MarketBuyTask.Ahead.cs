using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Game.Watchers;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Market;
using AutoTreasureMaps.Core.Tasks;
using AutoTreasureMaps.Core.Treasure;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Supply;

// A timeworn map is unique in the bags, but a deciphered one is not the same item. So while the character still stands
// at the board, the map just bought is deciphered and a second one is bought into the bags: the next map then needs no
// trip to the board at all, which halves the travel when maps are bought one after another.
internal sealed partial class MarketBuyTask
{
    private const int AheadReadyWaitMs = 15_000;

    private TreasureTier? boughtTier;

    private async Task BuyAhead(Plugin plugin)
    {
        var configuration = plugin.Configuration;
        if (!configuration.MarketBuyAhead || session is null || boughtTier is not { } tier || CancelToken.IsCancellationRequested)
        {
            return;
        }

        // The map bought now is the run's next one, so a second one is only worth it when the run may go on after it.
        if (session.StopAfterMap || (configuration.RunMapLimit > 0 && session.MapsRun + 2 > configuration.RunMapLimit))
        {
            Diag("Market: the run ends after the next map, so none is bought ahead.");
            return;
        }

        if (HeldMapReader.TryRead(plugin.Catalog, out _) || !DecipherOps.Unlocked() || DutyWatcher.InContent())
        {
            Diag("Market: a map is already deciphered, or deciphering is not possible here; not buying one ahead.");
            return;
        }

        await CloseBoard();
        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        Status = $"Deciphering {tier.Name}";
        Diag($"Market: deciphering {tier.Name} at the board, so a second map can be bought ahead.");
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Decipher.ChatStarted, tier.Name)}");
        if (!NpcInteraction.PlayerReady())
        {
            await WaitUntilTimed(NpcInteraction.PlayerReady, AheadReadyWaitMs, "decipher at the board");
        }

        var issue = await DecipherMap(tier, DecipherRoute.Menu);
        if (issue != DecipherIssue.None)
        {
            // The bought map stays in the bags, and the run's own bag step deciphers it as usual.
            Diag($"Market: deciphering at the board failed ({issue}); not buying one ahead.");
            return;
        }

        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Decipher.ChatDone, tier.Name)}");
        var tiers = MarketRules.Candidates(plugin, session, onlyRank);
        if (tiers.Count == 0 || CancelToken.IsCancellationRequested)
        {
            return;
        }

        Diag($"Market: buying a second map ahead on this board ({tiers.Count} tier(s) to try).");
        boughtTier = null;
        var answer = await Visit(configuration, tiers);
        if (answer.Outcome == MapSupplyOutcome.Supplied)
        {
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Market.ChatBoughtAhead)}");
        }
        else
        {
            Diag($"Market: no map bought ahead ({answer.Outcome}); the next one is bought when it is needed.");
        }
    }
}
