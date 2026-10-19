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

// The loot noted for the next cleanup, in compact columns of the item's game icon and "name ×count", each with a button
// that whitelists the item instead. A treasure dungeon under way adds what it has given so far. Shown only while
// auto-discard is on and something is listed; the list empties once a cleanup has run. The text is rebuilt only when the
// list or the language changes.
internal static class LootCleanupCard
{
    private const float PadX = 12f;
    private const float PadY = 6f;
    private const float RowGap = 1f;
    private const float RowPad = 4f;
    private const float ButtonGap = 4f;
    private const float IconInset = 1f;
    private const float IconRounding = 3f;
    private const float TitleGap = 16f;
    private const float ColumnGap = 14f;
    // Three columns once each is at least this wide, two below it.
    private const float WideColumn = 180f;

    private static readonly List<(uint ItemId, uint IconId, string Label)> rows = [];
    private static readonly Dictionary<uint, int> quantities = [];
    private static int builtRevision = -1;
    private static LanguageInfo? builtLanguage;
    private static string kindsCaption = string.Empty;

    public static void Draw(TreasureController controller)
    {
        if (!Plugin.Instance.Configuration.AutoDiscard || !LootCleanup.HasShown)
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
        var buttonHeight = 22f;
        var buttonY = origin.Y + (titleSize.Y - buttonHeight * scale) * 0.5f;
        using (Fonts.PushCaption())
        {
            var captionRight = origin.X + width - buttonWidth - 10f * scale;
            TextDraw.Right(kindsCaption, captionRight, origin.Y + titleSize.Y - TextDraw.Measure(kindsCaption).Y, Styling.TextMuted);
        }

        // A run that is going discards on its own between maps, and a dungeon's loot can only go once it has ended.
        var enabled = !cleaning && LootCleanup.HasPending && (!controller.Running || controller.Paused);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - buttonWidth, buttonY));
        if (PillButton.Draw("##atm_cleanup_now", label, Styling.AccentRose, PillButton.Emphasis.Tinted, FontAwesomeIcon.TrashAlt, enabled, buttonHeight))
        {
            LootCleanup.Start();
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, MathF.Max(titleSize.Y, buttonHeight * scale) + 6f * scale));
    }

    private static void DrawCard()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rowHeight = ImGui.GetTextLineHeight() + RowPad * scale;
        var gap = RowGap * scale;
        var inner = ImGui.GetContentRegionAvail().X - PadX * 2f * scale;
        var columns = (inner - ColumnGap * 2f * scale) / 3f >= WideColumn * scale ? 3 : 2;
        var lines = (rows.Count + columns - 1) / columns;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, PadY * 2f * scale + lines * rowHeight + (lines - 1) * gap);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, Styling.AccentRose, 0.03f);

        var left = origin.X + PadX * scale;
        var right = end.X - PadX * scale;
        var columnGap = ColumnGap * scale;
        var columnWidth = (right - left - columnGap * (columns - 1)) / columns;
        var textOffset = (rowHeight - ImGui.GetTextLineHeight()) * 0.5f;
        var keepTooltip = Loc.T(L.Cleanup.Keep);
        var kept = 0u;
        for (var index = 0; index < rows.Count; index++)
        {
            var x = left + index % columns * (columnWidth + columnGap);
            var y = origin.Y + PadY * scale + index / columns * (rowHeight + gap);
            var inset = IconInset * scale;
            ItemIcon.Draw(drawList, rows[index].IconId, new Vector2(x + inset, y + inset), new Vector2(x + rowHeight - inset, y + rowHeight - inset), IconRounding * scale);
            var buttonX = x + columnWidth - rowHeight;
            ImGui.SetCursorScreenPos(new Vector2(buttonX, y));
            ImGui.PushID(index);
            if (IconButton.Draw(FontAwesomeIcon.ShieldAlt, "##atm_cleanup_keep", rowHeight, Styling.AccentMint, keepTooltip))
            {
                kept = rows[index].ItemId;
            }

            ImGui.PopID();
            var textX = x + rowHeight + ButtonGap * scale;
            TextDraw.At(TextDraw.Truncate(rows[index].Label, buttonX - ButtonGap * scale - textX), new Vector2(textX, y + textOffset), Styling.TextSecondary);
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
        quantities.Clear();
        Merge(LootCleanup.Pending);
        Merge(LootCleanup.Preview);
        foreach (var (itemId, quantity) in quantities)
        {
            rows.Add((itemId, ItemIcon.IdOf(itemId), Loc.T(L.Cleanup.ItemLine, NotableLoot.Name(itemId), quantity)));
        }

        kindsCaption = Loc.Plural(L.Cleanup.PendingKinds, rows.Count);
    }

    // An item listed already and looted again in the dungeon under way shows once, with both counts.
    private static void Merge(IReadOnlyList<PendingLoot> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            quantities[item.ItemId] = quantities.GetValueOrDefault(item.ItemId) + item.Quantity;
        }
    }
}
