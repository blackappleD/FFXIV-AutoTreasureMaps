using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

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

    // A dungeon's own bonus setting while the one above is on: first, or not first.
    private static readonly SettingsControls.Choices.Choice[] bonusOverrideChoices =
    [
        new(L.Dungeon.BonusFirstName, L.Dungeon.BonusFirstDetail),
        new(L.Dungeon.BonusLastName, L.Dungeon.BonusLastDetail),
    ];

    private const float BonusIndent = 22f;

    private static bool bonusOverridesOpen;

    // Ordered like LurePolicy.
    private static readonly SettingsControls.Choices.Choice[] lureChoices =
    [
        new(L.Dungeon.LureAskName, L.Dungeon.LureAskDetail),
        new(L.Dungeon.LureOpenName, L.Dungeon.LureOpenDetail),
        new(L.Dungeon.LureAutoName, L.Dungeon.LureAutoDetail),
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

        if (configuration.DungeonPreferBonusEnemies)
        {
            DrawBonusOverrides(configuration);
        }

        SettingsRow.Draw(Loc.T(L.Dungeon.MandragorasInOrder),
            Loc.T(L.Dungeon.MandragorasInOrderHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.DungeonMandragorasInOrder, value => configuration.DungeonMandragorasInOrder = value, "##atm_dungeon_mandragoras"),
            SettingsRow.ToggleHeight);

        // Area attacks kill the party out of order, which a solo character cannot avoid, so the warning stands out.
        SettingsRow.Note(Loc.T(L.Dungeon.MandragorasSoloNote), Styling.AccentAmber);
    }

    // Shown only while bonus enemies come first, indented under that toggle. One row per dungeon would outgrow the card
    // as dungeons are added, so they sit under a row that opens them, closed until clicked, with how many dungeons leave
    // their bonus enemies alone on the right.
    private static void DrawBonusOverrides(Configuration configuration)
    {
        var custom = 0;
        foreach (var prefer in configuration.DungeonPreferBonusOverrides.Values)
        {
            if (!prefer)
            {
                custom++;
            }
        }

        var indent = BonusIndent * ImGuiHelpers.GlobalScale;
        ImGui.Indent(indent);
        DrawBonusOverrideRows(configuration, custom);
        ImGui.Unindent(indent);
    }

    private static void DrawBonusOverrideRows(Configuration configuration, int custom)
    {
        var summary = custom == 0 ? Loc.T(L.Dungeon.PerDungeonNone) : Loc.Plural(L.Dungeon.PerDungeonCustom, custom);
        if (SettingsRow.Disclosure("##atm_dungeon_bonus_each", Loc.T(L.Dungeon.PerDungeonBonus), summary, bonusOverridesOpen))
        {
            bonusOverridesOpen = !bonusOverridesOpen;
        }

        using var body = Motion.PushSection("atm_dungeon_bonus_each_body", bonusOverridesOpen);
        if (body is null)
        {
            return;
        }

        var handlers = DungeonRegistry.All;
        for (var index = 0; index < handlers.Length; index++)
        {
            var profile = handlers[index].Profile;
            if (profile.BonusNameIds.Length > 0)
            {
                DrawBonusOverride(configuration, profile);
            }
        }
    }

    private static void DrawBonusOverride(Configuration configuration, DungeonProfile profile)
    {
        var key = profile.Key;
        var selected = configuration.DungeonPreferBonusOverrides.TryGetValue(key, out var prefer) && !prefer ? 1 : 0;
        SettingsRow.Draw(TerritoryNames.Of(profile.TerritoryId),
            Loc.T(L.Dungeon.PerDungeonBonusHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo($"##atm_dungeon_bonus_{key}", bonusOverrideChoices, selected, choice =>
            {
                if (choice == 0)
                {
                    configuration.DungeonPreferBonusOverrides.Remove(key);
                }
                else
                {
                    configuration.DungeonPreferBonusOverrides[key] = false;
                }

                configuration.SaveDebounced();
            }));
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
