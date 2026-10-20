using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Market;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Tasks;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows.Components;
using AutoTreasureMaps.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Pages;

internal sealed class HistoryPage
{
    private const int ChartRuns = 24;
    private const int TileCount = 5;
    private const float PadX = 14f;
    private const float MetricWidth = 64f;
    private const float IconColumn = 18f;
    private const float ConfirmSlide = 12f;
    private const string NoValue = "-";
    private const float BadgeGap = 6f;

    // Indexes the list switch's segments.
    private enum ListView
    {
        Runs,
        MapLog,
    }

    private readonly MapTallyCards tallyCards = new();
    private readonly MapLogTable mapLogTable = new();
    private ListView listView;
    private bool confirmClear;
    private CachedText mapsText;
    private CachedText costText;
    private CachedText revenueText;
    private CachedText netText;
    private CachedText portalsText;
    private CachedText tomestonesText;
    private CachedText runsText;
    private CachedText perMapText;

    // The history's tallies with the run under way added in, and what they were built from.
    private Dictionary<uint, MapTally> combinedTallies = [];
    private int talliesRevision;
    private int builtHistoryRevision = -1;
    private TreasureSession? builtSession;
    private int builtTallyRevision = -1;

    public void Draw(Plugin plugin)
    {
        var history = plugin.History;
        var session = LiveSession(plugin.Controller);
        var totals = WithSession(history.Lifetime, session);
        var subtitle = totals.Runs == 0
            ? Loc.T(L.History.Empty)
            : Loc.Plural(L.History.Summary, totals.Runs, Formatting.Elapsed(totals.Duration), totals.MapsPerHour.ToString("F1", Loc.Culture));
        PageHeader.Draw(Loc.T(L.History.Title), subtitle);

        var tallies = Tallies(history, session);
        DrawLifetime(totals, LootPrices.ValueOf(tallies));
        Styling.VSpace(14f);

        var mapLog = plugin.MapLog;
        if (totals.Runs == 0 && mapLog.Entries.Count == 0)
        {
            DrawEmptyState();
            return;
        }

        if (tallies.Count > 0)
        {
            tallyCards.Draw(tallies, talliesRevision);
            Styling.VSpace(14f);
        }

        if (history.Records.Count > 0)
        {
            Label(Loc.T(L.History.MapsPerRun));
            DrawChart(history);
            Styling.VSpace(12f);
        }

        DrawListSwitch();
        Styling.VSpace(10f);
        if (listView == ListView.MapLog)
        {
            mapLogTable.Draw(mapLog);
        }
        else
        {
            var records = history.Records;
            for (var index = 0; index < records.Count; index++)
            {
                DrawRow(records[index], index);
                Styling.VSpace(2f);
            }
        }

        Styling.VSpace(8f);
        DrawClearControl(history, mapLog);
    }

    // A run is recorded only once it ends, so until then its maps so far are counted in here, and the page moves map by
    // map. A run that has done nothing yet, or has already been recorded while its after-run action finishes, is left out.
    private static TreasureSession? LiveSession(TreasureController controller)
        => controller.SessionSnapshot is { Recorded: false, DidNothing: false } session ? session : null;

    private static RunHistory.LifetimeTotals WithSession(RunHistory.LifetimeTotals totals, TreasureSession? session)
    {
        if (session is null)
        {
            return totals;
        }

        totals.Runs++;
        totals.Maps += session.MapsCompleted;
        totals.MapsRun += session.MapsRun;
        totals.Gil += session.Gil - session.GilSpentOnMaps;
        totals.Portals += session.PortalsSeen;
        totals.Tomestones += session.Tomestones;
        totals.Seconds += session.Elapsed.TotalSeconds;
        return totals;
    }

    // Rebuilt only when the history or the run's tallies change, bumping the revision the cards rebuild on.
    private Dictionary<uint, MapTally> Tallies(RunHistory history, TreasureSession? session)
    {
        var tallyRevision = session?.TallyRevision ?? -1;
        if (history.Revision == builtHistoryRevision && ReferenceEquals(session, builtSession) && tallyRevision == builtTallyRevision)
        {
            return combinedTallies;
        }

        builtHistoryRevision = history.Revision;
        builtSession = session;
        builtTallyRevision = tallyRevision;
        talliesRevision++;
        if (session is null || session.Tallies.Count == 0)
        {
            combinedTallies = history.LifetimeTallies;
            return combinedTallies;
        }

        combinedTallies = MapTally.Copy(history.LifetimeTallies);
        MapTally.AddAll(combinedTallies, session.Tallies);
        return combinedTallies;
    }

    private void DrawListSwitch()
    {
        ReadOnlySpan<Segmented.Item> items =
        [
            new(FontAwesomeIcon.History, Loc.T(L.History.RecentRuns)),
            new(FontAwesomeIcon.Map, Loc.T(L.History.MapLogTab)),
        ];
        var selected = (int)listView;
        if (Segmented.Draw("##atm_hist_list", items, ref selected, height: Layout.ActionPillHeight + 4f, width: Segmented.PreferredWidth(items)))
        {
            listView = (ListView)selected;
        }
    }

    private static void Label(string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var size = TextDraw.SectionTitleSize(text);
        TextDraw.SectionTitle(text, origin, Styling.TextStrong);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, size.Y + 8f * scale));
    }

    // The captions are rebuilt only when the number they show changes. The cost is the gil spent on maps less the gil
    // looted; the revenue is the rare loot at today's market price, and the net puts the two together.
    private void DrawLifetime(RunHistory.LifetimeTotals totals, long lootValue)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 8f * scale;
        var tileWidth = (ImGui.GetContentRegionAvail().X - gap * (TileCount - 1)) / TileCount;
        var totalCost = -totals.Gil;
        var runs = totals.Runs > 0 ? runsText.Get(totals.Runs, static key => Loc.Plural(L.History.TileRunsCount, (int)key)) : null;
        var perMap = totals.MapsRun > 0
            ? perMapText.Get(totalCost / totals.MapsRun, static key => Loc.T(L.History.TilePerMap, key.ToString("N0", Loc.Culture)))
            : null;
        var maps = mapsText.Get(totals.Maps, static key => key.ToString("N0", Loc.Culture));
        var cost = costText.Get(totalCost, static key => key.ToString("N0", Loc.Culture));
        var revenue = revenueText.Get(lootValue, static key => key.ToString("N0", Loc.Culture));
        var net = netText.Get(lootValue - totalCost, static key => Loc.T(L.History.TileNet, key.ToString("+#,0;-#,0;0", Loc.Culture)));
        var portals = portalsText.Get(totals.Portals, static key => key.ToString("N0", Loc.Culture));
        var tomestones = tomestonesText.Get(totals.Tomestones, static key => key.ToString("N0", Loc.Culture));

        StatTile.Draw(Loc.T(L.History.TileMaps), maps, runs, Styling.AccentBlue, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.History.TileCost), cost, perMap, Styling.AccentRose, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.History.TileRevenue), revenue, net, Styling.AccentAmber, tileWidth);
        if (ImGui.IsItemHovered())
        {
            Tooltip.Show(Loc.T(L.Run.TooltipCount, Loc.T(L.Stats.LootValue), revenue));
        }

        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.History.TilePortals), portals, null, Styling.AccentNebula, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.History.TileTomestones), tomestones, null, Styling.AccentMint, tileWidth);
    }

    private static void DrawEmptyState()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, 110f * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, end, Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface0, 0.6f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);

        var center = new Vector2((origin.X + end.X) * 0.5f, origin.Y + size.Y * 0.42f);
        ProgressRing.CenterIcon(center, FontAwesomeIcon.History, Styling.TextMuted, 26f * scale);
        TextDraw.Center(Loc.T(L.History.NoRuns), center.X, center.Y + 22f * scale, Styling.TextMuted);
        ImGui.Dummy(size);
    }

    private static void DrawChart(RunHistory history)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var records = history.Records;
        var count = Math.Min(ChartRuns, records.Count);
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Layout.ChartHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();

        Paint.Surface(drawList, origin, end, Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface0, 0.6f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);

        var padX = PadX * scale;
        var plotMin = new Vector2(origin.X + padX, origin.Y + 28f * scale);
        var plotMax = new Vector2(end.X - padX, end.Y - 12f * scale);
        var plotWidth = plotMax.X - plotMin.X;
        var plotHeight = plotMax.Y - plotMin.Y;

        var peak = 1;
        for (var index = 0; index < count; index++)
        {
            peak = Math.Max(peak, records[index].MapsCompleted);
        }

        using (Fonts.PushCaption())
        {
            TextDraw.At(Loc.Plural(L.History.ChartRange, count), new Vector2(plotMin.X, origin.Y + 9f * scale), Styling.TextMuted);
            TextDraw.Right(Loc.T(L.History.ChartPeak, peak), plotMax.X, origin.Y + 9f * scale, Styling.TextMuted);
        }

        drawList.AddLine(new Vector2(plotMin.X, plotMax.Y), new Vector2(plotMax.X, plotMax.Y), Paint.Col(Styling.WithAlpha(Styling.BorderDim, 0.7f)), 1f);

        var gap = 2f * scale;
        var barWidth = MathF.Max(2f * scale, (plotWidth - gap * (count - 1)) / count);
        var stride = barWidth + gap;
        var hovered = -1;
        if (Hit.HoveringRect(plotMin, plotMax))
        {
            hovered = (int)((ImGui.GetMousePos().X - plotMin.X) / stride);
            if (hovered >= count)
            {
                hovered = -1;
            }
        }

        var rounding = MathF.Min(4f * scale, barWidth * 0.5f);
        for (var index = 0; index < count; index++)
        {
            var record = records[count - 1 - index];
            var height = MathF.Max(2f * scale, plotHeight * record.MapsCompleted / peak);
            var barMin = new Vector2(plotMin.X + stride * index, plotMax.Y - height);
            var barMax = new Vector2(barMin.X + barWidth, plotMax.Y);
            var color = index == hovered ? Styling.AccentGlowSoft : Styling.AccentGlow;
            drawList.AddRectFilled(barMin, barMax, Paint.Col(color), rounding, ImDrawFlags.RoundCornersTop);
        }

        if (hovered >= 0)
        {
            Tooltip.Show(ChartTooltip(records[count - 1 - hovered]));
        }

        ImGui.Dummy(size);
    }

    private static string ChartTooltip(RunRecord record)
        => Loc.T(L.History.ChartTooltip, RelativeTime(record.EndedAtUtc), record.MapsCompleted, Formatting.Elapsed(record.Duration));

    private static void DrawRow(RunRecord record, int index)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Layout.HistoryRowHeight * scale);
        if (!ImGui.IsRectVisible(size))
        {
            ImGui.Dummy(size);
            return;
        }

        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();

        ImGui.PushID((nint)(index + 1));
        var hit = Hit.Area("##run", size, handCursor: false);
        var hover = Motion.Hover(Motion.Key("##run"), hit.Hovered);
        ImGui.PopID();

        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, Styling.AccentGlow, 0.02f, hover);

        var padX = PadX * scale;
        var midY = origin.Y + size.Y * 0.5f;
        var textX = DrawRunIcon(origin.X + padX, midY);
        var when = RelativeTime(record.EndedAtUtc);
        var job = string.IsNullOrEmpty(record.JobAbbreviation) ? NoValue : GameGlyphs.Cover(record.JobAbbreviation);
        var detail = Loc.T(L.History.RowDetail, job, Formatting.Elapsed(record.Duration));

        var whenSize = TextDraw.Measure(when);
        Vector2 detailSize;
        using (Fonts.PushCaption())
        {
            detailSize = TextDraw.Measure(detail);
        }

        var top = midY - (whenSize.Y + 3f * scale + detailSize.Y) * 0.5f;
        TextDraw.At(when, new Vector2(textX, top), Styling.TextStrong);
        if (record.EndedBy is { } ending)
        {
            Badge.DrawLeft(drawList, Loc.T(RunEnds.Label(ending)), EndColor(ending), textX + whenSize.X + BadgeGap * 2f * scale, top + whenSize.Y * 0.5f);
        }

        using (Fonts.PushCaption())
        {
            TextDraw.At(detail, new Vector2(textX, top + whenSize.Y + 3f * scale), Styling.TextDim);
        }

        var metricWidth = MetricWidth * scale;
        var x = end.X - padX - metricWidth;
        DrawMetric(x, midY, metricWidth, NumberText.Of(record.MapsCompleted), Loc.T(L.History.TileMaps), Styling.AccentBlue);
        x -= metricWidth;
        var netGil = record.NetGil;
        DrawMetric(x, midY, metricWidth, netGil != 0 ? netGil.ToString("N0", Loc.Culture) : NoValue, Loc.T(L.History.TileGil), GilColor(netGil));

        if (hit.Hovered)
        {
            DrawRunTooltip(record);
        }
    }

    private static Vector4 GilColor(long netGil) => netGil switch
    {
        > 0 => Styling.AccentAmber,
        < 0 => Styling.AccentRose,
        _ => Styling.TextMuted,
    };

    private static Vector4 EndColor(RunEnd ending) => ending switch
    {
        RunEnd.OutOfMaps or RunEnd.MapLimit or RunEnd.TimeLimit or RunEnd.SingleMap or RunEnd.Dungeon => Styling.AccentMint,
        RunEnd.Stopped => Styling.TextDim,
        RunEnd.Faulted => Styling.AccentRose,
        _ => Styling.AccentAmber,
    };

    // Returns where the row's text starts; the icon is centred in a fixed column so every row's text lines up.
    private static float DrawRunIcon(float x, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var column = IconColumn * scale;
        var iconSize = TextDraw.IconSize(FontAwesomeIcon.Map);
        TextDraw.Icon(FontAwesomeIcon.Map, new Vector2(x + (column - iconSize.X) * 0.5f, midY - iconSize.Y * 0.5f), Styling.TextDim);
        return x + column + 10f * scale;
    }

    private static void DrawMetric(float x, float midY, float width, string value, string label, Vector4 color)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var valueSize = TextDraw.Measure(value);
        var labelSize = TextDraw.SmallCapsSize(label);
        var top = midY - (valueSize.Y + 2f * scale + labelSize.Y) * 0.5f;
        var centerX = x + width * 0.5f;
        TextDraw.At(value, new Vector2(centerX - valueSize.X * 0.5f, top), color);
        TextDraw.SmallCaps(label, new Vector2(centerX - labelSize.X * 0.5f, top + valueSize.Y + 2f * scale), Styling.TextMuted);
    }

    // Composed only while the row is hovered.
    private static void DrawRunTooltip(RunRecord record)
    {
        using var tooltip = Tooltip.Begin();
        Tooltip.Text(record.EndedAtUtc.ToLocalTime().ToString("g", Loc.Culture), Styling.TextStrong);
        if (record.MapsPerHour > 0)
        {
            Tooltip.Text(Loc.T(L.History.TooltipRate, record.MapsPerHour.ToString("F1", Loc.Culture)));
        }

        Tooltip.Text(Loc.T(L.Run.TooltipMapsRun, record.MapsRun));
        CountLine(Loc.T(MapOutcomes.Label(MapOutcome.TimedOut)), record.MapsTimedOut);
        CountLine(Loc.T(MapOutcomes.Label(MapOutcome.Died)), record.MapsKnockedOut);
        CountLine(Loc.T(MapOutcomes.Label(MapOutcome.DigFailed)), record.MapsDigFailed);
        CountLine(Loc.T(MapOutcomes.Label(MapOutcome.Unreachable)), record.MapsUnreachable);
        CountLine(Loc.T(MapOutcomes.Label(MapOutcome.Faulted)), record.MapsFaulted);
        CountLine(Loc.T(L.Gather.MapsGathered), record.MapsGathered);
        CountLine(Loc.T(L.History.TilePortals), record.PortalsSeen);
        CountLine(Loc.T(L.History.Items), record.ItemsGained);
        CountLine(Loc.T(L.History.TileTomestones), record.Tomestones);
        var lootValue = LootPrices.ValueOf(record.Tallies);
        if (record.MapsBought > 0 || lootValue > 0)
        {
            Tooltip.Text(Loc.T(L.Run.TooltipCount, Loc.T(L.Market.TooltipLooted), record.Gil.ToString("N0", Loc.Culture)));
        }

        if (lootValue > 0)
        {
            Tooltip.Text(Loc.T(L.Run.TooltipCount, Loc.T(L.Stats.LootValue), lootValue.ToString("N0", Loc.Culture)));
        }

        if (record.MapsBought > 0)
        {
            Tooltip.Text(Loc.T(L.Run.TooltipCount, Loc.T(L.Market.TooltipSpent), record.GilSpentOnMaps.ToString("N0", Loc.Culture)));
            CountLine(Loc.T(L.Market.TooltipBought), record.MapsBought);
        }

        if (record.NotableItems is { Count: > 0 } notable)
        {
            Tooltip.Text(Loc.T(L.Run.TooltipNotable));
            foreach (var (itemId, quantity) in notable)
            {
                Tooltip.Text(Loc.T(L.Run.TooltipItem, NotableLoot.Name(itemId), quantity), Styling.AccentAmberSoft);
            }
        }

        if (record.EndedBy is { } ending)
        {
            Tooltip.Text(Loc.T(L.Run.TooltipCount, Loc.T(L.History.EndedBy), Loc.T(RunEnds.Label(ending))));
        }

        if (record.Refusal != MapRefusal.None)
        {
            Tooltip.Text(Loc.T(MapOutcomes.Reason(record.Refusal)), Styling.AccentAmberSoft);
        }
    }

    private static void CountLine(string label, int count)
    {
        if (count > 0)
        {
            Tooltip.Text(Loc.T(L.Run.TooltipCount, label, count));
        }
    }

    private static string RelativeTime(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalSeconds < 60)
        {
            return Loc.T(L.History.JustNow);
        }

        if (span.TotalMinutes < 60)
        {
            return Loc.T(L.History.MinutesAgo, (int)span.TotalMinutes);
        }

        if (span.TotalHours < 24)
        {
            return Loc.T(L.History.HoursAgo, (int)span.TotalHours);
        }

        return span.TotalDays < 7 ? Loc.T(L.History.DaysAgo, (int)span.TotalDays) : utc.ToLocalTime().ToString("M", Loc.Culture);
    }

    private void DrawClearControl(RunHistory history, MapLog mapLog)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var available = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var reveal = Motion.Transition(Motion.Key("##atm_hist_clear_state"), confirmClear);
        var slide = (1f - reveal) * ConfirmSlide * scale;
        using var alpha = Motion.PushAlpha(reveal);

        if (!confirmClear)
        {
            var label = Loc.T(L.History.ClearHistory);
            ImGui.SetCursorScreenPos(new Vector2(origin.X + available - PillButton.Width(label, FontAwesomeIcon.Trash) - slide, origin.Y));
            if (PillButton.Draw("##atm_hist_clear", label, Styling.AccentRose, PillButton.Emphasis.Ghost, FontAwesomeIcon.Trash))
            {
                confirmClear = true;
            }

            return;
        }

        var question = Loc.T(L.History.ClearQuestion);
        var yes = Loc.T(L.History.ClearYes);
        var no = Loc.T(L.Common.Cancel);
        var questionSize = TextDraw.Measure(question);
        var yesWidth = PillButton.Width(yes);
        var noWidth = PillButton.Width(no);
        var gap = 8f * scale;
        var buttonHeight = 28f * scale;

        var x = origin.X + available - noWidth - slide;
        ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
        if (PillButton.Draw("##atm_hist_clear_no", no, Styling.AccentGlow, PillButton.Emphasis.Ghost))
        {
            confirmClear = false;
        }

        x -= gap + yesWidth;
        ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
        if (PillButton.Draw("##atm_hist_clear_yes", yes, Styling.AccentRose, PillButton.Emphasis.Filled))
        {
            history.Clear();
            mapLog.Clear();
            confirmClear = false;
        }

        TextDraw.At(question, new Vector2(x - gap * 1.5f - questionSize.X, origin.Y + (buttonHeight - questionSize.Y) * 0.5f), Styling.AccentRoseSoft);
    }
}
