using AutoTreasureMaps.Core.Game;
using AutoTreasureMaps.Core.Game.Player;
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
// opens there is read afresh, so a sold listing only costs the trip, and a world whose listings are too old is not
// travelled to. Every tier is asked for in one request, made once for the purchase however many worlds it visits.
internal sealed partial class MarketBuyTask
{
    // Worlds tried after the first whose board had nothing under the cap after all.
    private const int MaxWorldHops = 2;
    private const int PriceQueryTimeoutMs = 15_000;

    private readonly HashSet<uint> excludedWorlds = [];
    private Dictionary<uint, ItemListings>? prices;

    // Whether this purchase may visit other worlds at all. A purchase outside a run has nothing to travel back after it,
    // and in a party a visit would leave the party behind while the dig waits on members who are on another world.
    private bool CanVisitWorlds(Configuration configuration, bool log = false)
    {
        if (!configuration.MarketCrossWorld)
        {
            return false;
        }

        var reason = session is null ? "a purchase outside a run stays on this world"
            : !LifestreamIPC.Instance.IsAvailable ? "cross-world buying is on but Lifestream is not loaded; buying on this world"
            : PartyHop.Away ? "the party waits on this world for the leader to buy and invite it back, so buying here"
            : PartyPresence.InParty() && !(PartyPresence.LocalParty() && PartyPresence.LeadsTheParty()) ? "in a party it does not lead, so buying on this world instead of visiting another"
            : null;
        if (reason is not null && log)
        {
            Diag($"Market: {reason}.");
        }

        return reason is null;
    }

    // The world travelled to for this visit, or 0 when the run stayed where it was. throttledOn names the world being
    // left because its board kept asking to wait; the chat says so only once the run really travels.
    private async Task<uint> MoveToCheapestWorld(Configuration configuration, List<TreasureTier> tiers, uint throttledOn = 0)
    {
        if (!CanVisitWorlds(configuration, log: true))
        {
            return 0;
        }

        var here = Worlds.Current;
        var dataCenter = Worlds.DataCenterOf(here);
        if (here == 0 || dataCenter.Length == 0)
        {
            return 0;
        }

        prices ??= await QueryPrices(dataCenter, tiers);
        if (prices is null)
        {
            return 0;
        }

        var spendable = MarketRules.Spendable(configuration, session);
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < tiers.Count; index++)
        {
            var tier = tiers[index];
            if (!prices.TryGetValue(tier.TimewornItemId, out var item))
            {
                Diag($"Market: Universalis has nothing on {tier.Name} in {dataCenter}.");
                continue;
            }

            var listings = item.Listings;
            var cap = MarketRules.CapOf(configuration, tier.Rank);
            // Universalis answers for the data centre asked, so every world it lists is one Lifestream can visit; the CN
            // World sheet marks no world public, so the sheet is not asked. A world Lifestream refuses is left out.
            // A world whose listings were last uploaded too long ago is not travelled to either, as they are likely sold.
            var refused = new HashSet<uint>();
            var allowed = new HashSet<uint>();
            var stale = new HashSet<uint>();
            bool Elsewhere(uint worldId)
            {
                if (worldId == here || excludedWorlds.Contains(worldId) || refused.Contains(worldId) || stale.Contains(worldId))
                {
                    return false;
                }

                if (!item.Fresh(worldId, now))
                {
                    stale.Add(worldId);
                    return false;
                }

                if (allowed.Contains(worldId) || LifestreamIPC.Instance.CanVisit(Worlds.NameOf(worldId)))
                {
                    allowed.Add(worldId);
                    return true;
                }

                refused.Add(worldId);
                return false;
            }

            var cheapest = Cheapest(listings, cap, spendable, Elsewhere);
            // A world whose board was already read empty, or that kept asking to wait, has nothing to buy, whatever
            // Universalis still lists for it.
            var cheapestHere = excludedWorlds.Contains(here) ? null : Cheapest(listings, cap, spendable, worldId => worldId == here);
            if (cheapest is null && cheapestHere is null)
            {
                var lowest = listings.Count == 0 ? "none" : $"{listings.Min(listing => listing.UnitPrice)} gil";
                Diag($"Market: Universalis shows no {tier.Name} under {cap} gil that can be bought in {dataCenter} ({listings.Count} listing(s), lowest {lowest}, {refused.Count} world(s) Lifestream would not visit, {stale.Count} with listings too old).");
                continue;
            }

            if (cheapest is not { } best || (cheapestHere is { } local && local.UnitPrice - best.UnitPrice < configuration.MarketCrossWorldMinSavings))
            {
                Diag($"Market: a trip for {tier.Name} saves too little ({Describe(cheapestHere)} on {Worlds.NameOf(here)}, {Describe(cheapest)} elsewhere); staying.");
                return 0;
            }

            var name = Worlds.NameOf(best.WorldId);
            Diag($"Market: Universalis lists {tier.Name} at {best.UnitPrice} gil on {name}, {Describe(cheapestHere)} on {Worlds.NameOf(here)}.");
            if (throttledOn != 0)
            {
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Market.ChatThrottledWorld, Worlds.NameOf(throttledOn))}");
            }

            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Market.ChatCheapestWorld, tier.Name, name, best.UnitPrice.ToString("N0", Loc.Culture))}");
            CloseBoardNow();
            // Visiting from the board's own city aetheryte lands the character next to the board it is about to use.
            var gateway = MarketBoards.Resolve(configuration.MarketBoard).AetheryteId;
            if (!await TakePartyAlong(best.WorldId))
            {
                return 0;
            }

            return await ChangeWorld(best.WorldId, "market-world", gateway) ? best.WorldId : 0;
        }

        return 0;
    }

    // Null when Universalis could not be asked; the run then buys where it stands.
    private async Task<Dictionary<uint, ItemListings>?> QueryPrices(string dataCenter, List<TreasureTier> tiers)
    {
        Status = Loc.T(L.Status.ComparingPrices, dataCenter);
        var itemIds = tiers.Select(static tier => tier.TimewornItemId).Distinct().ToList();
        var request = Universalis.Listings(dataCenter, itemIds, CancelToken);
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
