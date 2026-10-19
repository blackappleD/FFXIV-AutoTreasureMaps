using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Globalization;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Sections;

// The map log as a table, newest first: number, map, portal, dungeon floor, knockouts, time taken and the rare loot
// laid out flat. The text is built once per change of the log or the language; rows off screen are only measured.
internal sealed class MapLogTable
{
    private const float PadX = 12f;
    private const float RowPad = 8f;
    private const float CellGap = 10f;
    private const float NumberWidth = 44f;
    private const float FlagWidth = 52f;
    private const float DurationWidth = 64f;
    private const float MapShare = 0.3f;
    private const string NoValue = "-";

    private readonly List<Row> rows = [];
    private int builtRevision = -1;
    private LanguageInfo? builtLanguage;

    private readonly record struct Row(string Number, string Map, bool Portal, string Floor, bool Bottom, int Deaths, string Duration, string Loot, DateTime AtUtc);

    public void Draw(MapLog log)
    {
        if (log.Entries.Count == 0)
        {
            var text = Loc.T(L.History.NoMapLog);
            var available = ImGui.GetContentRegionAvail().X;
            TextDraw.Wrapped(text, ImGui.GetCursorScreenPos(), available, Styling.TextMuted);
            ImGui.Dummy(new Vector2(available, TextDraw.MeasureWrapped(text, available).Y));
            return;
        }

        if (log.Revision != builtRevision || !ReferenceEquals(builtLanguage, Loc.Current))
        {
            Build(log);
        }

        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;
        var rowHeight = ImGui.GetTextLineHeight() + RowPad * scale;
        var columns = Columns(ImGui.GetCursorScreenPos().X, width);
        DrawHeader(columns, width, rowHeight);
        var yes = Loc.T(L.History.LogYes);
        for (var index = 0; index < rows.Count; index++)
        {
            DrawRow(rows[index], index, columns, width, rowHeight, yes);
        }
    }

    // The left edge of each column: number, map, portal, floor, knockouts, time, loot; the last value is the right edge.
    private static float[] Columns(float left, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = CellGap * scale;
        var x = left + PadX * scale;
        var right = left + width - PadX * scale;
        var number = x;
        var map = number + NumberWidth * scale;
        var portal = map + MathF.Max(120f * scale, (right - map) * MapShare) + gap;
        var floor = portal + FlagWidth * scale + gap;
        var deaths = floor + FlagWidth * scale + gap;
        var duration = deaths + FlagWidth * scale + gap;
        var loot = duration + DurationWidth * scale + gap;
        return [number, map, portal, floor, deaths, duration, loot, right];
    }

    private static void DrawHeader(float[] columns, float width, float rowHeight)
    {
        var origin = ImGui.GetCursorScreenPos();
        var y = origin.Y + (rowHeight - TextDraw.SmallCapsSize(NoValue).Y) * 0.5f;
        TextDraw.SmallCaps(Loc.T(L.History.LogNumber), new Vector2(columns[0], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogMap), new Vector2(columns[1], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogPortal), new Vector2(columns[2], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogFloor), new Vector2(columns[3], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogDeaths), new Vector2(columns[4], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogDuration), new Vector2(columns[5], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogLoot), new Vector2(columns[6], y), Styling.TextMuted);
        ImGui.Dummy(new Vector2(width, rowHeight));
    }

    private static void DrawRow(in Row row, int index, float[] columns, float width, float rowHeight, string yes)
    {
        var size = new Vector2(width, rowHeight);
        if (!ImGui.IsRectVisible(size))
        {
            ImGui.Dummy(size);
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var hovered = Hit.HoveringRect(origin, end);
        if (index % 2 == 0 || hovered)
        {
            var tint = hovered ? Styling.WithAlpha(Styling.Surface3, 0.6f) : Styling.WithAlpha(Styling.Surface0, 0.55f);
            Paint.Fill(ImGui.GetWindowDrawList(), origin, end, tint, 6f * scale);
        }

        var gap = CellGap * scale;
        var y = origin.Y + (rowHeight - ImGui.GetTextLineHeight()) * 0.5f;
        TextDraw.At(row.Number, new Vector2(columns[0], y), Styling.TextDim);
        TextDraw.At(TextDraw.Truncate(row.Map, columns[2] - columns[1] - gap), new Vector2(columns[1], y), Styling.TextStrong);
        TextDraw.At(row.Portal ? yes : NoValue, new Vector2(columns[2], y), row.Portal ? Styling.AccentMint : Styling.TextMuted);
        TextDraw.At(row.Floor, new Vector2(columns[3], y), row.Floor == NoValue ? Styling.TextMuted : row.Bottom ? Styling.AccentMint : Styling.TextStrong);
        TextDraw.At(row.Deaths.ToString(Loc.Culture), new Vector2(columns[4], y), row.Deaths > 0 ? Styling.AccentRose : Styling.TextMuted);
        TextDraw.At(row.Duration, new Vector2(columns[5], y), row.Duration == NoValue ? Styling.TextMuted : Styling.TextSecondary);
        var loot = TextDraw.Truncate(row.Loot, columns[7] - columns[6]);
        TextDraw.At(loot, new Vector2(columns[6], y), row.Loot == NoValue ? Styling.TextMuted : Styling.AccentAmberSoft);
        ImGui.Dummy(size);

        if (hovered)
        {
            using var tooltip = Tooltip.Begin();
            Tooltip.Text(row.AtUtc.ToLocalTime().ToString("g", Loc.Culture), Styling.TextStrong);
            if (!ReferenceEquals(loot, row.Loot))
            {
                Tooltip.Text(row.Loot, Styling.AccentAmberSoft);
            }
        }
    }

    private void Build(MapLog log)
    {
        builtRevision = log.Revision;
        builtLanguage = Loc.Current;
        rows.Clear();
        var entries = log.Entries;
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            rows.Add(new Row(entry.Number.ToString(CultureInfo.InvariantCulture), NotableLoot.Name(entry.MapItemId), entry.Portal, FloorText(entry), entry.Rounds > 0 && entry.Floor >= entry.Rounds, entry.Deaths, DurationText(entry), LootText(entry), entry.AtUtc));
        }
    }

    // "4/5" against the dungeon's rounds, so a line that reached the last one stands apart from one sent out before it.
    private static string FloorText(MapLogEntry entry)
    {
        if (entry.Floor <= 0)
        {
            return NoValue;
        }

        var floor = entry.Floor.ToString(Loc.Culture);
        return entry.Rounds > 0 ? $"{floor}/{entry.Rounds.ToString(Loc.Culture)}" : floor;
    }

    private static string DurationText(MapLogEntry entry)
        => entry.Seconds > 0 ? Formatting.Elapsed(TimeSpan.FromSeconds(entry.Seconds)) : NoValue;

    private static string LootText(MapLogEntry entry)
    {
        if (entry.Loot is not { Count: > 0 } loot)
        {
            return NoValue;
        }

        var parts = new List<string>(loot.Count);
        foreach (var (itemId, quantity) in loot)
        {
            parts.Add(Loc.T(L.History.LogLootItem, NotableLoot.Name(itemId), quantity));
        }

        return string.Join(Loc.T(L.History.LogLootSeparator), parts);
    }
}
