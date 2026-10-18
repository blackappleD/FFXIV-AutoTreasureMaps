using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Tasks;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Numerics;
using System.Text;

namespace AutoTreasureMaps.Windows.Sections;

internal static class RunningPanel
{
    private const float PadX = 18f;
    private const int TileCount = 4;

    private static uint cachedTerritoryId = uint.MaxValue;
    private static string cachedZoneName = string.Empty;
    private static CachedText mapsDoneText;
    private static CachedText mapsOfLimitText;
    private static CachedText failedText;
    private static CachedText gilText;
    private static CachedText itemsText;
    private static CachedText timeLeftText;

    public static void Draw(TreasureController controller)
    {
        var paused = controller.Paused;
        var (accent, accentSoft, label) = PhasePalette(controller);

        DrawHeaderStrip(CurrentZoneName(), accent, accentSoft, paused);
        Styling.VSpace(6f);
        DrawHeroCard(controller, accent, accentSoft, label);

        Styling.VSpace(10f);
        DrawStatTiles(controller);
    }

    private static void DrawHeaderStrip(string footer, Vector4 accent, Vector4 accentSoft, bool paused)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail().X;
        var lineHeight = ImGui.GetTextLineHeight();
        var midY = origin.Y + lineHeight * 0.5f;

        var dot = paused ? accent : Styling.PulseColor(accent, accentSoft, Styling.PulseMedium);
        var radius = 4f * scale;
        Paint.Dot(drawList, new Vector2(origin.X + radius + 3f * scale, midY), radius, dot);

        var status = paused ? Loc.T(L.Shell.StatusPaused) : Loc.T(L.Shell.StatusRunning);
        var statusSize = TextDraw.SmallCapsSize(status);
        TextDraw.SmallCaps(status, new Vector2(origin.X + radius * 2f + 12f * scale, midY - statusSize.Y * 0.5f), Styling.TextSecondary);

        using (Fonts.PushCaption())
        {
            var footerSize = TextDraw.Measure(footer);
            TextDraw.At(footer, new Vector2(origin.X + available - footerSize.X, midY - footerSize.Y * 0.5f), Styling.TextMuted);
        }

        ImGui.Dummy(new Vector2(available, lineHeight));
    }

    private static void DrawHeroCard(TreasureController controller, Vector4 accent, Vector4 accentSoft, string label)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Layout.HeroCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var active = controller.Running && !controller.Paused;
        var rounding = Styling.PanelRounding * scale;

        Paint.Glass(drawList, origin, end, rounding, accent, active ? 0.10f : 0.03f, 0f, elevated: true);
        if (active)
        {
            Paint.Stroke(drawList, origin, end, Styling.PulseColor(Styling.WithAlpha(accent, 0.5f), accentSoft, Styling.PulseMedium), rounding, 1.6f);
        }

        var padX = PadX * scale;
        var ringRadius = size.Y * 0.5f - 18f * scale;
        var ringCenter = new Vector2(origin.X + padX + ringRadius, origin.Y + size.Y * 0.5f);
        DrawRunRing(ringCenter, ringRadius, accent, active);

        var columnX = ringCenter.X + ringRadius + 20f * scale;
        var columnWidth = end.X - padX - columnX;
        var y = origin.Y + 16f * scale;

        y += DrawPhaseChip(columnX, y, label, accent, accentSoft) + 10f * scale;
        y = DrawStatus(controller.Status, columnX, columnWidth, y);

        var barHeight = 8f * scale;
        var barOrigin = new Vector2(columnX, y);
        if (active)
        {
            Paint.IndeterminateBar(drawList, barOrigin, columnWidth, barHeight, accent);
        }
        else
        {
            Paint.Bar(drawList, barOrigin, columnWidth, barHeight, 0f, accent);
        }

        y += barHeight + 8f * scale;
        using (Fonts.PushCaption())
        {
            var mapsDone = MapsDone(controller);
            TextDraw.At(mapsDone, new Vector2(columnX, y), Styling.WithAlpha(accentSoft, 0.9f));
            DrawMapDetail(controller.Progress, columnX + TextDraw.Measure(mapsDone).X, columnX + columnWidth, y, accentSoft);
        }

        ImGui.Dummy(size);
    }

    private static float DrawStatus(string status, float x, float width, float y)
    {
        var text = TextDraw.Truncate(string.IsNullOrWhiteSpace(status) ? Loc.T(L.Common.Working) : status, width);
        TextDraw.At(text, new Vector2(x, y), Styling.TextSecondary);
        return y + ImGui.GetTextLineHeight() + 12f * ImGuiHelpers.GlobalScale;
    }

    // A run has no fixed number of maps to fill a ring with, so the ring only shows that it is moving.
    private static void DrawRunRing(Vector2 center, float radius, Vector4 accent, bool active)
    {
        var thickness = 6f * ImGuiHelpers.GlobalScale;
        ProgressRing.Track(center, radius, thickness, Styling.WithAlpha(Styling.BorderDim, 0.7f));
        if (active)
        {
            ProgressRing.Sweep(center, radius, thickness, accent, Styling.PulseOrbit, MathF.PI * 0.6f, 1f);
        }

        ProgressRing.CenterIcon(center, FontAwesomeIcon.Map, Styling.TextDim, radius * 0.55f);
    }

    // The wave and guards left during a fight, and the hunt's time left once the game shows it.
    private static void DrawMapDetail(TreasureProgress progress, float x, float right, float y, Vector4 accentSoft)
    {
        if (!progress.HasMap)
        {
            return;
        }

        var color = Styling.WithAlpha(accentSoft, 0.9f);
        var fight = MapRunText.Fight(progress);
        if (fight.Length > 0)
        {
            TextDraw.Trailing(fight, x, right, y, color);
            x += TextDraw.Measure(TextDraw.Separator).X + TextDraw.Measure(fight).X;
        }

        if (MapRunText.HasHuntTime(progress) && x < right)
        {
            TextDraw.Trailing(MapRunText.HuntTime(progress), x, right, y, color);
        }
    }

    // Against the map limit when one is set.
    private static string MapsDone(TreasureController controller)
    {
        var maps = controller.SessionSnapshot?.MapsRun ?? 0;
        var limit = Plugin.Instance.Configuration.RunMapLimit;
        if (limit <= 0)
        {
            return mapsDoneText.Get(maps, static key => Loc.Plural(L.Run.MapsDone, (int)key));
        }

        return mapsOfLimitText.Get(((long)limit << 32) | (uint)maps, static key => Loc.T(L.Run.MapsOfLimit, (int)(key & uint.MaxValue), (int)(key >> 32)));
    }

    private static float DrawPhaseChip(float x, float y, string text, Vector4 accent, Vector4 accentSoft)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var padX = 9f * scale;
        var padY = 3f * scale;

        using (Fonts.PushCaption())
        {
            var label = TextDraw.Upper(text);
            var textSize = TextDraw.Measure(label);
            var chipMin = new Vector2(x, y);
            var chipMax = chipMin + new Vector2(padX * 2f + textSize.X, textSize.Y + padY * 2f);
            Paint.Pill(drawList, chipMin, chipMax, Styling.WithAlpha(accent, 0.28f), Styling.WithAlpha(accent, 0.65f));
            TextDraw.At(label, new Vector2(x + padX, y + padY), accentSoft);
            return chipMax.Y - chipMin.Y;
        }
    }

    private static (Vector4 Accent, Vector4 AccentSoft, string Label) PhasePalette(TreasureController controller)
    {
        if (!controller.Running)
        {
            return (Styling.TextDim, Styling.TextSecondary, Loc.T(L.Run.PhaseReady));
        }

        if (controller.Paused)
        {
            return controller.PauseReason == PauseReason.InContent
                ? (Styling.AccentAmber, Styling.AccentAmberSoft, Loc.T(L.Run.PhasePausedInContent))
                : (Styling.AccentAmber, Styling.AccentAmberSoft, Loc.T(L.Run.PhasePaused));
        }

        return controller.Phase switch
        {
            TreasurePhase.Fighting  => (Styling.AccentGlow, Styling.AccentGlowSoft, Loc.T(L.Run.PhaseFighting)),
            TreasurePhase.Finishing => (Styling.AccentMint, Styling.AccentMintSoft, Loc.T(L.Run.PhaseFinishing)),
            TreasurePhase.Idle      => (Styling.TextDim,    Styling.TextSecondary,  Loc.T(L.Run.PhaseStandingBy)),
            _                       => (Styling.AccentBlue, Styling.AccentBlueSoft, ReadyState.PhaseLabel(controller.Phase)),
        };
    }

    private static void DrawStatTiles(TreasureController controller)
    {
        var session = controller.SessionSnapshot;
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 8f * scale;
        var tileWidth = (ImGui.GetContentRegionAvail().X - gap * (TileCount - 1)) / TileCount;

        var failed = session?.MapsFailed ?? 0;
        var failedSub = failed > 0 ? failedText.Get(failed, static key => Loc.Plural(L.Run.Failed, (int)key)) : null;
        StatTile.Draw(Loc.T(L.Run.TileLooted), NumberText.Of(session?.MapsCompleted ?? 0), failedSub, Styling.AccentGlow, tileWidth);
        if (session is not null && ImGui.IsItemHovered())
        {
            Tooltip.Show(MapsTooltip(session));
        }

        ImGui.SameLine(0, gap);
        var items = session?.ItemsGained ?? 0;
        var itemsSub = items > 0 ? itemsText.Get(items, static key => Loc.Plural(L.MapRun.Items, (int)key)) : null;
        var netGil = session is null ? 0 : session.Gil - session.GilSpentOnMaps;
        var gil = gilText.Get(netGil, static key => key.ToString("N0", Loc.Culture));
        StatTile.Draw(Loc.T(L.Run.TileGil), gil, itemsSub, Styling.AccentAmber, tileWidth);
        if (session is not null && (session.NotableItems.Count > 0 || session.MapsBought > 0) && ImGui.IsItemHovered())
        {
            Tooltip.Show(GilTooltip(session));
        }

        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.Run.TilePortals), NumberText.Of(session?.PortalsSeen ?? 0), null, Styling.AccentNebula, tileWidth);
        ImGui.SameLine(0, gap);
        var elapsed = session?.Elapsed ?? TimeSpan.Zero;
        StatTile.Draw(Loc.T(L.Run.TileElapsed), Formatting.Elapsed(elapsed), TimeLeft(elapsed), Styling.AccentBlue, tileWidth);
    }

    private static string? TimeLeft(TimeSpan elapsed)
    {
        var limitMinutes = Plugin.Instance.Configuration.RunMinuteLimit;
        if (limitMinutes <= 0)
        {
            return null;
        }

        var minutesLeft = Math.Max(0, (int)Math.Ceiling(limitMinutes - elapsed.TotalMinutes));
        return timeLeftText.Get(minutesLeft, static key => Loc.T(L.Run.MinutesLeft, (int)key));
    }

    // Built only while hovered, so the counts are not composed every frame.
    private static string MapsTooltip(TreasureSession session)
    {
        var lines = new StringBuilder(Loc.T(L.Run.TooltipMapsRun, session.MapsRun));
        AppendCount(lines, MapOutcome.TimedOut, session.MapsTimedOut);
        AppendCount(lines, MapOutcome.Died, session.MapsKnockedOut);
        AppendCount(lines, MapOutcome.DigFailed, session.MapsDigFailed);
        AppendCount(lines, MapOutcome.Unreachable, session.MapsUnreachable);
        AppendCount(lines, MapOutcome.Faulted, session.MapsFaulted);
        if (session.MapsGathered > 0)
        {
            lines.Append('\n').Append(Loc.T(L.Run.TooltipCount, Loc.T(L.Gather.MapsGathered), session.MapsGathered));
        }

        return lines.ToString();
    }

    private static void AppendCount(StringBuilder lines, MapOutcome outcome, int count)
    {
        if (count > 0)
        {
            lines.Append('\n').Append(Loc.T(L.Run.TooltipCount, Loc.T(MapOutcomes.Label(outcome)), count));
        }
    }

    // The tile shows the gil looted less the gil spent on maps; the tooltip splits the two.
    private static string GilTooltip(TreasureSession session)
    {
        var lines = new StringBuilder();
        if (session.MapsBought > 0)
        {
            lines.Append(Loc.T(L.Run.TooltipCount, Loc.T(L.Market.TooltipLooted), session.Gil.ToString("N0", Loc.Culture)));
            lines.Append('\n').Append(Loc.T(L.Run.TooltipCount, Loc.T(L.Market.TooltipSpent), session.GilSpentOnMaps.ToString("N0", Loc.Culture)));
            lines.Append('\n').Append(Loc.T(L.Run.TooltipCount, Loc.T(L.Market.TooltipBought), session.MapsBought));
        }

        if (session.NotableItems.Count == 0)
        {
            return lines.ToString();
        }

        if (lines.Length > 0)
        {
            lines.Append('\n');
        }

        lines.Append(Loc.T(L.Run.TooltipNotable));
        foreach (var (itemId, quantity) in session.NotableItems)
        {
            lines.Append('\n').Append(Loc.T(L.Run.TooltipItem, NotableLoot.Name(itemId), quantity));
        }

        return lines.ToString();
    }

    private static string CurrentZoneName()
    {
        uint territoryId = Svc.ClientState.TerritoryType;
        if (territoryId == cachedTerritoryId)
        {
            return cachedZoneName;
        }

        cachedTerritoryId = territoryId;
        cachedZoneName = GameGlyphs.Cover(Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId)?.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty);
        if (cachedZoneName.Length == 0)
        {
            cachedZoneName = Loc.T(L.Run.SomewhereElse);
        }

        return cachedZoneName;
    }
}
