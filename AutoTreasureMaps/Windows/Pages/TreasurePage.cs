using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Tasks;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows.Components;
using AutoTreasureMaps.Windows.Sections;
using AutoTreasureMaps.Windows.Shell;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Pages;

internal sealed class TreasurePage
{
    private const float SwitchRevealMs = 320f;
    private const float EmptyCardHeight = 118f;
    private const float EmptyCardPadX = 24f;
    private const float LastMapGap = 10f;
    private const float LastMapIconGap = 8f;
    private const float LastMapLabelGap = 12f;

    public void Draw(Plugin plugin, AppWindow window)
    {
        var controller = plugin.Controller;
        var running = controller.Running;

        using var reveal = Motion.PushSwitch("##atm_treasure_state", running, SwitchRevealMs);
        if (running)
        {
            RunningPanel.Draw(controller);
            DrawLiveMap(controller.Progress);
            DrawLastMap(controller.Progress);
            LootCleanupCard.Draw(controller);
            RunningPanel.DrawTallies(controller);
            return;
        }

        DrawIdle(plugin, window);
    }

    private static void DrawIdle(Plugin plugin, AppWindow window)
    {
        if (Headline.Draw(plugin.Controller, plugin.History))
        {
            window.Show(AppWindow.Page.Plugins);
        }

        Styling.VSpace(20f);
        SectionTitle(Loc.T(L.Treasure.CurrentMap));
        DrawCurrentMap(plugin);
        DrawLastMap(plugin.Controller.Progress);
        LootCleanupCard.Draw(plugin.Controller);
        Styling.VSpace(20f);
        SectionTitle(Loc.T(L.Maps.BagTitle));
        MapBagCard.Draw(plugin);
        Styling.VSpace(12f);
    }

    private static void DrawCurrentMap(Plugin plugin)
    {
        var state = HeldSpot.Read(plugin.Catalog, out _, out var tier, out var spot);
        using var reveal = Motion.PushSwitch("##atm_current_map", (int)state, SwitchRevealMs);
        if (state == HeldSpotState.Resolved)
        {
            var setupNeeded = ReadyState.Resolve(plugin.Controller).Kind == ReadyState.Kind.SetupNeeded;
            CurrentMapCard.Draw(tier, spot, MapRunReadiness.Blocker(plugin), setupNeeded, plugin.Controller.Running);
            return;
        }

        if (state == HeldSpotState.Unresolved)
        {
            EmptyCard(FontAwesomeIcon.QuestionCircle, Loc.T(L.Spot.Unresolved));
            return;
        }

        EmptyCard(FontAwesomeIcon.Map, Loc.T(L.Treasure.CurrentMapEmpty));
    }

    private static void DrawLiveMap(TreasureProgress progress)
    {
        if (!progress.HasMap)
        {
            return;
        }

        Styling.VSpace(20f);
        SectionTitle(Loc.T(L.Treasure.CurrentMap));
        CurrentMapCard.DrawLive(progress);
    }

    private static void DrawLastMap(TreasureProgress progress)
    {
        if (progress.LastMap is not { } last)
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        Styling.VSpace(LastMapGap);
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var lineHeight = ImGui.GetTextLineHeight();
        var color = last.Outcome == MapOutcome.Looted ? Styling.AccentMintSoft : Styling.AccentAmberSoft;
        var iconSize = TextDraw.IconSize(FontAwesomeIcon.History);
        TextDraw.Icon(FontAwesomeIcon.History, new Vector2(origin.X, origin.Y + (lineHeight - iconSize.Y) * 0.5f), Styling.TextDim);
        var label = Loc.T(L.MapRun.LastMap);
        var labelX = origin.X + iconSize.X + LastMapIconGap * scale;
        var labelSize = TextDraw.SmallCapsSize(label);
        TextDraw.SmallCaps(label, new Vector2(labelX, origin.Y + (lineHeight - labelSize.Y) * 0.5f), Styling.TextMuted);
        var lineX = labelX + labelSize.X + LastMapLabelGap * scale;
        TextDraw.At(TextDraw.Truncate(MapRunText.LastMap(last), origin.X + width - lineX), new Vector2(lineX, origin.Y), color);
        ImGui.Dummy(new Vector2(width, lineHeight));
    }

    private static void SectionTitle(string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var size = TextDraw.SectionTitleSize(text);
        TextDraw.SectionTitle(text, origin, Styling.TextStrong);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, size.Y + 8f * scale));
    }

    private static void EmptyCard(FontAwesomeIcon icon, string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, EmptyCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, end, Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface0, 0.6f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);

        var padX = EmptyCardPadX * scale;
        var wrapWidth = size.X - padX * 2f;
        var textSize = TextDraw.MeasureWrapped(text, wrapWidth);
        var iconHeight = 26f * scale;
        var gap = 12f * scale;
        var top = origin.Y + (size.Y - iconHeight - gap - textSize.Y) * 0.5f;
        var center = new Vector2((origin.X + end.X) * 0.5f, top + iconHeight * 0.5f);
        ProgressRing.CenterIcon(center, icon, Styling.TextMuted, iconHeight);
        TextDraw.Wrapped(text, new Vector2(center.X - textSize.X * 0.5f, top + iconHeight + gap), wrapWidth, Styling.TextMuted);
        ImGui.Dummy(size);
    }
}
