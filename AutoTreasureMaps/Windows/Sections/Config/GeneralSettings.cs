using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Windows.Components;

namespace AutoTreasureMaps.Windows.Sections.Config;

internal static class GeneralSettings
{
    private const int MapLimitMax = 200;
    private const int MinuteLimitMax = 600;
    private const int FailureLimitMin = 1;
    private const int FailureLimitMax = 10;
    private const float RaiseJobPanelWidth = 320f;

    private static string[]? raiseJobNames;
    private static string[]? raiseJobDetails;

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
        DrawPartyGroup(configuration);
        DrawRunGroup(configuration);
        DrawAfterRunGroup(configuration);
        PostRunSettings.Draw(configuration);
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

        SettingsRow.Draw(Loc.T(L.Settings.DragAnywhere),
            Loc.T(L.Settings.DragAnywhereHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.DragWindowFromAnywhere, value => configuration.DragWindowFromAnywhere = value, "##atm_general_draganywhere"),
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

        SettingsRow.Draw(Loc.T(L.Settings.LootPrices),
            Loc.T(L.Settings.LootPricesHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.LootPriceEstimates, value => configuration.LootPriceEstimates = value, "##atm_general_lootprices"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.Tankbuster),
            Loc.T(L.Settings.TankbusterHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.TankbusterMitigation, value => configuration.TankbusterMitigation = value, "##atm_general_tankbuster"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.AutoFollowLeader),
            Loc.T(L.Settings.AutoFollowLeaderHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoFollowLeader, value => configuration.AutoFollowLeader = value, "##atm_general_autofollow"),
            SettingsRow.ToggleHeight);
    }

    private static void DrawPartyGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.Party));

        SettingsRow.Draw(Loc.T(L.Settings.AutoAcceptRaise),
            Loc.T(L.Settings.AutoAcceptRaiseHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoAcceptRaise, value => configuration.AutoAcceptRaise = value, "##atm_general_acceptraise"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.RaiseAfterFight),
            Loc.T(L.Settings.RaiseAfterFightHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.RaiseAfterFight, value => configuration.RaiseAfterFight = value, "##atm_general_raiseafter"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSection("##atm_general_raisejob_section", configuration.RaiseAfterFight);
        if (body is null)
        {
            return;
        }

        var selected = Math.Max(0, Array.IndexOf(RaiseOps.ClassJobIds, configuration.RaiseClassJobId));
        SettingsRow.Draw(Loc.T(L.Settings.RaiseJob),
            Loc.T(L.Settings.RaiseJobHelp),
            SettingsControls.RowComboWidth,
            () => DrawRaiseJobCombo(configuration, selected));
        if (!GearsetSwitcher.HasGearsetFor(RaiseOps.ClassJobIds[selected], GearsetSwitcher.NoGearset))
        {
            SettingsRow.Note(Loc.T(L.Settings.RaiseJobNoGearset), Styling.AccentAmberSoft);
        }
    }

    // The jobs and spells are named as the game client names them, so they read as they do in game.
    private static void DrawRaiseJobCombo(Configuration configuration, int selected)
    {
        if (raiseJobNames is null || raiseJobDetails is null)
        {
            raiseJobNames = new string[RaiseOps.ClassJobIds.Length];
            raiseJobDetails = new string[RaiseOps.ClassJobIds.Length];
            var swiftcast = RaiseOps.ActionName(RaiseOps.SwiftcastActionId);
            for (var index = 0; index < RaiseOps.ClassJobIds.Length; index++)
            {
                var classJobId = RaiseOps.ClassJobIds[index];
                raiseJobNames[index] = RaiseOps.JobName(classJobId);
                raiseJobDetails[index] = $"{swiftcast} + {RaiseOps.ActionName(RaiseOps.RaiseActionFor(classJobId))}";
            }
        }

        var picked = selected;
        if (Dropdown.DrawDetailed("##atm_general_raisejob", raiseJobNames, raiseJobDetails, ref picked, SettingsControls.RowComboWidth, RaiseJobPanelWidth)
            && picked != selected)
        {
            configuration.RaiseClassJobId = RaiseOps.ClassJobIds[picked];
            configuration.SaveDebounced();
        }
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
