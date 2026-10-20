using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Windows.Components;

namespace AutoTreasureMaps.Windows.Sections.Config;

internal static class GeneralSettings
{
    private const int MapLimitMax = 200;
    private const int MinuteLimitMax = 600;
    private const int FailureLimitMin = 1;
    private const int FailureLimitMax = 10;

    private static readonly AfterRunAction[] afterRunOrder =
        [AfterRunAction.StayLoggedIn, AfterRunAction.ReturnToInn, AfterRunAction.Logout, AfterRunAction.CloseGame];

    // Ordered like afterRunOrder, because the picked index reads both.
    private static readonly SettingsControls.Choices.Choice[] afterRunChoices =
    [
        new(L.Treasure.AfterStayName, L.Treasure.AfterStayDetail),
        new(L.Treasure.AfterInnName, L.Treasure.AfterInnDetail),
        new(L.Treasure.AfterLogoutName, L.Treasure.AfterLogoutDetail),
        new(L.Treasure.AfterCloseName, L.Treasure.AfterCloseDetail),
    ];

    public static void Draw(Configuration configuration)
    {
        DrawLanguageGroup(configuration);
        DrawWindowGroup(configuration);
        DrawBehaviorGroup(configuration);
        DrawRunGroup(configuration);
        DrawAfterRunGroup(configuration);
    }

    private static void DrawRunGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Run.SettingsGroup));

        var mapFormat = configuration.RunMapLimit == 0 ? Loc.T(L.Run.NoLimit) : Loc.T(L.Safety.MapsFormat);
        SettingsRow.Draw(Loc.T(L.Run.MapLimit),
            Loc.T(L.Run.MapLimitHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(configuration, "##atm_run_maps",
                () => configuration.RunMapLimit, value => configuration.RunMapLimit = value,
                0, MapLimitMax, mapFormat));

        var minuteFormat = configuration.RunMinuteLimit == 0 ? Loc.T(L.Run.NoLimit) : Loc.T(L.Safety.MinutesFormat);
        SettingsRow.Draw(Loc.T(L.Run.MinuteLimit),
            Loc.T(L.Run.MinuteLimitHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(configuration, "##atm_run_minutes",
                () => configuration.RunMinuteLimit, value => configuration.RunMinuteLimit = value,
                0, MinuteLimitMax, minuteFormat));

        SettingsRow.Draw(Loc.T(L.Run.FailureLimit),
            Loc.T(L.Run.FailureLimitHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(configuration, "##atm_run_failures",
                () => configuration.RunFailureLimit, value => configuration.RunFailureLimit = value,
                FailureLimitMin, FailureLimitMax, Loc.T(L.Safety.MapsFormat)));

        SettingsRow.Note(Loc.T(L.Run.AlsoStops));
    }

    private static void DrawLanguageGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.Language));

        SettingsRow.Draw(Loc.T(L.Settings.Language),
            Loc.T(L.Settings.LanguageHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.DrawLanguageCombo(configuration));
    }

    private static void DrawWindowGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralWindow));

        SettingsRow.Draw(Loc.T(L.Settings.OpenOnLogin),
            Loc.T(L.Settings.OpenOnLoginHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoShowOnLogin, value => configuration.AutoShowOnLogin = value, "##atm_general_autoshow"),
            SettingsRow.ToggleHeight);
    }

    private static void DrawBehaviorGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralBehavior));

        SettingsRow.Draw(Loc.T(L.Settings.AutoPause),
            Loc.T(L.Settings.AutoPauseHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoPauseInContent, value => configuration.AutoPauseInContent = value, "##atm_general_autopause"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Session.AutoResume),
            Loc.T(L.Session.AutoResumeHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoResumeOnFault, value => configuration.AutoResumeOnFault = value, "##atm_general_autoresume"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.Tankbuster),
            Loc.T(L.Settings.TankbusterHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.TankbusterMitigation, value => configuration.TankbusterMitigation = value, "##atm_general_tankbuster"),
            SettingsRow.ToggleHeight);
    }

    private static void DrawAfterRunGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Treasure.WhenDone));

        var selected = Math.Max(0, Array.IndexOf(afterRunOrder, configuration.AfterRun));
        SettingsRow.Draw(Loc.T(L.Treasure.WhenDone),
            Loc.T(L.Treasure.WhenDoneHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##atm_general_afterrun", afterRunChoices, selected, choice =>
            {
                configuration.AfterRun = afterRunOrder[choice];
                configuration.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(afterRunChoices[selected].Detail));
    }
}
