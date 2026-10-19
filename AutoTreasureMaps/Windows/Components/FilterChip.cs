using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Components;

internal static class FilterChip
{
    private const float PadX = 11f;
    private const float DotRadius = 3.5f;
    private const float Gap = 7f;
    private const float IconPadX = 5f;
    private const float IconInset = 3f;
    private const float IconRounding = 4f;
    private const float IconDimAlpha = 0.45f;

    // With an item icon the chip leads with that icon in place of the dot, sized to the chip's height.
    public static float Width(string label, string? count, FontAwesomeIcon? trailingIcon = null, uint iconId = 0, float height = 0f)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var lead = iconId != 0 ? (IconPadX * scale) + IconSide(height) : DotRadius * 2f * scale + PadX * scale;
        var width = lead + PadX * scale + Gap * scale + TextDraw.Measure(label).X;
        if (count is not null)
        {
            width += Gap * scale + TextDraw.Measure(count).X;
        }

        if (trailingIcon is { } glyph)
        {
            width += Gap * scale + TextDraw.IconSize(glyph).X;
        }

        return width;
    }

    public static bool Draw(string id, string label, string? count, Vector4 accent, bool active, float height,
        FontAwesomeIcon? trailingIcon = null, string? tooltip = null, uint iconId = 0)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(Width(label, count, trailingIcon, iconId, height), height);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var hit = Hit.Area(id, size);
        var hover = Motion.Hover(Motion.Key(id), hit.Hovered);
        var on = Motion.Approach(Motion.Key(id, 1), active ? 1f : 0f, 18f);
        var dl = ImGui.GetWindowDrawList();

        var fill = Vector4.Lerp(Styling.WithAlpha(Styling.Surface1, 0.5f + 0.4f * hover), Styling.WithAlpha(accent, 0.16f + 0.08f * hover), on);
        var border = Vector4.Lerp(Styling.WithAlpha(Styling.BorderDim, 0.45f + 0.3f * hover), Styling.WithAlpha(accent, 0.5f + 0.25f * hover), on);
        Paint.Pill(dl, origin, end, fill, border);

        var midY = origin.Y + size.Y * 0.5f;
        float x;
        if (iconId != 0)
        {
            // Lit while the chip is on, faded while it is off, as the dot is filled or hollow.
            var side = IconSide(height);
            x = origin.X + IconPadX * scale;
            var iconMin = new Vector2(x, midY - side * 0.5f);
            ItemIcon.Draw(dl, iconId, iconMin, iconMin + new Vector2(side, side), IconRounding * scale, alpha: IconDimAlpha + (1f - IconDimAlpha) * MathF.Max(on, hover * 0.6f));
            x += side + Gap * scale;
        }
        else
        {
            x = origin.X + PadX * scale;
            var radius = DotRadius * scale;
            var dotCenter = new Vector2(x + radius, midY);
            if (active)
            {
                dl.AddCircleFilled(dotCenter, radius, Paint.Col(accent));
            }
            else
            {
                dl.AddCircle(dotCenter, radius, Paint.Col(Styling.WithAlpha(accent, 0.7f)), 0, 1.2f * scale);
            }

            x += radius * 2f + Gap * scale;
        }

        var labelColor = Vector4.Lerp(Vector4.Lerp(Styling.TextDim, Styling.TextSecondary, hover), Styling.TextStrong, on);
        var labelSize = TextDraw.Measure(label);
        TextDraw.At(label, new Vector2(x, midY - labelSize.Y * 0.5f), labelColor);
        x += labelSize.X;

        if (count is not null)
        {
            x += Gap * scale;
            var countSize = TextDraw.Measure(count);
            TextDraw.At(count, new Vector2(x, midY - countSize.Y * 0.5f), Vector4.Lerp(Styling.TextMuted, Styling.Lighten(accent, 0.2f), on));
            x += countSize.X;
        }

        if (trailingIcon is { } glyph)
        {
            x += Gap * scale;
            var iconSize = TextDraw.IconSize(glyph);
            TextDraw.Icon(glyph, new Vector2(x, midY - iconSize.Y * 0.5f), Vector4.Lerp(Styling.TextDim, Styling.TextStrong, hover));
        }

        if (hit.Hovered && tooltip is not null)
        {
            Tooltip.Show(tooltip);
        }

        return hit.Clicked;
    }

    private static float IconSide(float height)
        => MathF.Max(1f, height - IconInset * 2f * ImGuiHelpers.GlobalScale);
}
