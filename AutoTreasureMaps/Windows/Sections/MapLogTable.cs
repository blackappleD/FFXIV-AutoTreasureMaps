using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Globalization;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Sections;

// The map log as a table, newest first: number, map, portal, dungeon floor, the dungeon's special rounds, knockouts,
// time taken and the rare loot laid out flat. Fifty lines a page, of every map or of the one picked above the table.
// The text is built once per change of the log, the language or the map picked; rows off screen are only measured.
internal sealed class MapLogTable
{
    private const float PadX = 12f;
    private const float RowPad = 8f;
    private const float CellGap = 10f;
    private const float NumberWidth = 44f;
    private const float FlagWidth = 52f;
    private const float DurationWidth = 64f;
    private const float SpecialWidth = 110f;
    private const float MapShare = 0.3f;
    private const string NoValue = "-";
    private const int PageSize = 50;
    private const float FilterWidth = 260f;
    private const float PagerButton = 26f;

    private readonly List<Row> rows = [];
    private int builtRevision = -1;
    private LanguageInfo? builtLanguage;
    private uint builtFilter = uint.MaxValue;

    // The map the table shows, by item id, or 0 for every map; and the page, counted from 0.
    private uint filter;
    private int page;

    // The filter's choices, every map first and then each map the log holds, most logged first.
    private readonly List<uint> filterMaps = [];
    private string[] filterLabels = [];

    private readonly float[] columnEdges = new float[9];

    private readonly record struct Row(string Number, string Map, bool Portal, string Floor, bool Bottom, string Special, int Deaths, string DeathsText, string Duration, string Loot, DateTime AtUtc);

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

        if (log.Revision != builtRevision || !ReferenceEquals(builtLanguage, Loc.Current) || filter != builtFilter)
        {
            Build(log);
        }

        var pages = Math.Max(1, (rows.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 0, pages - 1);
        DrawControls(pages);
        Styling.VSpace(6f);

        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;
        var rowHeight = ImGui.GetTextLineHeight() + RowPad * scale;
        var columns = Columns(columnEdges, ImGui.GetCursorScreenPos().X, width);
        DrawHeader(columns, width, rowHeight);
        var yes = Loc.T(L.History.LogYes);
        var first = page * PageSize;
        var last = Math.Min(rows.Count, first + PageSize);
        for (var index = first; index < last; index++)
        {
            DrawRow(rows[index], index - first, columns, width, rowHeight, yes);
        }

        if (pages > 1)
        {
            Styling.VSpace(6f);
            DrawPager("bottom", pages, ImGui.GetCursorScreenPos().X + width);
        }
    }

    // The map filter on the left and the pager on the right, on one line.
    private void DrawControls(int pages)
    {
        var top = ImGui.GetCursorScreenPos();
        var right = top.X + ImGui.GetContentRegionAvail().X;
        var picked = Math.Max(0, filterMaps.IndexOf(filter));
        if (Dropdown.Draw("##atm_maplog_filter", filterLabels, ref picked, FilterWidth) && picked >= 0 && picked < filterMaps.Count)
        {
            filter = filterMaps[picked];
            page = 0;
        }

        var below = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(top);
        DrawPager("top", pages, right);
        ImGui.SetCursorScreenPos(below);
    }

    // The page arrows and "page 2 of 5" against the right edge, with how many lines the filter leaves.
    private void DrawPager(string id, int pages, float right)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var button = PagerButton * scale;
        var height = MathF.Max(button, ImGui.GetFrameHeight());
        var origin = ImGui.GetCursorScreenPos();
        var text = Loc.T(L.History.LogPage, page + 1, pages, rows.Count);
        var textSize = TextDraw.Measure(text);
        var gap = 6f * scale;
        var x = right - button;
        var y = origin.Y + (height - button) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        if (IconButton.Draw(FontAwesomeIcon.ChevronRight, $"##atm_maplog_next_{id}", button, tooltip: Loc.T(L.History.LogNextPage), enabled: page < pages - 1))
        {
            page++;
        }

        x -= gap + textSize.X;
        TextDraw.At(text, new Vector2(x, origin.Y + (height - textSize.Y) * 0.5f), Styling.TextSecondary);
        x -= gap + button;
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        if (IconButton.Draw(FontAwesomeIcon.ChevronLeft, $"##atm_maplog_prev_{id}", button, tooltip: Loc.T(L.History.LogPreviousPage), enabled: page > 0))
        {
            page--;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(right - origin.X, height));
    }

    // The left edge of each column: number, map, portal, floor, special rounds, knockouts, time, loot; the last value is
    // the right edge.
    // Written into the one array the table keeps, so nothing is allocated per frame.
    private static float[] Columns(float[] edges, float left, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = CellGap * scale;
        var x = left + PadX * scale;
        var right = left + width - PadX * scale;
        var number = x;
        var map = number + NumberWidth * scale;
        var portal = map + MathF.Max(120f * scale, (right - map) * MapShare) + gap;
        var floor = portal + FlagWidth * scale + gap;
        var special = floor + FlagWidth * scale + gap;
        var deaths = special + SpecialWidth * scale + gap;
        var duration = deaths + FlagWidth * scale + gap;
        var loot = duration + DurationWidth * scale + gap;
        edges[0] = number;
        edges[1] = map;
        edges[2] = portal;
        edges[3] = floor;
        edges[4] = special;
        edges[5] = deaths;
        edges[6] = duration;
        edges[7] = loot;
        edges[8] = right;
        return edges;
    }

    private static void DrawHeader(float[] columns, float width, float rowHeight)
    {
        var origin = ImGui.GetCursorScreenPos();
        var y = origin.Y + (rowHeight - TextDraw.SmallCapsSize(NoValue).Y) * 0.5f;
        TextDraw.SmallCaps(Loc.T(L.History.LogNumber), new Vector2(columns[0], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogMap), new Vector2(columns[1], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogPortal), new Vector2(columns[2], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogFloor), new Vector2(columns[3], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogSpecial), new Vector2(columns[4], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogDeaths), new Vector2(columns[5], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogDuration), new Vector2(columns[6], y), Styling.TextMuted);
        TextDraw.SmallCaps(Loc.T(L.History.LogLoot), new Vector2(columns[7], y), Styling.TextMuted);
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
        var special = TextDraw.Truncate(row.Special, columns[5] - columns[4] - gap);
        TextDraw.At(special, new Vector2(columns[4], y), row.Special == NoValue ? Styling.TextMuted : Styling.AccentMint);
        TextDraw.At(row.DeathsText, new Vector2(columns[5], y), row.Deaths > 0 ? Styling.AccentRose : Styling.TextMuted);
        TextDraw.At(row.Duration, new Vector2(columns[6], y), row.Duration == NoValue ? Styling.TextMuted : Styling.TextSecondary);
        var loot = TextDraw.Truncate(row.Loot, columns[8] - columns[7]);
        TextDraw.At(loot, new Vector2(columns[7], y), row.Loot == NoValue ? Styling.TextMuted : Styling.AccentAmberSoft);
        ImGui.Dummy(size);

        if (hovered)
        {
            using var tooltip = Tooltip.Begin();
            Tooltip.Text(row.AtUtc.ToLocalTime().ToString("g", Loc.Culture), Styling.TextStrong);
            if (!ReferenceEquals(special, row.Special))
            {
                Tooltip.Text(row.Special, Styling.AccentMint);
            }

            if (!ReferenceEquals(loot, row.Loot))
            {
                Tooltip.Text(row.Loot, Styling.AccentAmberSoft);
            }
        }
    }

    private void Build(MapLog log)
    {
        var entries = log.Entries;
        if (log.Revision != builtRevision || !ReferenceEquals(builtLanguage, Loc.Current))
        {
            BuildFilter(entries);
        }

        // A map no longer in the log, after a clear, falls back to every map.
        if (!filterMaps.Contains(filter))
        {
            filter = 0;
        }

        builtRevision = log.Revision;
        builtLanguage = Loc.Current;
        builtFilter = filter;
        rows.Clear();
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            if (filter != 0 && entry.MapItemId != filter)
            {
                continue;
            }

            rows.Add(new Row(entry.Number.ToString(CultureInfo.InvariantCulture), NotableLoot.Name(entry.MapItemId), entry.Portal, FloorText(entry), entry.Rounds > 0 && entry.Floor >= entry.Rounds, SpecialText(entry), entry.Deaths, entry.Deaths.ToString(Loc.Culture), DurationText(entry), LootText(entry), entry.AtUtc));
        }
    }

    private void BuildFilter(List<MapLogEntry> entries)
    {
        var counts = new Dictionary<uint, int>();
        foreach (var entry in entries)
        {
            counts[entry.MapItemId] = counts.GetValueOrDefault(entry.MapItemId) + 1;
        }

        filterMaps.Clear();
        filterMaps.Add(0);
        var labels = new List<string>(counts.Count + 1) { Loc.T(L.History.LogAllMaps, entries.Count) };
        foreach (var (mapItemId, count) in counts.OrderByDescending(static pair => pair.Value))
        {
            filterMaps.Add(mapItemId);
            labels.Add(Loc.T(L.History.LogMapFilter, NotableLoot.Name(mapItemId), count));
        }

        filterLabels = [.. labels];
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

    // Each special round with its score, in the order met: "奇梦抚心 95".
    private static string SpecialText(MapLogEntry entry)
    {
        if (entry.SpecialRounds is not { Count: > 0 } specialRounds)
        {
            return NoValue;
        }

        var parts = new List<string>(specialRounds.Count);
        foreach (var round in specialRounds)
        {
            // The sack round is the only kind so far.
            parts.Add(Loc.T(L.History.LogSackRound, round.Score));
        }

        return string.Join(Loc.T(L.History.LogLootSeparator), parts);
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
