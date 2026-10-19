using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Sections.Config;

// Every tier row hands SettingsRow the same cached delegate and leaves the row's data in static fields, so drawing the
// whole list allocates nothing per frame.
internal static class MapSettings
{
    private const string TierToggleId = "##atm_maps_tier";
    private const float BadgeGap = 10f;

    private static readonly MapOrder[] orders = [MapOrder.HighestFirst, MapOrder.LowestFirst];

    // Ordered like orders, because the picked index reads both.
    private static readonly SettingsControls.Choices.Choice[] orderChoices =
    [
        new(L.Maps.OrderHighestName, L.Maps.OrderHighestDetail),
        new(L.Maps.OrderLowestName, L.Maps.OrderLowestDetail),
    ];

    private static readonly LootRoll[] rolls = [LootRoll.Need, LootRoll.Greed, LootRoll.Pass, LootRoll.Manual];

    // Ordered like rolls, because the picked index reads both.
    private static readonly SettingsControls.Choices.Choice[] rollChoices =
    [
        new(L.MapRun.RollNeedName, L.MapRun.RollNeedDetail),
        new(L.MapRun.RollGreedName, L.MapRun.RollGreedDetail),
        new(L.MapRun.RollPassName, L.MapRun.RollPassDetail),
        new(L.MapRun.RollManualName, L.MapRun.RollManualDetail),
    ];

    private static readonly ExpansionKind[] expansions =
        [ExpansionKind.ARR, ExpansionKind.HW, ExpansionKind.SB, ExpansionKind.ShB, ExpansionKind.EW, ExpansionKind.DT];

    private static readonly Action drawTierControl = DrawTierControl;

    private static Configuration? rowConfiguration;
    private static TreasureTier rowTier;

    public static void Draw(Configuration configuration, TreasureCatalog catalog)
    {
        DrawOrderGroup(configuration);
        DrawLootGroup(configuration);
        DailyMapSettings.Draw(configuration);
        MarketSettings.Draw(configuration, catalog);
        DrawTiersIntro();
        for (var index = 0; index < expansions.Length; index++)
        {
            DrawExpansionGroup(configuration, catalog, expansions[index]);
        }
    }

    private static void DrawOrderGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Maps.Order));

        var selected = Math.Max(0, Array.IndexOf(orders, configuration.MapOrder));
        SettingsRow.Draw(Loc.T(L.Maps.OrderLabel),
            Loc.T(L.Maps.OrderHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##atm_maps_order", orderChoices, selected, choice =>
            {
                configuration.MapOrder = orders[choice];
                configuration.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(orderChoices[selected].Detail));
    }

    private static void DrawLootGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.MapRun.Loot));

        var selected = Math.Max(0, Array.IndexOf(rolls, configuration.LootRoll));
        SettingsRow.Draw(Loc.T(L.MapRun.Roll),
            Loc.T(L.MapRun.RollHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##atm_maps_roll", rollChoices, selected, choice =>
            {
                configuration.LootRoll = rolls[choice];
                configuration.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(rollChoices[selected].Detail));
    }

    private static void DrawTiersIntro()
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Maps.Tiers));

        SettingsRow.Note(Loc.T(L.Maps.TiersNote));
        SettingsRow.Note(Loc.T(L.Maps.FullPartyNote), Styling.AccentAmberSoft);
        SettingsRow.Note(Loc.T(L.Maps.UnderseaNote), Styling.AccentBlueSoft);
    }

    private static void DrawExpansionGroup(Configuration configuration, TreasureCatalog catalog, ExpansionKind expansion)
    {
        var tiers = catalog.Tiers;
        if (!AnyTierIn(tiers, expansion))
        {
            return;
        }

        using var group = SettingsGroup.Begin(ExpansionLabels.Name(expansion));
        rowConfiguration = configuration;
        for (var index = 0; index < tiers.Length; index++)
        {
            var tier = tiers[index];
            if (tier.Expansion != expansion)
            {
                continue;
            }

            rowTier = tier;
            var controlWidth = tier.Undersea ? TextDraw.IconSize(FontAwesomeIcon.Lock).X / ImGuiHelpers.GlobalScale : SettingsControls.ToggleWidth;
            SettingsRow.Draw(tier.Name, Loc.T(HelpFor(tier)), controlWidth, drawTierControl, SettingsRow.ToggleHeight, tier.TimewornItemId);
        }

        rowConfiguration = null;
    }

    private static void DrawTierControl()
    {
        if (rowConfiguration is not { } configuration)
        {
            return;
        }

        var tier = rowTier;
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var midY = origin.Y + SettingsRow.ToggleHeight * scale * 0.5f;
        var badgeRight = origin.X - BadgeGap * scale;
        if (tier.Undersea)
        {
            Badge.Draw(ImGui.GetWindowDrawList(), Loc.T(L.Maps.UnderseaBadge), Styling.AccentBlue, badgeRight, midY);
            var iconSize = TextDraw.IconSize(FontAwesomeIcon.Lock);
            TextDraw.Icon(FontAwesomeIcon.Lock, new Vector2(origin.X, midY - iconSize.Y * 0.5f), Styling.TextMuted);
            return;
        }

        if (tier.TunedForFullParty)
        {
            Badge.Draw(ImGui.GetWindowDrawList(), Loc.T(L.Maps.FullPartyBadge), Styling.AccentAmber, badgeRight, midY);
        }

        var enabled = MapTierRules.IsEnabled(configuration, tier);
        ImGui.PushID(tier.Rank);
        var changed = ToggleSwitch.Draw(TierToggleId, ref enabled);
        ImGui.PopID();
        if (changed)
        {
            MapTierRules.SetEnabled(configuration, tier, enabled);
        }
    }

    private static LocString HelpFor(in TreasureTier tier)
    {
        if (tier.Undersea)
        {
            return L.Maps.UnderseaHelp;
        }

        return tier.TunedForFullParty ? L.Maps.FullPartyHelp : L.Maps.SoloHelp;
    }

    private static bool AnyTierIn(ReadOnlySpan<TreasureTier> tiers, ExpansionKind expansion)
    {
        for (var index = 0; index < tiers.Length; index++)
        {
            if (tiers[index].Expansion == expansion)
            {
                return true;
            }
        }

        return false;
    }
}
