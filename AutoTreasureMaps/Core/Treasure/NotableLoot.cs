using AutoTreasureMaps.Core.Localization;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Globalization;

namespace AutoTreasureMaps.Core.Treasure;

// The game colours an item's name by its rarity, and a name above plain white is what a player notices in the loot:
// crystals, everyday materials and gil never are.
internal static class NotableLoot
{
    private const byte PlainRarity = 1;

    public static bool IsNotable(uint itemId)
        => Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } item && item.Rarity > PlainRarity;

    public static string Name(uint itemId)
    {
        var name = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? itemId.ToString(CultureInfo.InvariantCulture) : GameGlyphs.Cover(name);
    }
}
