using AutoTreasureMaps.Core.Cleanup;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Tasks;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Sections;

// The loot noted for the next cleanup, two columns of "name ×count", each with a button that whitelists the item instead.
// Shown only while auto-discard is on and something is listed; the list empties once a cleanup has run. The text is
// rebuilt only when the list or the language changes.
internal static class LootCleanupCard
{
    private const float PadX = 14f;
    private const float PadY = 10f;
    private const float RowGap = 4f;
    private const float ButtonGap = 6f;
    private const float TitleGap = 20f;
    private const int Columns = 2;

    private static readonly List<(uint ItemId, string Label)> rows = [];
    private static int builtRevision = -1;
    private static LanguageInfo? builtLanguage;
    private static string kindsCaption = string.Empty;

    public static void Draw(TreasureController controller)
    {
        if (!Plugin.Instance.Configuration.AutoDiscard || !LootCleanup.HasPending)
        {
            return;
        }

        if (LootCleanup.Revision != builtRevision || !ReferenceEquals(builtLanguage, Loc.Current))
        {
            Build();
        }

        Styling.VSpace(TitleGap);
        DrawTitle(controller);
        DrawCard();
    }

    private static void DrawTitle(TreasureController controller)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var title = Loc.T(L.Cleanup.PendingTitle);
        var titleSize = TextDraw.SectionTitleSize(title);
        TextDraw.SectionTitle(title, origin, Styling.TextStrong);

        var cleaning = LootCleanup.Running;
        var label = Loc.T(cleaning ? L.Cleanup.Discarding : L.Cleanup.DiscardNow);
        var buttonWidth = PillButton.Width(label, FontAwesomeIcon.TrashAlt);
        var buttonHeight = 24f;
        var buttonY = origin.Y + (titleSize.Y - buttonHeight * scale) * 0.5f;
        using (Fonts.PushCaption())
        {
            var captionRight = origin.X + width - buttonWidth - 10f * scale;
            TextDraw.Right(kindsCaption, captionRight, origin.Y + titleSize.Y - TextDraw.Measure(kindsCaption).Y, Styling.TextMuted);
        }

        // A run that is going discards on its own between maps.
        var enabled = !cleaning && (!controller.Running || controller.Paused);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - buttonWidth, buttonY));
        if (PillButton.Draw("##atm_cleanup_now", label, Styling.AccentRose, PillButton.Emphasis.Tinted, FontAwesomeIcon.TrashAlt, enabled, buttonHeight))
        {
            LootCleanup.Start();
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, MathF.Max(titleSize.Y, buttonHeight * scale) + 8f * scale));
    }

    private static void DrawCard()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rowHeight = ImGui.GetFrameHeight();
        var gap = RowGap * scale;
        var lines = (rows.Count + Columns - 1) / Columns;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, PadY * 2f * scale + lines * rowHeight + (lines - 1) * gap);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        Paint.Glass(ImGui.GetWindowDrawList(), origin, end, Styling.CardRounding * scale, Styling.AccentRose, 0.03f);

        var left = origin.X + PadX * scale;
        var right = end.X - PadX * scale;
        var columnGap = 16f * scale;
        var columnWidth = (right - left - columnGap * (Columns - 1)) / Columns;
        var textOffset = (rowHeight - ImGui.GetTextLineHeight()) * 0.5f;
        var keepTooltip = Loc.T(L.Cleanup.Keep);
        var kept = 0u;
        for (var index = 0; index < rows.Count; index++)
        {
            var x = left + index % Columns * (columnWidth + columnGap);
            var y = origin.Y + PadY * scale + index / Columns * (rowHeight + gap);
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            ImGui.PushID(index);
            if (IconButton.Draw(FontAwesomeIcon.ShieldAlt, "##atm_cleanup_keep", rowHeight, Styling.AccentMint, keepTooltip))
            {
                kept = rows[index].ItemId;
            }

            ImGui.PopID();
            var textX = x + rowHeight + ButtonGap * scale;
            TextDraw.At(TextDraw.Truncate(rows[index].Label, x + columnWidth - textX), new Vector2(textX, y + textOffset), Styling.TextSecondary);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);

        // After the loop, since whitelisting shortens the list it walks.
        if (kept != 0)
        {
            LootCleanup.AddToWhitelist(kept);
        }
    }

    private static void Build()
    {
        builtRevision = LootCleanup.Revision;
        builtLanguage = Loc.Current;
        rows.Clear();
        var pending = LootCleanup.Pending;
        for (var index = 0; index < pending.Count; index++)
        {
            var item = pending[index];
            rows.Add((item.ItemId, Loc.T(L.Cleanup.ItemLine, NotableLoot.Name(item.ItemId), item.Quantity)));
        }

        kindsCaption = Loc.Plural(L.Cleanup.PendingKinds, pending.Count);
    }
}
