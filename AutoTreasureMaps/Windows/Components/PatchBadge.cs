using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Components;

// The patch label the FFXIV wiki puts beside a map, in the wiki's own label-patch-N colours: one style per expansion,
// a three-stop gradient for Endwalker and Dawntrail. Labels are cached per language, so drawing allocates nothing.
internal static class PatchBadge
{
    private const float PadX = 4f;
    private const float PadY = 1f;
    private const float Rounding = 3f;

    private readonly record struct Style(Vector4 Border, Vector4 Left, Vector4 Middle, Vector4 Right, Vector4 Text, Vector4 Shadow);

    private static readonly Vector4 White = Rgb(0xffffff);
    private static readonly Vector4 NoShadow = Vector4.Zero;

    // Indexed by major version minus one.
    private static readonly Style[] styles =
    [
        Solid(0x555555, 0x282828, Rgb(0xdddddd)),
        Solid(0x909090, 0x666666, White),
        Solid(0x90b3fd, 0x4c7ee8, White),
        Solid(0xe4667b, 0xa22a3e, White),
        Solid(0x7f11af, 0x2e1d4a, White),
        new(Rgb(0x4296c1), Rgb(0x3d4e99), Rgb(0x3584ad), Rgb(0xcc7a2a), White, Rgb(0x16395c)),
        new(Rgb(0xe5c45c), Rgb(0x66531c), Rgb(0x9b853f), Rgb(0x66531c), White, Rgb(0x292308)),
    ];

    private static readonly Dictionary<GamePatch, string> labels = [];
    private static LanguageInfo? labelLanguage;

    public static float Draw(ImDrawListPtr drawList, GamePatch patch, float leftX, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var style = styles[Math.Clamp(patch.Major, 1, styles.Length) - 1];
        var label = Label(patch);
        using (Fonts.PushCaption())
        {
            var labelSize = TextDraw.Measure(label);
            var padX = PadX * scale;
            var padY = PadY * scale;
            var rounding = Rounding * scale;
            var min = new Vector2(leftX, midY - labelSize.Y * 0.5f - padY);
            var max = new Vector2(leftX + labelSize.X + padX * 2f, midY + labelSize.Y * 0.5f + padY);
            if (style.Left == style.Right && style.Left == style.Middle)
            {
                Paint.Fill(drawList, min, max, style.Left, rounding);
            }
            else
            {
                var middleX = (min.X + max.X) * 0.5f;
                Paint.GradientH(drawList, min, max with { X = middleX }, style.Left, style.Middle, rounding, ImDrawFlags.RoundCornersLeft);
                Paint.GradientH(drawList, min with { X = middleX }, max, style.Middle, style.Right, rounding, ImDrawFlags.RoundCornersRight);
            }

            Paint.Stroke(drawList, min, max, style.Border, rounding);
            var textPos = new Vector2(min.X + padX, midY - labelSize.Y * 0.5f);
            if (style.Shadow.W > 0f)
            {
                TextDraw.At(label, textPos + new Vector2(scale, scale), style.Shadow);
            }

            TextDraw.At(label, textPos, style.Text);
            return max.X - min.X;
        }
    }

    private static string Label(GamePatch patch)
    {
        if (!ReferenceEquals(labelLanguage, Loc.Current))
        {
            labels.Clear();
            labelLanguage = Loc.Current;
        }

        if (!labels.TryGetValue(patch, out var label))
        {
            label = Loc.T(L.Maps.PatchBadge, patch.ToString());
            labels[patch] = label;
        }

        return label;
    }

    private static Style Solid(uint border, uint fill, Vector4 text)
        => new(Rgb(border), Rgb(fill), Rgb(fill), Rgb(fill), text, NoShadow);

    private static Vector4 Rgb(uint hex)
        => new(((hex >> 16) & 0xff) / 255f, ((hex >> 8) & 0xff) / 255f, (hex & 0xff) / 255f, 1f);
}
