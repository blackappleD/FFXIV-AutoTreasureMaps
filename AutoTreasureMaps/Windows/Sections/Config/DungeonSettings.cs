using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Windows.Components;

namespace AutoTreasureMaps.Windows.Sections.Config;

internal static class DungeonSettings
{
    // Ordered like GatePolicy, whose value indexes these.
    private static readonly SettingsControls.Choices.Choice[] gateChoices =
    [
        new(L.Dungeon.GateRandomName, L.Dungeon.GateRandomDetail),
        new(L.Dungeon.GateLeftName, L.Dungeon.GateLeftDetail),
        new(L.Dungeon.GateRightName, L.Dungeon.GateRightDetail),
    ];

    private static readonly SettingsControls.Choices.Choice[] gateOverrideChoices =
    [
        new(L.Dungeon.GateRandomName, L.Dungeon.GateRandomDetail),
        new(L.Dungeon.GateLeftName, L.Dungeon.GateLeftDetail),
        new(L.Dungeon.GateRightName, L.Dungeon.GateRightDetail),
        new(L.Dungeon.GateDefaultName, L.Dungeon.GateDefaultDetail),
    ];

    // Ordered like LurePolicy.
    private static readonly SettingsControls.Choices.Choice[] lureChoices =
    [
        new(L.Dungeon.LureAskName, L.Dungeon.LureAskDetail),
        new(L.Dungeon.LureOpenName, L.Dungeon.LureOpenDetail),
    ];

    public static void Draw(Configuration configuration)
    {
        DrawPortal(configuration);
        DrawGates(configuration);
        DrawBonusEnemies(configuration);
        DrawLure(configuration);
    }

    private static void DrawBonusEnemies(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Dungeon.GroupBonus));

        SettingsRow.Draw(Loc.T(L.Dungeon.PreferBonus),
            Loc.T(L.Dungeon.PreferBonusHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.DungeonPreferBonusEnemies, value => configuration.DungeonPreferBonusEnemies = value, "##atm_dungeon_bonus"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Dungeon.MandragorasInOrder),
            Loc.T(L.Dungeon.MandragorasInOrderHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.DungeonMandragorasInOrder, value => configuration.DungeonMandragorasInOrder = value, "##atm_dungeon_mandragoras"),
            SettingsRow.ToggleHeight);

        // Area attacks kill the party out of order, which a solo character cannot avoid, so the warning stands out.
        SettingsRow.Note(Loc.T(L.Dungeon.MandragorasSoloNote), Styling.AccentAmber);
    }

    private static void DrawPortal(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Dungeon.GroupPortal));

        SettingsRow.Draw(Loc.T(L.Dungeon.EnterPortal),
            Loc.T(L.Dungeon.EnterPortalHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.DungeonEnterPortal, value => configuration.DungeonEnterPortal = value, "##atm_dungeon_portal"),
            SettingsRow.ToggleHeight);
    }

    private static void DrawGates(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Dungeon.GroupGates));

        var selected = Math.Clamp((int)configuration.DungeonGatePolicy, 0, gateChoices.Length - 1);
        SettingsRow.Draw(Loc.T(L.Dungeon.GatePolicy),
            Loc.T(L.Dungeon.GatePolicyHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##atm_dungeon_gate", gateChoices, selected, choice =>
            {
                configuration.DungeonGatePolicy = (GatePolicy)choice;
                configuration.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(gateChoices[selected].Detail));

        SettingsRow.Draw(Loc.T(L.Dungeon.FollowHint),
            Loc.T(L.Dungeon.FollowHintHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.DungeonFollowGateHint, value => configuration.DungeonFollowGateHint = value, "##atm_dungeon_hint"),
            SettingsRow.ToggleHeight);

        var handlers = DungeonRegistry.All;
        for (var index = 0; index < handlers.Length; index++)
        {
            var profile = handlers[index].Profile;
            if (profile.Kind != DungeonKind.Gate)
            {
                continue;
            }

            DrawOverride(configuration, profile);
        }
    }

    private static void DrawOverride(Configuration configuration, DungeonProfile profile)
    {
        var key = profile.Key;
        var current = configuration.DungeonGatePolicyOverrides.TryGetValue(key, out var policy) ? policy : GatePolicy.Default;
        var selected = Math.Clamp((int)current, 0, gateOverrideChoices.Length - 1);
        SettingsRow.Draw(TerritoryNames.Of(profile.TerritoryId),
            Loc.T(L.Dungeon.PerDungeonHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo($"##atm_dungeon_gate_{key}", gateOverrideChoices, selected, choice =>
            {
                var picked = (GatePolicy)choice;
                if (picked == GatePolicy.Default)
                {
                    configuration.DungeonGatePolicyOverrides.Remove(key);
                }
                else
                {
                    configuration.DungeonGatePolicyOverrides[key] = picked;
                }

                configuration.SaveDebounced();
            }));
    }

    private static void DrawLure(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Dungeon.GroupLure));

        var selected = Math.Clamp((int)configuration.DungeonLurePolicy, 0, lureChoices.Length - 1);
        SettingsRow.Draw(Loc.T(L.Dungeon.LurePolicy),
            Loc.T(L.Dungeon.LurePolicyHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##atm_dungeon_lure", lureChoices, selected, choice =>
            {
                configuration.DungeonLurePolicy = (LurePolicy)choice;
                configuration.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(lureChoices[selected].Detail));
    }
}
