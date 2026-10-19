using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoTreasureMaps.Windows;

// An item's own icon from the game's data, as the bags show it. The texture loads in the background, so a frame or two
// may draw nothing in its place.
internal static class ItemIcon
{
    public static uint IdOf(uint itemId)
        => Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } row ? row.Icon : 0u;

    public static void Draw(ImDrawListPtr dl, uint iconId, Vector2 min, Vector2 max, float rounding, bool highQuality = false)
    {
        if (iconId == 0)
        {
            return;
        }

        var texture = Svc.Texture.GetFromGameIcon(new GameIconLookup(iconId, itemHq: highQuality)).GetWrapOrEmpty();
        dl.AddImageRounded(texture.Handle, min, max, Vector2.Zero, Vector2.One, Paint.Col(Vector4.One), rounding, ImDrawFlags.RoundCornersAll);
    }
}
