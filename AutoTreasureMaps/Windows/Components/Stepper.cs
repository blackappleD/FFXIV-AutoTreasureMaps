using AutoTreasureMaps.Core.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Components;

internal static class Stepper
{
    public const float DefaultWidth = 168f;

    // A drag moves the value by half a step per pixel.
    private const float FloatDragStepsPerPixel = 0.5f;

    private readonly record struct Frame(Vector2 Origin, Vector2 Size, float Height);

    public static bool Draw(string id, ref int value, int step, int min, int max, string format, float width = DefaultWidth)
    {
        var frame = Begin(id, width);
        var changed = false;
        if (Decrement(frame, value > min))
        {
            value = Math.Max(min, value - step);
            changed = true;
        }

        PlaceValue(frame);
        using (PushValueColors())
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0f))
        {
            var edited = value;
            var label = ValueLabel.Begin("##value", format);
            if (ImGui.DragInt("##value", ref edited, MathF.Max(0.25f, step * 0.1f), min, max, label.Format))
            {
                value = Math.Clamp(edited, min, max);
                changed = true;
            }

            label.End();
        }

        if (Increment(frame, value < max))
        {
            value = Math.Min(max, value + step);
            changed = true;
        }

        End(frame);
        return changed;
    }

    // Values snap to the step, so repeated clicks never drift into long fractions.
    public static bool Draw(string id, ref float value, float step, float min, float max, string format, float width = DefaultWidth)
    {
        var frame = Begin(id, width);
        var changed = false;
        if (Decrement(frame, value > min))
        {
            value = Snap(MathF.Max(min, value - step), step);
            changed = true;
        }

        PlaceValue(frame);
        using (PushValueColors())
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0f))
        {
            var edited = value;
            var label = ValueLabel.Begin("##value", format, "%g");
            if (ImGui.DragFloat("##value", ref edited, step * FloatDragStepsPerPixel, min, max, label.Format))
            {
                value = Snap(Math.Clamp(edited, min, max), step);
                changed = true;
            }

            label.End();
        }

        if (Increment(frame, value < max))
        {
            value = Snap(MathF.Min(max, value + step), step);
            changed = true;
        }

        End(frame);
        return changed;
    }

    private static float Snap(float value, float step) => MathF.Round(value / step) * step;

    private static Frame Begin(string id, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = ImGui.GetFrameHeight();
        var size = new Vector2(width * scale, height);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var rounding = height * 0.5f;

        Paint.Fill(drawList, origin, end, Styling.WithAlpha(Styling.Surface0, 0.9f), rounding);
        Paint.Stroke(drawList, origin, end, Styling.WithAlpha(Styling.BorderDim, 0.6f), rounding);

        ImGui.PushID(id);
        ImGui.SetCursorScreenPos(origin);
        return new Frame(origin, size, height);
    }

    private static bool Decrement(in Frame frame, bool enabled)
        => IconButton.Draw(FontAwesomeIcon.Minus, "##dec", frame.Height, enabled: enabled);

    private static void PlaceValue(in Frame frame)
    {
        ImGui.SetCursorScreenPos(frame.Origin + new Vector2(frame.Height, 0f));
        ImGui.SetNextItemWidth(frame.Size.X - frame.Height * 2f);
    }

    private static ImRaii.ColorDisposable PushValueColors()
        => ImRaii.PushColor(ImGuiCol.FrameBg, Vector4.Zero)
            .Push(ImGuiCol.FrameBgHovered, Styling.WithAlpha(Styling.Surface2, 0.6f))
            .Push(ImGuiCol.FrameBgActive, Styling.WithAlpha(Styling.Surface3, 0.6f))
            .Push(ImGuiCol.Text, Styling.TextStrong);

    private static bool Increment(in Frame frame, bool enabled)
    {
        if (ImGui.IsItemHovered())
        {
            Tooltip.Show(Loc.T(L.Common.DragAdjustHint));
        }

        ImGui.SetCursorScreenPos(new Vector2(frame.Origin.X + frame.Size.X - frame.Height, frame.Origin.Y));
        return IconButton.Draw(FontAwesomeIcon.Plus, "##inc", frame.Height, enabled: enabled);
    }

    private static void End(in Frame frame)
    {
        ImGui.PopID();
        ImGui.SetCursorScreenPos(frame.Origin);
        ImGui.Dummy(frame.Size);
    }
}
