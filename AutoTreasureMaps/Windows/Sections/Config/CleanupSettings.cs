using AutoTreasureMaps.Core.Cleanup;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoTreasureMaps.Windows.Sections.Config;

internal static class CleanupSettings
{
    private const float PickerWidth = 340f;
    private const float AddButtonWidth = 96f;
    private const string KeptListId = "##atm_cleanup_kept";
    // Bag contents change slowly next to the frame rate, so the picker reads them about once a second.
    private const long PickerRefreshMs = 1_000;

    private static readonly List<uint> pickerItems = [];
    private static string[] pickerLabels = [];
    private static long pickerRefreshAtMs;
    private static int pickerSelection;

    private static LanguageInfo? alwaysKeptLanguage;
    private static string alwaysKeptText = string.Empty;

    public static void Draw(Configuration configuration)
    {
        DrawToggleGroup(configuration);
        using var whitelist = Motion.PushSection("##atm_cleanup_whitelist_section", configuration.AutoDiscard);
        if (whitelist is null)
        {
            return;
        }

        DrawWhitelistGroup(configuration);
    }

    private static void DrawToggleGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Cleanup.Group));

        SettingsRow.Draw(Loc.T(L.Cleanup.Enable),
            Loc.T(L.Cleanup.EnableHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoDiscard, SetAutoDiscard, "##atm_cleanup_on"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSwitch("##atm_cleanup_body", configuration.AutoDiscard);
        SettingsRow.Note(Loc.T(configuration.AutoDiscard ? L.Cleanup.EnableHelp : L.Cleanup.Off));
    }

    private static void SetAutoDiscard(bool value)
    {
        Plugin.Instance.Configuration.AutoDiscard = value;
        if (!value)
        {
            LootCleanup.Clear();
        }
    }

    private static void DrawWhitelistGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Cleanup.Whitelist));

        SettingsRow.DrawBlock(Loc.T(L.Cleanup.AddItem),
            Loc.T(L.Cleanup.AddItemHelp),
            DrawAddRow);

        SettingsRow.DrawBlock(Loc.T(L.Cleanup.KeptItems),
            Loc.T(L.Cleanup.KeptItemsHelp),
            () => DrawKeptList(configuration));

        SettingsRow.DrawBlock(Loc.T(L.Cleanup.AlwaysKept),
            Loc.T(L.Cleanup.AlwaysKeptHelp),
            () => SettingsRow.Note(AlwaysKeptText()));
    }

    // Only what is in the bags right now, since the whole game item list is noise here.
    private static void DrawAddRow()
    {
        RefreshPicker();
        if (pickerItems.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Cleanup.NoneInBag));
            return;
        }

        pickerSelection = Math.Clamp(pickerSelection, 0, pickerItems.Count - 1);
        SettingsControls.DrawSearchableCombo("##atm_cleanup_picker", pickerLabels, ref pickerSelection, PickerWidth);

        ImGui.SameLine();
        var buttonSize = new Vector2(AddButtonWidth * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeight());
        bool clicked;
        ImGui.PushID("##atm_cleanup_add");
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
        {
            clicked = ImGui.Button(Loc.T(L.Safety.Add), buttonSize);
        }

        ImGui.PopID();

        if (!clicked)
        {
            return;
        }

        LootCleanup.AddToWhitelist(pickerItems[pickerSelection]);
        pickerRefreshAtMs = 0;
    }

    private static void DrawKeptList(Configuration configuration)
    {
        var items = configuration.AutoDiscardKeep;
        if (items.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Cleanup.NoKept));
            return;
        }

        var removeIndex = -1;
        for (var index = 0; index < items.Count; index++)
        {
            ListRows.Ordinal(index);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            {
                ImGui.TextUnformatted(NotableLoot.Name(items[index]));
            }

            if (ListRows.RemoveButton(KeptListId, index, Loc.T(L.Safety.Remove)))
            {
                removeIndex = index;
            }
        }

        if (removeIndex < 0)
        {
            return;
        }

        LootCleanup.RemoveFromWhitelist(items[removeIndex]);
        pickerRefreshAtMs = 0;
    }

    private static void RefreshPicker()
    {
        var now = Environment.TickCount64;
        if (now < pickerRefreshAtMs)
        {
            return;
        }

        pickerRefreshAtMs = now + PickerRefreshMs;
        LootCleanup.FillWhitelistCandidates(pickerItems);
        if (pickerLabels.Length != pickerItems.Count)
        {
            pickerLabels = new string[pickerItems.Count];
        }

        for (var index = 0; index < pickerItems.Count; index++)
        {
            pickerLabels[index] = NotableLoot.Name(pickerItems[index]);
        }
    }

    // Formatted once per language instead of on every frame.
    private static string AlwaysKeptText()
    {
        if (ReferenceEquals(alwaysKeptLanguage, Loc.Current))
        {
            return alwaysKeptText;
        }

        alwaysKeptLanguage = Loc.Current;
        var items = HighValueLoot.AllItems;
        var names = new string[items.Length];
        for (var index = 0; index < items.Length; index++)
        {
            names[index] = NotableLoot.Name(items[index]);
        }

        alwaysKeptText = string.Join(", ", names);
        return alwaysKeptText;
    }
}
