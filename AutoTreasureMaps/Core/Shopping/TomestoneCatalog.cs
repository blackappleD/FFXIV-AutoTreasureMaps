using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Shopping;

internal readonly record struct TomestoneCurrency(uint ItemId, string Name, uint IconId);

internal readonly record struct TomestoneOffer(uint ItemId, string Name, uint IconId, uint Cost);

// The tomestones of the current patch and what their vendors sell, read from the game data once. A patch renames the
// non-Poetics tomestones, so the currencies come from TomestonesItem rather than fixed ids. Only listings that a vendor
// NPC opens and that cost that tomestone alone are offered, which is what GatherBuddy Reborn can buy.
internal static class TomestoneCatalog
{
    // SpecialShop costs below this are an index into the TomestonesItem slots rather than an item id.
    private const uint TomestoneSlotLimit = 10;
    // TomestonesItem slot 1 is Poetics, 2 the current uncapped tomestone and 3 the weekly capped one; a higher slot holds
    // the previous patch's tomestone, which only trades in.
    private const uint CurrentTomestoneSlots = 3;
    // How a shop numbers its costs, after AllaganLib's SpecialShopListing.ConvertCurrencyId: with currency types 2 and 4 a
    // cost under 10 is a tomestone slot; with type 16, and in two shops of their own, a cost up to 28 is the index of a
    // scrip, seal or other currency instead, so it is never read as the item of the same id.
    private const byte TomestoneCurrencyType = 2;
    private const byte TomestoneOrCurrencyType = 4;
    private const byte IndexedCurrencyType = 16;
    private const uint IndexedCurrencyMax = 28;
    private const uint IndexedCurrencyExempt = 25;
    // Set on a cost read as a currency index, which no item id reaches.
    private const uint IndexedCurrencyFlag = 0x8000_0000;

    private static readonly HashSet<uint> TomestoneSlotShops = [1770446, 1770699, 1770803];
    private static readonly HashSet<uint> IndexedCurrencyShops = [1770637, 1770638];

    private static TomestoneCurrency[]? currencies;
    private static Task<Dictionary<uint, TomestoneOffer[]>>? offers;

    public static IReadOnlyList<TomestoneCurrency> Currencies => currencies ??= BuildCurrencies();

    // Every vendor NPC and special shop is read to find the offers, which takes a moment, so it runs off the frame and
    // the settings show the lists once it is done.
    public static bool OffersReady
    {
        get
        {
            offers ??= Task.Run(BuildOffers);
            return offers.IsCompletedSuccessfully;
        }
    }

    public static IReadOnlyList<TomestoneOffer> OffersFor(uint currencyItemId)
    {
        if (!OffersReady)
        {
            return [];
        }

        return offers!.Result.TryGetValue(currencyItemId, out var list) ? list : [];
    }

    public static bool TryGetCurrency(uint currencyItemId, out TomestoneCurrency currency)
    {
        var all = Currencies;
        for (var index = 0; index < all.Count; index++)
        {
            if (all[index].ItemId == currencyItemId)
            {
                currency = all[index];
                return true;
            }
        }

        currency = default;
        return false;
    }

    private static TomestoneCurrency[] BuildCurrencies()
    {
        var items = Svc.Data.GetExcelSheet<Item>();
        var found = new List<(uint Slot, TomestoneCurrency Currency)>();
        foreach (var row in Svc.Data.GetExcelSheet<TomestonesItem>())
        {
            if (row.Tomestones.RowId is 0 or > CurrentTomestoneSlots || row.CurrencyInventorySlot == 0 || row.Item.RowId == 0)
            {
                continue;
            }

            if (!items.TryGetRow(row.Item.RowId, out var item))
            {
                continue;
            }

            found.Add((row.Tomestones.RowId, new TomestoneCurrency(item.RowId, item.Name.ExtractText(), item.Icon)));
        }

        found.Sort((left, right) => left.Slot.CompareTo(right.Slot));
        var result = new TomestoneCurrency[found.Count];
        for (var index = 0; index < found.Count; index++)
        {
            result[index] = found[index].Currency;
        }

        return result;
    }

    private static Dictionary<uint, TomestoneOffer[]> BuildOffers()
    {
        var slotItems = new Dictionary<uint, uint>();
        foreach (var row in Svc.Data.GetExcelSheet<TomestonesItem>())
        {
            if (row.Tomestones.RowId != 0 && row.CurrencyInventorySlot != 0)
            {
                slotItems.TryAdd(row.Tomestones.RowId, row.Item.RowId);
            }
        }

        var wanted = new HashSet<uint>();
        foreach (var currency in Currencies)
        {
            wanted.Add(currency.ItemId);
        }

        var items = Svc.Data.GetExcelSheet<Item>();
        var reachable = ReachableSpecialShops();
        var byCurrency = new Dictionary<uint, Dictionary<uint, TomestoneOffer>>();
        foreach (var shop in Svc.Data.GetExcelSheet<SpecialShop>())
        {
            if (!reachable.Contains(shop.RowId))
            {
                continue;
            }

            foreach (var entry in shop.Item)
            {
                if (!TrySingleCost(shop.RowId, entry, shop.UseCurrencyType, slotItems, out var currencyItemId, out var cost) || !wanted.Contains(currencyItemId))
                {
                    continue;
                }

                foreach (var received in entry.ReceiveItems)
                {
                    var itemId = received.Item.RowId;
                    if (itemId == 0 || !items.TryGetRow(itemId, out var item))
                    {
                        continue;
                    }

                    var name = item.Name.ExtractText();
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    if (!byCurrency.TryGetValue(currencyItemId, out var list))
                    {
                        list = [];
                        byCurrency[currencyItemId] = list;
                    }

                    // An item sold in several shops is listed once, at its lowest price.
                    if (!list.TryGetValue(itemId, out var known) || cost < known.Cost)
                    {
                        list[itemId] = new TomestoneOffer(itemId, name, item.Icon, cost);
                    }
                }
            }
        }

        var result = new Dictionary<uint, TomestoneOffer[]>();
        foreach (var (currencyItemId, list) in byCurrency)
        {
            var sorted = list.Values.ToArray();
            Array.Sort(sorted, (left, right) => string.CompareOrdinal(left.Name, right.Name));
            result[currencyItemId] = sorted;
        }

        return result;
    }

    // A listing that also asks for a second currency or item (a gear upgrade, say) is left out.
    private static bool TrySingleCost(uint shopId, SpecialShop.ItemStruct entry, byte useCurrencyType, Dictionary<uint, uint> slotItems, out uint currencyItemId, out uint cost)
    {
        currencyItemId = 0;
        cost = 0;
        foreach (var itemCost in entry.ItemCosts)
        {
            var raw = itemCost.ItemCost.RowId;
            if (raw == 0 || itemCost.CurrencyCost == 0)
            {
                continue;
            }

            var resolved = Resolve(shopId, raw, useCurrencyType, slotItems);

            if (currencyItemId != 0 && currencyItemId != resolved)
            {
                return false;
            }

            currencyItemId = resolved;
            cost = itemCost.CurrencyCost;
        }

        return currencyItemId != 0;
    }

    private static uint Resolve(uint shopId, uint raw, byte useCurrencyType, Dictionary<uint, uint> slotItems)
    {
        var slot = raw < TomestoneSlotLimit && slotItems.TryGetValue(raw, out var slotItem) ? slotItem : 0;
        if (slot != 0 && TomestoneSlotShops.Contains(shopId))
        {
            return slot;
        }

        var indexed = raw <= IndexedCurrencyMax
            && (IndexedCurrencyShops.Contains(shopId) || (useCurrencyType == IndexedCurrencyType && raw != IndexedCurrencyExempt));
        if (indexed)
        {
            return raw | IndexedCurrencyFlag;
        }

        if (slot != 0 && useCurrencyType is TomestoneCurrencyType or TomestoneOrCurrencyType)
        {
            return slot;
        }

        return raw;
    }

    // The special shops some vendor NPC opens: straight from its menu, through a topic list or a pre-handler, a FATE shop,
    // or a page of an inclusion shop.
    private static HashSet<uint> ReachableSpecialShops()
    {
        var specialShops = Svc.Data.GetExcelSheet<SpecialShop>();
        var topics = Svc.Data.GetExcelSheet<TopicSelect>();
        var preHandlers = Svc.Data.GetExcelSheet<PreHandler>();
        var inclusionShops = Svc.Data.GetExcelSheet<InclusionShop>();
        var reachable = new HashSet<uint>();

        void AddInclusion(uint inclusionShopId)
        {
            if (!inclusionShops.TryGetRow(inclusionShopId, out var inclusion))
            {
                return;
            }

            foreach (var category in inclusion.Category)
            {
                if (category.RowId == 0 || !category.IsValid)
                {
                    continue;
                }

                var series = category.Value.InclusionShopSeries;
                if (series.RowId == 0 || !series.IsValid)
                {
                    continue;
                }

                foreach (var page in series.Value)
                {
                    if (page.SpecialShop.RowId != 0)
                    {
                        reachable.Add(page.SpecialShop.RowId);
                    }
                }
            }
        }

        void AddTarget(uint id, int depth)
        {
            if (id == 0 || depth > 2)
            {
                return;
            }

            if (specialShops.HasRow(id))
            {
                reachable.Add(id);
            }
            else if (inclusionShops.HasRow(id))
            {
                AddInclusion(id);
            }
            else if (topics.TryGetRow(id, out var topic))
            {
                foreach (var shop in topic.Shop)
                {
                    AddTarget(shop.RowId, depth + 1);
                }
            }
            else if (preHandlers.TryGetRow(id, out var preHandler))
            {
                AddTarget(preHandler.Target.RowId, depth + 1);
            }
        }

        foreach (var npc in Svc.Data.GetExcelSheet<ENpcBase>())
        {
            foreach (var data in npc.ENpcData)
            {
                AddTarget(data.RowId, 0);
            }
        }

        foreach (var fateShop in Svc.Data.GetExcelSheet<FateShop>())
        {
            foreach (var shop in fateShop.SpecialShop)
            {
                if (shop.RowId != 0)
                {
                    reachable.Add(shop.RowId);
                }
            }
        }

        return reachable;
    }
}
