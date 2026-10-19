using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Collections.Frozen;
using System.Collections.Immutable;

namespace AutoTreasureMaps.Core.Treasure;

// How loot is counted. Shards, crystals and clusters (items 2 to 19) come by the hundred and would drown the item
// count, so they are left out of it. Tomestones are counted on their own, like gil; the game's TomestonesItem sheet
// names every kind.
internal static class LootKinds
{
    private const uint FirstCrystalItemId = 2;
    private const uint LastCrystalItemId = 19;

    private static FrozenSet<uint>? tomestoneItemIds;

    public static bool IsCrystal(uint itemId) => itemId is >= FirstCrystalItemId and <= LastCrystalItemId;

    public static bool IsTomestone(uint itemId) => TomestoneItemIds.Contains(itemId);

    // Every item gained but crystals and tomestones.
    public static int ItemCount(ImmutableArray<ItemGain> gains)
    {
        var total = 0;
        var items = gains.IsDefault ? [] : gains.AsSpan();
        for (var index = 0; index < items.Length; index++)
        {
            if (!IsCrystal(items[index].ItemId) && !IsTomestone(items[index].ItemId))
            {
                total += items[index].Quantity;
            }
        }

        return total;
    }

    public static int TomestoneCount(ImmutableArray<ItemGain> gains)
    {
        var total = 0;
        var items = gains.IsDefault ? [] : gains.AsSpan();
        for (var index = 0; index < items.Length; index++)
        {
            if (IsTomestone(items[index].ItemId))
            {
                total += items[index].Quantity;
            }
        }

        return total;
    }

    private static FrozenSet<uint> TomestoneItemIds => tomestoneItemIds ??= ReadTomestoneItemIds();

    private static FrozenSet<uint> ReadTomestoneItemIds()
    {
        var ids = new HashSet<uint>();
        foreach (var row in Svc.Data.GetExcelSheet<TomestonesItem>())
        {
            if (row.Item.RowId != 0)
            {
                ids.Add(row.Item.RowId);
            }
        }

        return ids.ToFrozenSet();
    }
}
