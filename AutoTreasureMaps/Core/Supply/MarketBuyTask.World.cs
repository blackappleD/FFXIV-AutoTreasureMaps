using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Market;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Core.Treasure;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Supply;

// Before the board is opened, Universalis tells which world of the data centre sells the map cheapest. The run visits
// that world when the saving is worth the trip and buys there, digging on whatever world it stands on; the board it
// opens there is read afresh, so a stale or sold listing only costs the trip.
internal sealed partial class MarketBuyTask
{
    // Worlds tried after the first whose board had nothing under the cap after all.
    private const int MaxWorldHops = 2;
    private const int PriceQueryTimeoutMs = 15_000;

    private readonly HashSet<uint> excludedWorlds = [];

    // The world travelled to for this visit, or 0 when the run stayed where it was.
    private async Task<uint> MoveToCheapestWorld(Configuration configuration, List<TreasureTier> tiers)
    {
        if (!configuration.MarketCrossWorld || !LifestreamIPC.Instance.IsAvailable)
        {
            if (configuration.MarketCrossWorld)
            {
                Diag("Market: cross-world buying is on but Lifestream is not loaded; buying on this world.");
            }

            return 0;
        }

        var here = Worlds.Current;
        var dataCenter = Worlds.DataCenterOf(here);
        if (here == 0 || dataCenter.Length == 0)
        {
            return 0;
        }

        var spendable = MarketRules.Spendable(configuration, session);
        for (var index = 0; index < tiers.Count; index++)
        {
            var tier = tiers[index];
            var listings = await QueryPrices(dataCenter, tier);
            if (listings is null)
            {
                return 0;
            }

            var cap = MarketRules.CapOf(configuration, tier.Rank);
            var cheapest = Cheapest(listings, cap, spendable, worldId => worldId != here && !excludedWorlds.Contains(worldId) && Worlds.Visitable(worldId));
            var cheapestHere = Cheapest(listings, cap, spendable, worldId => worldId == here);
            if (cheapest is null && cheapestHere is null)
            {
                Diag($"Market: Universalis shows no {tier.Name} under {cap} gil in {dataCenter}.");
                continue;
            }

            if (cheapest is not { } best || (cheapestHere is { } local && local.UnitPrice - best.UnitPrice < configuration.MarketCrossWorldMinSavings))
            {
                Diag($"Market: a trip for {tier.Name} saves too little ({Describe(cheapestHere)} on {Worlds.NameOf(here)}, {Describe(cheapest)} elsewhere); staying.");
                return 0;
            }

            var name = Worlds.NameOf(best.WorldId);
            Diag($"Market: Universalis lists {tier.Name} at {best.UnitPrice} gil on {name}, {Describe(cheapestHere)} on {Worlds.NameOf(here)}.");
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Market.ChatCheapestWorld, tier.Name, name, best.UnitPrice.ToString("N0", Loc.Culture))}");
            CloseBoardNow();
            return await ChangeWorld(best.WorldId, "market-world") ? best.WorldId : 0;
        }

        return 0;
    }

    // Null when Universalis could not be asked; the run then buys where it stands.
    private async Task<List<WorldListing>?> QueryPrices(string dataCenter, TreasureTier tier)
    {
        Status = $"Comparing prices of {tier.Name} across {dataCenter}";
        var request = Universalis.Listings(dataCenter, tier.TimewornItemId, CancelToken);
        await WaitUntilTimed(() => request.IsCompleted, PriceQueryTimeoutMs, "universalis prices");
        if (request.IsCompletedSuccessfully)
        {
            return request.Result;
        }

        var why = request.IsCompleted ? request.Exception?.GetBaseException().Message ?? "cancelled" : "no answer in time";
        Warn($"Market: could not read prices from Universalis ({why}); buying on this world.");
        return null;
    }

    // Maps are unique, so only a single map can be bought; the tax counts against the gil that may be spent.
    private static WorldListing? Cheapest(List<WorldListing> listings, int cap, long spendable, Func<uint, bool> onWorld)
    {
        WorldListing? best = null;
        for (var index = 0; index < listings.Count; index++)
        {
            var listing = listings[index];
            if (listing.Quantity != 1 || listing.UnitPrice <= 0 || listing.UnitPrice > cap || listing.UnitPrice + listing.Tax > spendable || !onWorld(listing.WorldId))
            {
                continue;
            }

            if (best is not { } current || listing.UnitPrice < current.UnitPrice)
            {
                best = listing;
            }
        }

        return best;
    }

    private static string Describe(WorldListing? listing) => listing is { } found ? $"{found.UnitPrice} gil" : "none";
}
