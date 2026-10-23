using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Shopping;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Sections.Config;

internal static class ShoppingSettings
{
    private const int ThresholdStep = 50;
    private const float PickerWidth = 340f;
    private const float AddButtonWidth = 96f;
    private const float QuantityWidth = 120f;
    private const float QuantityGap = 8f;
    // The IPC check asks GatherBuddy for its version, so it is read about once a second rather than every frame.
    private const long AvailabilityRefreshMs = 1_000;

    private sealed class PickerState
    {
        public IReadOnlyList<TomestoneOffer> Offers = [];
        public string[] Labels = [];
        public int Selection;
        public string PickerId = string.Empty;
        public string AddId = string.Empty;
        public string ThresholdId = string.Empty;
        public string ListId = string.Empty;
    }

    private static readonly Dictionary<uint, PickerState> pickers = [];
    private static readonly Dictionary<uint, string> itemNames = [];
    private static LanguageInfo? namesLanguage;
    private static bool gatherBuddyReady;
    private static long availabilityRefreshAtMs;

    public static void Draw(Configuration configuration)
    {
        DrawToggleGroup(configuration);
        using var lists = Motion.PushSection("##atm_shopping_lists", configuration.TomestoneShopping);
        if (lists is null)
        {
            return;
        }

        var currencies = TomestoneCatalog.Currencies;
        for (var index = 0; index < currencies.Count; index++)
        {
            DrawCurrencyGroup(configuration, currencies[index]);
        }
    }

    private static void DrawToggleGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Shopping.Group));

        SettingsRow.Draw(Loc.T(L.Shopping.Enable),
            Loc.T(L.Shopping.EnableHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.TomestoneShopping, value => configuration.TomestoneShopping = value, "##atm_shopping_on"),
            SettingsRow.ToggleHeight);

        RefreshAvailability();
        if (!gatherBuddyReady)
        {
            SettingsRow.Note(Loc.T(L.Shopping.NeedsGatherBuddy), Styling.AccentAmber);
        }

        using var body = Motion.PushSwitch("##atm_shopping_body", configuration.TomestoneShopping);
        if (!configuration.TomestoneShopping)
        {
            SettingsRow.Note(Loc.T(L.Shopping.Off));
            return;
        }

        SettingsRow.Note(Loc.T(L.Shopping.EnableHelp));
        SettingsRow.Note(Loc.T(L.Shopping.Independent), Styling.AccentMint);
        if (gatherBuddyReady)
        {
            SettingsRow.Note(Loc.T(L.Shopping.GatherBuddyReady, ListNames()));
        }
    }

    private static void DrawCurrencyGroup(Configuration configuration, TomestoneCurrency currency)
    {
        var list = TomestoneShopping.ListFor(configuration, currency.ItemId);
        var picker = Picker(currency.ItemId);
        using var group = SettingsGroup.Begin(currency.Name);

        SettingsRow.Draw(Loc.T(L.Shopping.Threshold),
            Loc.T(L.Shopping.ThresholdHelp),
            Stepper.DefaultWidth,
            () => DrawThreshold(configuration, list, picker.ThresholdId),
            itemId: currency.ItemId);
        SettingsRow.Caption(Loc.T(L.Shopping.Held, TomestoneShopping.HeldCount(currency.ItemId)));

        SettingsRow.DrawBlock(Loc.T(L.Shopping.AddItem),
            Loc.T(L.Shopping.AddItemHelp),
            () => DrawAddRow(configuration, list, picker));

        SettingsRow.DrawBlock(Loc.T(L.Shopping.Items),
            Loc.T(L.Shopping.ItemsHelp),
            () => DrawItems(configuration, list, picker.ListId));
    }

    private static void DrawThreshold(Configuration configuration, TomestoneShoppingList list, string id)
    {
        var value = list.Threshold;
        var format = value == 0 ? Loc.T(L.Shopping.ThresholdOff) : Loc.T(L.Shopping.ThresholdFormat);
        if (!Stepper.Draw(id, ref value, ThresholdStep, 0, TomestoneShopping.MaxThreshold, format))
        {
            return;
        }

        list.Threshold = value;
        configuration.SaveDebounced();
    }

    private static void DrawAddRow(Configuration configuration, TomestoneShoppingList list, PickerState picker)
    {
        if (!TomestoneCatalog.OffersReady)
        {
            SettingsRow.Note(Loc.T(L.Shopping.Loading));
            return;
        }

        RefreshPicker(list.CurrencyItemId, picker);
        if (picker.Offers.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Shopping.NoOffers));
            return;
        }

        picker.Selection = Math.Clamp(picker.Selection, 0, picker.Offers.Count - 1);
        SettingsControls.DrawSearchableCombo(picker.PickerId, picker.Labels, ref picker.Selection, PickerWidth, Loc.T(L.Shopping.SearchHint));

        var picked = picker.Offers[picker.Selection];
        var duplicate = Contains(list, picked.ItemId);
        ImGui.SameLine();
        var buttonSize = new Vector2(AddButtonWidth * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeight());
        bool clicked;
        ImGui.PushID(picker.AddId);
        using (ImRaii.Disabled(duplicate))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
        {
            clicked = ImGui.Button(Loc.T(L.Safety.Add), buttonSize);
        }

        ImGui.PopID();
        if (clicked)
        {
            list.Items.Add(new TomestoneShoppingItem { ItemId = picked.ItemId, Quantity = 1 });
            configuration.SaveDebounced();
        }

        if (!duplicate)
        {
            return;
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
        {
            ImGui.TextUnformatted(Loc.T(L.Safety.AlreadyAdded));
        }
    }

    private static void DrawItems(Configuration configuration, TomestoneShoppingList list, string listId)
    {
        var items = list.Items;
        if (items.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Shopping.NoItems));
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var quantityFormat = Loc.T(L.Shopping.QuantityFormat);
        var removeIndex = -1;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            ListRows.Ordinal(index);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            {
                ImGui.TextUnformatted(ItemName(item.ItemId));
            }

            if (ImGui.IsItemHovered())
            {
                ItemTooltip.Hover(item.ItemId);
            }

            ImGui.SameLine(SettingsGroup.InnerRightLocalX() - ImGui.GetFrameHeight() - (QuantityWidth + QuantityGap) * scale);
            ImGui.PushID(listId);
            ImGui.PushID(index);
            var quantity = item.Quantity;
            if (Stepper.Draw("##quantity", ref quantity, 1, 1, TomestoneShopping.MaxQuantity, quantityFormat, QuantityWidth))
            {
                item.Quantity = quantity;
                configuration.SaveDebounced();
            }

            ImGui.PopID();
            ImGui.PopID();

            if (ListRows.RemoveButton(listId, index, Loc.T(L.Safety.Remove)))
            {
                removeIndex = index;
            }
        }

        if (removeIndex < 0)
        {
            return;
        }

        items.RemoveAt(removeIndex);
        configuration.SaveDebounced();
    }

    private static PickerState Picker(uint currencyItemId)
    {
        if (!pickers.TryGetValue(currencyItemId, out var picker))
        {
            picker = new PickerState
            {
                PickerId = $"##atm_shopping_picker_{currencyItemId}",
                AddId = $"##atm_shopping_add_{currencyItemId}",
                ThresholdId = $"##atm_shopping_threshold_{currencyItemId}",
                ListId = $"##atm_shopping_items_{currencyItemId}",
            };
            pickers[currencyItemId] = picker;
        }

        return picker;
    }

    // Labels are built once, when the offers are first read.
    private static void RefreshPicker(uint currencyItemId, PickerState picker)
    {
        if (picker.Labels.Length > 0)
        {
            return;
        }

        var offers = TomestoneCatalog.OffersFor(currencyItemId);
        var labels = new string[offers.Count];
        for (var index = 0; index < offers.Count; index++)
        {
            labels[index] = Loc.T(L.Shopping.OfferLabel, GameGlyphs.Cover(offers[index].Name), offers[index].Cost);
        }

        picker.Offers = offers;
        picker.Labels = labels;
    }

    private static bool Contains(TomestoneShoppingList list, uint itemId)
    {
        for (var index = 0; index < list.Items.Count; index++)
        {
            if (list.Items[index].ItemId == itemId)
            {
                return true;
            }
        }

        return false;
    }

    // Read from the sheet once per item and language rather than on every frame.
    private static string ItemName(uint itemId)
    {
        if (!ReferenceEquals(namesLanguage, Loc.Current))
        {
            itemNames.Clear();
            namesLanguage = Loc.Current;
        }

        if (!itemNames.TryGetValue(itemId, out var name))
        {
            name = NotableLoot.Name(itemId);
            itemNames[itemId] = name;
        }

        return name;
    }

    private static string ListNames()
    {
        var currencies = TomestoneCatalog.Currencies;
        var names = new string[currencies.Count];
        for (var index = 0; index < currencies.Count; index++)
        {
            names[index] = TomestoneShopping.ListName(currencies[index]);
        }

        return string.Join(", ", names);
    }

    private static void RefreshAvailability()
    {
        var now = Environment.TickCount64;
        if (now < availabilityRefreshAtMs)
        {
            return;
        }

        availabilityRefreshAtMs = now + AvailabilityRefreshMs;
        gatherBuddyReady = TomestoneShopping.Available;
    }
}
