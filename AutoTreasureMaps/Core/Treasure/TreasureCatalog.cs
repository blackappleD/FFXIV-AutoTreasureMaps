using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Collections.Frozen;

namespace AutoTreasureMaps.Core.Treasure;

// Built once from the game's TreasureHuntRank sheet. A map item reaches its tier only through that sheet's item columns:
// Item.AdditionalData is 0 for some special maps although their rank row points at them.
internal sealed class TreasureCatalog
{
    private const byte UnderseaTexture = 4;
    private const byte NoTier = byte.MaxValue;

    private readonly TreasureTier[] tiers;
    private readonly byte[] indexByRank;
    private readonly FrozenDictionary<uint, byte> indexByTimewornItem;
    private readonly FrozenDictionary<uint, byte> indexByKeyItem;

    private TreasureCatalog(TreasureTier[] tiers)
    {
        this.tiers = tiers;
        var highestRank = 0;
        for (var index = 0; index < tiers.Length; index++)
        {
            highestRank = Math.Max(highestRank, tiers[index].Rank);
        }

        indexByRank = new byte[highestRank + 1];
        Array.Fill(indexByRank, NoTier);
        var timewornItems = new Dictionary<uint, byte>(tiers.Length);
        var keyItems = new Dictionary<uint, byte>(tiers.Length * 2);
        for (var index = 0; index < tiers.Length; index++)
        {
            var tier = tiers[index];
            var slot = (byte)index;
            indexByRank[tier.Rank] = slot;
            AddItem(timewornItems, tier.TimewornItemId, slot);
            AddItem(keyItems, tier.KeyItemId, slot);
            AddItem(keyItems, tier.PortalKeyItemId, slot);
        }

        indexByTimewornItem = timewornItems.ToFrozenDictionary();
        indexByKeyItem = keyItems.ToFrozenDictionary();
    }

    public ReadOnlySpan<TreasureTier> Tiers => tiers;

    public static TreasureCatalog Build()
    {
        var rankSheet = Svc.Data.GetExcelSheet<TreasureHuntRank>();
        var itemSheet = Svc.Data.GetExcelSheet<Item>();
        var built = new List<TreasureTier>(rankSheet.Count);
        for (var rowIndex = 0; rowIndex < rankSheet.Count; rowIndex++)
        {
            var row = rankSheet.GetRowAt(rowIndex);
            // Row 0 repeats row 1, and the client's own lookup starts at row 1.
            if (row.RowId == 0 || row.RowId >= NoTier || row.ItemName.RowId == 0)
            {
                continue;
            }

            if (itemSheet.GetRowOrDefault(row.ItemName.RowId) is not { } item)
            {
                continue;
            }

            var (spotCount, expansion) = SurveySpots(row.RowId);
            built.Add(new TreasureTier(
                GameGlyphs.Cover(item.Name.ExtractText()),
                row.ItemName.RowId,
                row.KeyItemName.RowId,
                row.InstanceMap.RowId,
                (byte)row.RowId,
                row.MaxPartySize,
                spotCount,
                expansion,
                row.TreasureHuntTexture == UnderseaTexture,
                !item.IsUntradable));
        }

        var catalog = new TreasureCatalog(built.ToArray());
        RunLog.Debug($"Treasure catalog: {catalog.tiers.Length} map tiers read from the game data.");
        return catalog;
    }

    public bool TryGetByRank(uint rank, out TreasureTier tier)
        => TryGetAt(rank < (uint)indexByRank.Length ? indexByRank[rank] : NoTier, out tier);

    public bool TryGetByTimewornItem(uint itemId, out TreasureTier tier)
        => TryGetAt(indexByTimewornItem.TryGetValue(itemId, out var index) ? index : NoTier, out tier);

    // The tier's position in Tiers, for callers that keep one value per tier in a parallel array.
    public bool TryGetTimewornIndex(uint itemId, out int index)
    {
        if (indexByTimewornItem.TryGetValue(itemId, out var slot))
        {
            index = slot;
            return true;
        }

        index = -1;
        return false;
    }

    // Matches the deciphered map and the thief's map a portal leaves behind, the same columns the client searches.
    public bool TryGetByKeyItem(uint itemId, out TreasureTier tier)
        => TryGetAt(indexByKeyItem.TryGetValue(itemId, out var index) ? index : NoTier, out tier);

    private bool TryGetAt(byte index, out TreasureTier tier)
    {
        if (index == NoTier)
        {
            tier = default;
            return false;
        }

        tier = tiers[index];
        return true;
    }

    // The first row keeps a shared item id, as the client's own search walks the rows in order and stops at the first.
    private static void AddItem(Dictionary<uint, byte> index, uint itemId, byte slot)
    {
        if (itemId == 0)
        {
            return;
        }

        index.TryAdd(itemId, slot);
    }

    // A tier's expansion is the expansion of the zones its spots lie in; every spot of a tier shares one.
    private static (byte Count, ExpansionKind Expansion) SurveySpots(uint rank)
    {
        if (!Svc.Data.GetSubrowExcelSheet<TreasureSpot>().TryGetRow(rank, out var spots))
        {
            return (0, ExpansionKind.ARR);
        }

        var count = 0;
        var expansion = ExpansionKind.ARR;
        for (var spotIndex = 0; spotIndex < spots.Count; spotIndex++)
        {
            var location = spots[spotIndex].Location;
            if (location.RowId == 0)
            {
                continue;
            }

            if (count == 0 && location.ValueNullable?.Territory.ValueNullable is { } territory)
            {
                expansion = (ExpansionKind)Math.Min(territory.ExVersion.RowId, (uint)ExpansionKind.DT);
            }

            count++;
        }

        return ((byte)Math.Min(count, byte.MaxValue), expansion);
    }
}
