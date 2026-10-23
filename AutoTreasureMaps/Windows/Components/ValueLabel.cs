using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Components;

// ImGui 1.88 reads a Ctrl+click-typed value back through the display format, so a format with no number in it (an "off" or
// "no limit" label shown at 0) throws every typed value away. Such a label is drawn over the widget instead, and ImGui is
// given a plain number format. Wrap one drag or slider: Begin before it, End right after it.
internal readonly struct ValueLabel
{
    // Widgets being dragged or typed into, which show the number rather than the label.
    private static readonly HashSet<uint> Editing = [];

    private readonly uint id;
    // Null when the format has a number in it, so ImGui draws it as usual; empty while the widget shows the number.
    private readonly string? label;
    private readonly uint color;

    public string Format { get; }

    private ValueLabel(uint id, string? label, uint color, string format)
    {
        this.id = id;
        this.label = label;
        this.color = color;
        Format = format;
    }

    public static ValueLabel Begin(string widgetId, string format, string plainFormat = "%d")
    {
        if (HasPlaceholder(format))
        {
            return new ValueLabel(0, null, 0, format);
        }

        var itemId = ImGui.GetID(widgetId);
        if (Editing.Contains(itemId))
        {
            return new ValueLabel(itemId, string.Empty, 0, plainFormat);
        }

        var color = ImGui.GetColorU32(ImGuiCol.Text);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        return new ValueLabel(itemId, format.Replace("%%", "%", StringComparison.Ordinal), color, plainFormat);
    }

    public void End()
    {
        if (label is null)
        {
            return;
        }

        if (ImGui.IsItemActive())
        {
            Editing.Add(id);
        }
        else
        {
            Editing.Remove(id);
        }

        if (label.Length == 0)
        {
            return;
        }

        ImGui.PopStyleColor();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var size = ImGui.CalcTextSize(label);
        var position = new Vector2(min.X + (max.X - min.X - size.X) * 0.5f, min.Y + (max.Y - min.Y - size.Y) * 0.5f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(min, max, true);
        drawList.AddText(position, color, label);
        drawList.PopClipRect();
    }

    // A printf conversion, so not a lone "%%".
    private static bool HasPlaceholder(string format)
    {
        for (var index = 0; index < format.Length - 1; index++)
        {
            if (format[index] != '%')
            {
                continue;
            }

            if (format[index + 1] != '%')
            {
                return true;
            }

            index++;
        }

        return false;
    }
}
