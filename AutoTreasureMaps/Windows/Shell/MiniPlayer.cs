using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Tasks;
using AutoTreasureMaps.Windows.Components;
using AutoTreasureMaps.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Shell;

internal static class MiniPlayer
{
    private const float PadX = 18f;
    private const float ButtonSize = 34f;
    private const float ButtonGap = 8f;
    private const float BarWidth = 160f;
    private const float BarHeight = 8f;

    public static bool Draw(Plugin plugin, Vector2 size, float windowRounding)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var controller = plugin.Controller;
        var info = ReadyState.Resolve(controller);

        Dock.Background(drawList, origin, end, windowRounding);

        var padX = PadX * scale;
        var buttonSize = ButtonSize * scale;
        var buttonsWidth = buttonSize * 2f + ButtonGap * scale;
        ImGui.SetCursorScreenPos(origin);
        var hit = Hit.Area("##atm_mini_open", new Vector2(size.X - padX - buttonsWidth - 8f * scale, size.Y));
        var hover = Motion.Hover(Motion.Key("##atm_mini_open"), hit.Hovered);
        if (hover > 0.01f)
        {
            Paint.Fill(drawList, origin, end, Styling.WithAlpha(Styling.Surface2, 0.35f * hover), windowRounding, ImDrawFlags.RoundCornersBottom);
        }

        var midY = origin.Y + size.Y * 0.5f;
        var dotColor = controller.Paused ? info.Accent : Styling.PulseColor(info.Accent, info.AccentSoft, Styling.PulseMedium);
        Paint.Dot(drawList, new Vector2(origin.X + padX + 4f * scale, midY), 4f * scale, dotColor);

        var barWidth = BarWidth * scale;
        var barRight = end.X - padX - buttonsWidth - 16f * scale;
        var barX = barRight - barWidth;
        var barOrigin = new Vector2(barX, midY - BarHeight * scale * 0.5f);
        if (controller.Paused)
        {
            Paint.Bar(drawList, barOrigin, barWidth, BarHeight * scale, 0f, info.Accent);
        }
        else
        {
            Paint.IndeterminateBar(drawList, barOrigin, barWidth, BarHeight * scale, info.Accent);
        }

        var textX = origin.X + padX + 22f * scale;
        var phase = controller.Paused ? Loc.T(L.Run.PhasePaused) : ReadyState.PhaseLabel(controller.Phase);
        var phaseSize = TextDraw.SmallCapsSize(phase);
        var lineHeight = ImGui.GetTextLineHeight();
        var gap = 2f * scale;
        var top = midY - (phaseSize.Y + gap + lineHeight) * 0.5f;
        TextDraw.SmallCaps(phase, new Vector2(textX, top), info.AccentSoft);
        var detail = controller.Status;
        TextDraw.At(TextDraw.Truncate(detail, barX - 16f * scale - textX), new Vector2(textX, top + phaseSize.Y + gap), Styling.TextStrong);

        var resumeBlocked = controller.Paused && controller.PauseReason == PauseReason.InContent;
        ImGui.SetCursorScreenPos(new Vector2(end.X - padX - buttonSize * 2f - ButtonGap * scale, midY - buttonSize * 0.5f));
        if (IconButton.Draw(controller.Paused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause, "##atm_mini_pause", buttonSize,
                controller.Paused ? Styling.AccentMintSoft : Styling.AccentAmberSoft,
                resumeBlocked ? Loc.T(L.Shell.ResumeBlocked) : controller.Paused ? Loc.T(L.Common.Resume) : Loc.T(L.Common.Pause),
                enabled: !resumeBlocked))
        {
            controller.TogglePause();
        }

        ImGui.SetCursorScreenPos(new Vector2(end.X - padX - buttonSize, midY - buttonSize * 0.5f));
        if (IconButton.Draw(FontAwesomeIcon.Stop, "##atm_mini_stop", buttonSize, Styling.AccentRose, Loc.T(L.Common.StopRun)))
        {
            controller.Stop(travelBack: true);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
        return hit.Clicked;
    }
}
