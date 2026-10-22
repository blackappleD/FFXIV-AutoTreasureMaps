using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Windows.Components;
using AutoTreasureMaps.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Shell;

internal static class ActionDock
{
    private const float PadX = 18f;
    private const float ButtonGap = 8f;

    // Pause takes this share of a three-button dock; stop after this map and stop split the rest.
    private const float PauseShare = 0.26f;

    public static void Draw(Plugin plugin, Vector2 size, float windowRounding)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Dock.Background(drawList, origin, end, windowRounding);

        var padX = PadX * scale;
        var buttonHeight = Layout.HeroButtonHeight * scale;
        var innerWidth = size.X - padX * 2f;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, origin.Y + (size.Y - buttonHeight) * 0.5f));

        if (plugin.Controller.Running)
        {
            DrawRunControls(plugin, innerWidth);
        }
        else
        {
            DrawStart(plugin, innerWidth);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    private static void DrawRunControls(Plugin plugin, float innerWidth)
    {
        var controller = plugin.Controller;
        var gap = ButtonGap * ImGuiHelpers.GlobalScale;
        var stopAfter = controller.CanStopAfterMap;
        var pauseWidth = stopAfter ? (innerWidth - gap * 2f) * PauseShare : (innerWidth - gap) * 0.5f;
        var stopWidth = stopAfter ? (innerWidth - gap * 2f - pauseWidth) * 0.5f : pauseWidth;

        if (PauseButton.Draw(controller.PauseReason, pauseWidth))
        {
            controller.TogglePause();
        }

        ImGui.SameLine(0f, gap);
        if (stopAfter)
        {
            if (StopAfterMapButton.Draw(controller.StopAfterMap, stopWidth))
            {
                controller.ToggleStopAfterMap();
            }

            ImGui.SameLine(0f, gap);
        }

        var session = controller.SessionSnapshot;
        var state = controller.Paused ? Loc.T(L.Treasure.StatePaused) : Loc.T(L.Treasure.StateRunning);
        var stopSub = session is null ? state : Loc.T(L.Treasure.StopSub, state, Formatting.Elapsed(session.Elapsed));
        if (StopButton.Draw(stopSub, stopWidth))
        {
            controller.Stop(travelBack: true);
        }
    }

    private static void DrawStart(Plugin plugin, float innerWidth)
    {
        var check = ReadyState.Resolve(plugin.Controller).Check;
        if (StartButton.Draw(TreasureLauncher.Sublabel(check), check.CanStart, TreasureLauncher.Reason(check), innerWidth))
        {
            TreasureLauncher.Start();
        }
    }
}
