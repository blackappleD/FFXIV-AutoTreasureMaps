using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Market;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Sections;

// One card per kind of map that was run: how often, the dungeons behind its portal by the floor they ended on, and
// its high-value loot priced from Universalis. Highest tier first. The text is rebuilt only when the tallies, the
// prices or the language change; keep one instance per place it is drawn.
internal sealed class MapTallyCards
{
    private const float PadX = 14f;
    private const float PadY = 10f;
    private const float LineGap = 4f;
    private const float CardGap = 8f;
    private const int ItemColumns = 2;

    private readonly List<Card> cards = [];
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

    private static void DrawCard(Card card)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var lineHeight = ImGui.GetTextLineHeight();
        float captionHeight;
        using (Fonts.PushCaption())
        {
            captionHeight = ImGui.GetTextLineHeight();
        }

        var rows = (card.Items.Length + ItemColumns - 1) / ItemColumns;
        var gap = LineGap * scale;
        var itemsHeight = rows > 0 ? gap + rows * (lineHeight + gap) : 0f;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, PadY * 2f * scale + lineHeight + gap + captionHeight + itemsHeight);
        if (!ImGui.IsRectVisible(size))
        {
            ImGui.Dummy(size);
            return;
        }

        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
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

        TextDraw.At(TextDraw.Truncate(card.Name, right - left - worthWidth), new Vector2(left, y), Styling.TextStrong);
        y += lineHeight + gap;

        using (Fonts.PushCaption())
        {
            TextDraw.At(TextDraw.Truncate(card.Summary, right - left), new Vector2(left, y), Styling.TextDim);
        }

        y += captionHeight + gap * 2f;
        var columnGap = 16f * scale;
        var columnWidth = (right - left - columnGap * (ItemColumns - 1)) / ItemColumns;
        for (var index = 0; index < card.Items.Length; index++)
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
        pricesCaption = LootPrices.Loaded ? Loc.T(L.Stats.Prices, LootPrices.DataCenter) : Loc.T(L.Stats.PricesLoading);
        cards.Clear();
        var catalog = Plugin.Instance.Catalog;
        var ordered = new List<(byte Rank, uint MapItemId, MapTally Tally)>(tallies.Count);
        foreach (var (mapItemId, tally) in tallies)
        {
            ordered.Add((catalog.TryGetByTimewornItem(mapItemId, out var tier) ? tier.Rank : (byte)0, mapItemId, tally));
        }

        ordered.Sort(static (a, b) => a.Rank != b.Rank ? b.Rank.CompareTo(a.Rank) : a.MapItemId.CompareTo(b.MapItemId));
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

        var listed = HighValueLoot.For(mapItemId);
        var items = new ItemLine[listed.Length];
        for (var index = 0; index < listed.Length; index++)
        {
            var itemId = listed[index];
            var count = tally.Items.GetValueOrDefault(itemId);
            var price = LootPrices.Of(itemId);
            var value = count > 0 && price > 0 ? ((long)price * count).ToString("N0", Loc.Culture) : string.Empty;
            items[index] = new ItemLine(Loc.T(L.Run.TooltipItem, NotableLoot.Name(itemId), count), value, count > 0);
        }

        var worth = LootPrices.ValueOf(tally.Items);
        var priced = worth > 0;
        var worthText = listed.Length == 0 ? string.Empty : Loc.T(L.Stats.Worth, priced ? worth.ToString("N0", Loc.Culture) : "-");
        return new Card(NotableLoot.Name(mapItemId), worthText, priced, string.Join(TextDraw.Separator, parts), items);
    }

    private sealed record Card(string Name, string Worth, bool Priced, string Summary, ItemLine[] Items);

    private readonly record struct ItemLine(string Label, string Value, bool Gained);
}
