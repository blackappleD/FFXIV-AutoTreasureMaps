using FFXIVClientStructs.FFXIV.Client.Game;
using System.Collections.Immutable;

namespace AutoTreasureMaps.Core.Treasure;

// Item counts across the bags, the crystal pouch and the currency pouch, taken before a coffer opens and again once its
// loot has settled; the difference is what the coffer gave. Gil is the currency pouch's item 1. A container that is not
// loaded reads as empty, which only happens around a zone change. A high-quality copy counts under its item id plus
// HighQualityOffset, as the game itself addresses it, so a coffer's normal-quality loot stands apart from high-quality
// copies the player already had; Gains folds the two back into one item. Read only on the framework thread.
internal static unsafe class BagSnapshot
{
    public const uint HighQualityOffset = 1_000_000;

    private const uint GilItemId = 1;

    private static readonly InventoryType[] Containers =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.Crystals, InventoryType.Currency,
    ];

    public static Dictionary<uint, int> Capture()
    {
        var counts = new Dictionary<uint, int>();
        var inventory = InventoryManager.Instance();
        if (inventory is null)
        {
            return counts;
        }

        for (var containerIndex = 0; containerIndex < Containers.Length; containerIndex++)
        {
            var container = inventory->GetInventoryContainer(Containers[containerIndex]);
            if (container is null || !container->IsLoaded)
            {
                continue;
            }

            for (var slotIndex = 0; slotIndex < container->Size; slotIndex++)
            {
                var slot = container->GetInventorySlot(slotIndex);
                if (slot is null || slot->ItemId == 0)
                {
                    continue;
                }

                var key = Key(slot->ItemId, slot->IsHighQuality());
                counts[key] = counts.GetValueOrDefault(key) + slot->Quantity;
            }
        }

        return counts;
    }

    // Order-independent, so polling it to see whether the loot has stopped arriving allocates nothing.
    public static long Fingerprint()
    {
        var inventory = InventoryManager.Instance();
        if (inventory is null)
        {
            return 0;
        }

        long fingerprint = 0;
        for (var containerIndex = 0; containerIndex < Containers.Length; containerIndex++)
        {
            var container = inventory->GetInventoryContainer(Containers[containerIndex]);
            if (container is null || !container->IsLoaded)
            {
                continue;
            }

            for (var slotIndex = 0; slotIndex < container->Size; slotIndex++)
            {
                var slot = container->GetInventorySlot(slotIndex);
                if (slot is not null && slot->ItemId != 0)
                {
                    fingerprint += (slot->ItemId * 2_654_435_761L) ^ slot->Quantity;
                }
            }
        }

        return fingerprint;
    }

    public static uint Key(uint itemId, bool highQuality) => highQuality ? itemId + HighQualityOffset : itemId;

    public static uint ItemIdOf(uint key) => key >= HighQualityOffset ? key - HighQualityOffset : key;

    public static bool IsHighQualityKey(uint key) => key >= HighQualityOffset;

    // Only gains count: an item spent meanwhile, a potion in the fight, is not the coffer's doing. Each quality gains on
    // its own, so a high-quality potion drunk does not hide normal-quality loot of the same item.
    public static (int Gil, ImmutableArray<ItemGain> Items) Gains(Dictionary<uint, int> before, Dictionary<uint, int> after)
    {
        var gil = 0;
        var byItem = new Dictionary<uint, int>();
        foreach (var (key, gained) in KeyedGains(before, after))
        {
            if (key == GilItemId)
            {
                gil = gained;
                continue;
            }

            var itemId = ItemIdOf(key);
            byItem[itemId] = byItem.GetValueOrDefault(itemId) + gained;
        }

        var items = ImmutableArray.CreateBuilder<ItemGain>(byItem.Count);
        foreach (var (itemId, gained) in byItem)
        {
            items.Add(new ItemGain(itemId, gained));
        }

        return (gil, items.ToImmutable());
    }

    // The gains by quality, keyed as the snapshot is, for the cleanup, which discards one quality without the other.
    public static List<(uint Key, int Gained)> KeyedGains(Dictionary<uint, int> before, Dictionary<uint, int> after)
    {
        var gains = new List<(uint Key, int Gained)>();
        foreach (var (key, count) in after)
        {
            var gained = count - before.GetValueOrDefault(key);
            if (gained > 0)
            {
                gains.Add((key, gained));
            }
        }

        return gains;
    }
}
