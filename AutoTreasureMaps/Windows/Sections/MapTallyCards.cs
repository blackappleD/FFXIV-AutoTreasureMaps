using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Market;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Sections;

// One card per kind of map that was run: how often, the dungeons behind its portal by the floor they ended on, and
// its rare loot priced from Universalis. Highest tier first. The text is rebuilt only when the tallies, the
// prices or the language change; keep one instance per place it is drawn. A card's loot list starts folded and
// opens from its header, marked by a chevron after the map's name.
internal sealed class MapTallyCards
{
    private const float PadX = 14f;
    private const float PadY = 10f;
    private const float LineGap = 4f;
    private const float CardGap = 8f;
    private const float ChevronGap = 6f;
    private const int ItemColumns = 2;

    private readonly List<Card> cards = [];
    private readonly HashSet<uint> expanded = [];
    private int builtRevision = -1;
    private int builtPriceRevision = -1;
    private LanguageInfo? builtLanguage;
    private string pricesCaption = string.Empty;

    public void Draw(Dictionary<uint, MapTally> tallies, int revision)
    {
        if (tallies.Count == 0)
        {
            return;
        }

        LootPrices.Want();
        if (revision != builtRevision || LootPrices.Revision != builtPriceRevision || !ReferenceEquals(builtLanguage, Loc.Current))
        {
            Build(tallies);
            builtRevision = revision;
            builtPriceRevision = LootPrices.Revision;
            builtLanguage = Loc.Current;
        }

        DrawTitle();
        for (var index = 0; index < cards.Count; index++)
        {
            if (index > 0)
            {
                Styling.VSpace(CardGap);
            }

            DrawCard(cards[index]);
        }
    }

    private void DrawTitle()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var title = Loc.T(L.Stats.Title);
        var size = TextDraw.SectionTitleSize(title);
        TextDraw.SectionTitle(title, origin, Styling.TextStrong);
        using (Fonts.PushCaption())
        {
            var caption = TextDraw.Truncate(pricesCaption, width - size.X - 16f * scale);
            TextDraw.Right(caption, origin.X + width, origin.Y + size.Y - TextDraw.Measure(caption).Y, Styling.TextMuted);
        }

        ImGui.Dummy(new Vector2(width, size.Y + 8f * scale));
    }

    private void DrawCard(Card card)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var lineHeight = ImGui.GetTextLineHeight();
        float captionHeight;
        using (Fonts.PushCaption())
        {
            captionHeight = ImGui.GetTextLineHeight();
        }

        var foldable = card.Items.Length > 0;
        var open = foldable && expanded.Contains(card.MapItemId);
        var rows = open ? (card.Items.Length + ItemColumns - 1) / ItemColumns : 0;
        var gap = LineGap * scale;
        var itemsHeight = rows > 0 ? gap + rows * (lineHeight + gap) : 0f;
        var headerHeight = PadY * 2f * scale + lineHeight + gap + captionHeight;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, headerHeight + itemsHeight);
        if (!ImGui.IsRectVisible(size))
        {
            ImGui.Dummy(size);
            return;
        }

        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var hover = 0f;
        if (foldable)
        {
            var id = $"##atm_tally_{card.MapItemId}";
            var hit = Hit.Area(id, new Vector2(size.X, headerHeight));
            hover = Motion.Hover(Motion.Key(id), hit.Hovered);
            if (hit.Clicked && !expanded.Remove(card.MapItemId))
            {
                expanded.Add(card.MapItemId);
            }

            ImGui.SetCursorScreenPos(origin);
        }

        var drawList = ImGui.GetWindowDrawList();
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, Styling.AccentAmber, 0.03f);

        var padX = PadX * scale;
        var left = origin.X + padX;
        var right = end.X - padX;
        var y = origin.Y + PadY * scale;
        var worthWidth = card.Worth.Length > 0 ? TextDraw.Measure(card.Worth).X + 12f * scale : 0f;
        if (card.Worth.Length > 0)
        {
            TextDraw.Right(card.Worth, right, y, card.Priced ? Styling.AccentAmber : Styling.TextMuted);
        }

        var chevron = open ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight;
        var chevronSize = foldable ? TextDraw.IconSize(chevron) : Vector2.Zero;
        var chevronSpace = foldable ? chevronSize.X + ChevronGap * scale : 0f;
        var name = TextDraw.Truncate(card.Name, right - left - worthWidth - chevronSpace);
        TextDraw.At(name, new Vector2(left, y), Styling.TextStrong);
        if (foldable)
        {
            var chevronOrigin = new Vector2(left + TextDraw.Measure(name).X + ChevronGap * scale, y + (lineHeight - chevronSize.Y) * 0.5f);
            TextDraw.Icon(chevron, chevronOrigin, Vector4.Lerp(Styling.TextDim, Styling.TextStrong, hover));
        }

        y += lineHeight + gap;

        using (Fonts.PushCaption())
        {
            TextDraw.At(TextDraw.Truncate(card.Summary, right - left), new Vector2(left, y), Styling.TextDim);
        }

        y += captionHeight + gap * 2f;
        var columnGap = 16f * scale;
        var columnWidth = (right - left - columnGap * (ItemColumns - 1)) / ItemColumns;
        for (var index = 0; open && index < card.Items.Length; index++)
        {
            var item = card.Items[index];
            var x = left + index % ItemColumns * (columnWidth + columnGap);
            var rowY = y + index / ItemColumns * (lineHeight + gap);
            var valueWidth = item.Value.Length > 0 ? TextDraw.Measure(item.Value).X + 8f * scale : 0f;
            TextDraw.At(TextDraw.Truncate(item.Label, columnWidth - valueWidth), new Vector2(x, rowY), item.Gained ? Styling.AccentAmberSoft : Styling.TextMuted);
            if (item.Value.Length > 0)
            {
                TextDraw.Right(item.Value, x + columnWidth, rowY, Styling.TextDim);
            }
        }

        ImGui.Dummy(size);
    }

    private void Build(Dictionary<uint, MapTally> tallies)
    {
        pricesCaption = !LootPrices.Enabled ? Loc.T(L.Stats.PricesOff)
            : LootPrices.Loaded ? Loc.T(L.Stats.Prices, LootPrices.DataCenter)
            : Loc.T(L.Stats.PricesLoading);
        cards.Clear();
        var catalog = Plugin.Instance.Catalog;
        var ordered = new List<(byte Rank, uint MapItemId, MapTally Tally)>(tallies.Count);
        foreach (var (mapItemId, tally) in tallies)
        {
            ordered.Add((catalog.TryGetByTimewornItem(mapItemId, out var tier) ? tier.Rank : (byte)0, mapItemId, tally));
        }

        ordered.Sort(static (first, second) => first.Rank != second.Rank ? second.Rank.CompareTo(first.Rank) : first.MapItemId.CompareTo(second.MapItemId));
        for (var index = 0; index < ordered.Count; index++)
        {
            cards.Add(BuildCard(ordered[index].MapItemId, ordered[index].Tally));
        }
    }

    private static Card BuildCard(uint mapItemId, MapTally tally)
    {
        var parts = new List<string> { Loc.Plural(L.Stats.Maps, tally.Maps) };
        if (tally.Dungeons > 0)
        {
            parts.Add(Loc.Plural(L.Stats.Dungeons, tally.Dungeons));
            var floors = tally.Floors.Keys.ToList();
            floors.Sort();
            for (var index = 0; index < floors.Count; index++)
            {
                parts.Add(Loc.T(L.Stats.Floor, floors[index], tally.Floors[floors[index]]));
            }
        }

        // The map's and its dungeon's lists run to dozens of items, so only the ones gained are shown.
        var listed = RareLoot.For(mapItemId);
        var items = new List<ItemLine>();
        for (var index = 0; index < listed.Length; index++)
        {
            var itemId = listed[index];
            var count = tally.Items.GetValueOrDefault(itemId);
            if (count <= 0)
            {
                continue;
            }

            var price = LootPrices.Of(itemId);
            var value = price > 0 ? ((long)price * count).ToString("N0", Loc.Culture) : string.Empty;
            items.Add(new ItemLine(Loc.T(L.Run.TooltipItem, NotableLoot.Name(itemId), count), value, true));
        }

        var worth = LootPrices.ValueOf(tally.Items);
        var priced = worth > 0;
        var worthText = listed.Length == 0 ? string.Empty : Loc.T(L.Stats.Worth, priced ? worth.ToString("N0", Loc.Culture) : "-");
        return new Card(mapItemId, NotableLoot.Name(mapItemId), worthText, priced, string.Join(TextDraw.Separator, parts), items.ToArray());
    }

    private sealed record Card(uint MapItemId, string Name, string Worth, bool Priced, string Summary, ItemLine[] Items);

    private readonly record struct ItemLine(string Label, string Value, bool Gained);
}
