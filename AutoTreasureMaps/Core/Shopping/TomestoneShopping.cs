using AutoTreasureMaps.Core.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoTreasureMaps.Core.Shopping;

internal static class TomestoneShopping
{
    // GatherBuddy counts these to tell how many of an item are held, so a count read here lines up with its targets.
    private static readonly InventoryType[] HeldContainers =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.EquippedItems,
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody,
        InventoryType.ArmoryHands, InventoryType.ArmoryWaist, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar, InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
        InventoryType.ArmorySoulCrystal,
    ];

    public const int MaxThreshold = 2_000;
    public const int MaxQuantity = 999;

    // The GatherBuddy list a tomestone's items are written to; one list per tomestone, so each stays its own.
    public static string ListName(in TomestoneCurrency currency) => $"TreasureHelper · {currency.Name}";

    public static bool Available => GatherBuddyIPC.Instance.IsAvailable;

    public static TomestoneShoppingList ListFor(Configuration configuration, uint currencyItemId)
    {
        var lists = configuration.TomestoneShoppingLists;
        for (var index = 0; index < lists.Count; index++)
        {
            if (lists[index].CurrencyItemId == currencyItemId)
            {
                return lists[index];
            }
        }

        var created = new TomestoneShoppingList { CurrencyItemId = currencyItemId };
        lists.Add(created);
        return created;
    }

    // The lists of the current tomestones that have an item to buy; a list kept from an older patch's tomestone is left out.
    public static List<(TomestoneCurrency Currency, TomestoneShoppingList List)> ActiveLists(Configuration configuration)
    {
        var active = new List<(TomestoneCurrency, TomestoneShoppingList)>();
        var lists = configuration.TomestoneShoppingLists;
        for (var index = 0; index < lists.Count; index++)
        {
            var list = lists[index];
            if (list.Items.Count > 0 && TomestoneCatalog.TryGetCurrency(list.CurrencyItemId, out var currency))
            {
                active.Add((currency, list));
            }
        }

        return active;
    }

    // Any one tomestone at or over its threshold starts the trip.
    public static bool TryFindTrigger(Configuration configuration, out TomestoneCurrency currency, out int held)
    {
        foreach (var (candidate, list) in ActiveLists(configuration))
        {
            if (list.Threshold <= 0)
            {
                continue;
            }

            var count = HeldCount(candidate.ItemId);
            if (count >= list.Threshold)
            {
                currency = candidate;
                held = count;
                return true;
            }
        }

        currency = default;
        held = 0;
        return false;
    }

    public static unsafe int HeldCount(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        return inventory is null ? 0 : inventory->GetInventoryItemCount(itemId);
    }

    // What the bags, armoury and gear hold of an item, NQ and HQ together.
    public static unsafe uint HeldItemCount(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        if (inventory is null)
        {
            return 0;
        }

        var total = 0u;
        for (var containerIndex = 0; containerIndex < HeldContainers.Length; containerIndex++)
        {
            var container = inventory->GetInventoryContainer(HeldContainers[containerIndex]);
            if (container is null || !container->IsLoaded)
            {
                continue;
            }

            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item is not null && item->ItemId == itemId)
                {
                    total += (uint)item->Quantity;
                }
            }
        }

        return total;
    }

    // GatherBuddy buys up to a count held, so each item's count is what is held now plus the quantity to buy.
    public static (uint ItemId, uint TargetQuantity)[] Targets(TomestoneShoppingList list)
    {
        var targets = new List<(uint, uint)>(list.Items.Count);
        for (var index = 0; index < list.Items.Count; index++)
        {
            var item = list.Items[index];
            if (item.ItemId == 0 || item.Quantity <= 0)
            {
                continue;
            }

            targets.Add((item.ItemId, HeldItemCount(item.ItemId) + (uint)item.Quantity));
        }

        return targets.ToArray();
    }
}
