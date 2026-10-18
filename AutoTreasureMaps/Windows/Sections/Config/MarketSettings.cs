using AutoTreasureMaps.Core.External;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Market;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace AutoTreasureMaps.Windows.Sections.Config;

// Every cap row hands SettingsRow the same cached delegate and leaves the row's tier in static fields, so drawing the list
// allocates nothing per frame.
internal static class MarketSettings
{
    private const int GilStep = 10_000;
    private const int GilMax = 100_000_000;
    private const int CapStep = 1_000;
    private const int CapMax = 10_000_000;
    private const float BoardPanelWidth = 320f;
    private const string CapStepperId = "##atm_market_cap";

    private static readonly Action drawCapControl = DrawCapControl;

    private static Configuration? rowConfiguration;
    private static TreasureTier rowTier;
    private static LanguageInfo? boardLanguage;
    private static string[] boardNames = [];
    private static string[] boardDetails = [];

    public static void Draw(Configuration configuration, TreasureCatalog catalog)
    {
        DrawBuyingGroup(configuration);
        using var caps = Motion.PushSection("##atm_market_caps", configuration.MarketBuyingEnabled);
        if (caps is null)
        {
            return;
        }

        DrawCapsGroup(configuration, catalog);
    }

    private static void DrawBuyingGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Market.Group));

        SettingsRow.Draw(Loc.T(L.Market.Enabled),
            Loc.T(L.Market.EnabledHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.MarketBuyingEnabled, value => configuration.MarketBuyingEnabled = value, "##atm_market_on"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSwitch("##atm_market_body", configuration.MarketBuyingEnabled);
        if (!configuration.MarketBuyingEnabled)
        {
            SettingsRow.Note(Loc.T(L.Market.Off));
            return;
        }

        SettingsRow.Draw(Loc.T(L.Market.Budget),
            Loc.T(L.Market.BudgetHelp),
            Stepper.DefaultWidth,
            () => DrawGil(configuration, "##atm_market_budget", configuration.MarketRunBudget, value => configuration.MarketRunBudget = value));

        SettingsRow.Draw(Loc.T(L.Market.KeepGil),
            Loc.T(L.Market.KeepGilHelp),
            Stepper.DefaultWidth,
            () => DrawGil(configuration, "##atm_market_keep", configuration.MarketKeepGil, value => configuration.MarketKeepGil = value));

        ResolveBoardLabels();
        var selected = BoardIndex(configuration.MarketBoard);
        SettingsRow.Draw(Loc.T(L.Market.Board),
            Loc.T(L.Market.BoardHelp),
            SettingsControls.RowComboWidth,
            () => DrawBoardCombo(configuration, selected));
        SettingsRow.Caption(boardDetails[selected]);

        SettingsRow.Draw(Loc.T(L.Market.BuyAhead),
            Loc.T(L.Market.BuyAheadHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.MarketBuyAhead, value => configuration.MarketBuyAhead = value, "##atm_market_ahead"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Market.CrossWorld),
            Loc.T(L.Market.CrossWorldHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.MarketCrossWorld, value => configuration.MarketCrossWorld = value, "##atm_market_world"),
            SettingsRow.ToggleHeight);
        if (!configuration.MarketCrossWorld)
        {
            return;
        }

        if (!ExternalPlugins.IsInstalled(ExternalPlugin.Lifestream))
        {
            SettingsRow.Note(Loc.T(L.Market.CrossWorldNoLifestream));
        }

        SettingsRow.Draw(Loc.T(L.Market.CrossWorldSavings),
            Loc.T(L.Market.CrossWorldSavingsHelp),
            Stepper.DefaultWidth,
            () => DrawGil(configuration, "##atm_market_world_savings", configuration.MarketCrossWorldMinSavings, value => configuration.MarketCrossWorldMinSavings = value));
    }

    private static void DrawGil(Configuration configuration, string id, int current, Action<int> setter)
    {
        var value = current;
        if (!Stepper.Draw(id, ref value, GilStep, 0, GilMax, Loc.T(L.Market.GilFormat)))
        {
            return;
        }

        setter(value);
        configuration.SaveDebounced();
    }

    private static void DrawBoardCombo(Configuration configuration, int selected)
    {
        var picked = selected;
        if (!Dropdown.DrawDetailed("##atm_market_board", boardNames, boardDetails, ref picked, SettingsControls.RowComboWidth, BoardPanelWidth))
        {
            return;
        }

        configuration.MarketBoard = picked == 0 ? MarketBoardChoice.Automatic : MarketBoards.Cities[picked - 1].Choice;
        configuration.SaveDebounced();
    }

    private static void DrawCapsGroup(Configuration configuration, TreasureCatalog catalog)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Market.Caps));

        SettingsRow.Note(Loc.T(L.Market.CapsNote));
        var help = Loc.T(L.Market.CapHelp);
        var tiers = catalog.Tiers;
        rowConfiguration = configuration;
        for (var index = 0; index < tiers.Length; index++)
        {
            var tier = tiers[index];
            if (!MarketRules.CanBeBought(tier))
            {
                continue;
            }

            rowTier = tier;
            SettingsRow.Draw(tier.Name, help, Stepper.DefaultWidth, drawCapControl);
        }

        rowConfiguration = null;
    }

    // A cap of 0 shows as not set rather than as a price, since it means the tier is never bought.
    private static void DrawCapControl()
    {
        if (rowConfiguration is not { } configuration)
        {
            return;
        }

        var tier = rowTier;
        var cap = MarketRules.CapOf(configuration, tier.Rank);
        var format = cap > 0 ? Loc.T(L.Market.GilFormat) : Loc.T(L.Market.NotSet);
        ImGui.PushID(tier.Rank);
        var changed = Stepper.Draw(CapStepperId, ref cap, CapStep, 0, CapMax, format);
        ImGui.PopID();
        if (changed)
        {
            MarketRules.SetCap(configuration, tier.Rank, cap);
        }
    }

    // Index 0 is Automatic and the cities follow in their table's order; city names come from the game in its own language.
    private static int BoardIndex(MarketBoardChoice choice)
    {
        var cities = MarketBoards.Cities;
        for (var index = 0; index < cities.Length; index++)
        {
            if (cities[index].Choice == choice)
            {
                return index + 1;
            }
        }

        return 0;
    }

    private static void ResolveBoardLabels()
    {
        if (ReferenceEquals(boardLanguage, Loc.Current) && boardNames.Length > 0)
        {
            return;
        }

        var cities = MarketBoards.Cities;
        boardNames = new string[cities.Length + 1];
        boardDetails = new string[cities.Length + 1];
        boardNames[0] = Loc.T(L.Market.BoardAutoName);
        boardDetails[0] = Loc.T(L.Market.BoardAutoDetail);
        for (var index = 0; index < cities.Length; index++)
        {
            boardNames[index + 1] = TerritoryNames.Of(cities[index].TerritoryId);
            boardDetails[index + 1] = Loc.T(RouteDetail(cities[index].Route));
        }

        boardLanguage = Loc.Current;
    }

    private static LocString RouteDetail(MarketBoardRoute route) => route switch
    {
        MarketBoardRoute.Aetheryte => L.Market.BoardAetheryteDetail,
        MarketBoardRoute.Aethernet => L.Market.BoardAethernetDetail,
        _ => L.Market.BoardDistrictDetail,
    };
}
