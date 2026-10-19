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
    private const float SearchWidth = 340f;
    private const float AddButtonWidth = 96f;
    private const int MaxResults = 20;
    private const int MaxQuantityStep = 10;
    private const int MaxQuantityLimit = 9_999;
    private const string KeptListId = "##atm_cleanup_kept";
    private const string ResultListId = "##atm_cleanup_results";

    private static readonly List<uint> results = [];
    private static readonly List<string> resultNames = [];
    private static string query = string.Empty;
    private static bool queryFocused;
    private static int matchCount;

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

        // The one rule a player must not miss, so it stands apart from the help and in an accent colour, on or off.
        SettingsRow.Note(Loc.T(L.Cleanup.Safety), Styling.AccentAmber);

        using var body = Motion.PushSwitch("##atm_cleanup_body", configuration.AutoDiscard);
        SettingsRow.Note(Loc.T(configuration.AutoDiscard ? L.Cleanup.EnableHelp : L.Cleanup.Off));
        if (!configuration.AutoDiscard)
        {
            return;
        }

        SettingsRow.Draw(Loc.T(L.Cleanup.MaxQuantity),
            Loc.T(L.Cleanup.MaxQuantityHelp),
            Stepper.DefaultWidth,
            () => DrawMaxQuantity(configuration));
    }

    private static void DrawMaxQuantity(Configuration configuration)
    {
        var value = configuration.AutoDiscardMaxQuantity;
        if (!Stepper.Draw("##atm_cleanup_max", ref value, MaxQuantityStep, 0, MaxQuantityLimit, Loc.T(L.Cleanup.MaxQuantityFormat)))
        {
            return;
        }

        configuration.AutoDiscardMaxQuantity = value;
        configuration.SaveDebounced();
        LootCleanup.ApplyLimit();
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

    // Any item in the game, by part of its name or its id; the matches are searched again only when the text changes.
    private static void DrawAddRow()
    {
        if (SearchField.Draw("##atm_cleanup_search", Loc.T(L.Cleanup.SearchHint), ref query, ref queryFocused, SearchWidth * ImGuiHelpers.GlobalScale, Layout.ActionPillHeight))
        {
            Search();
        }

        if (query.Trim().Length == 0)
        {
            return;
        }

        if (results.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Cleanup.NoMatch));
            return;
        }

        var buttonSize = new Vector2(AddButtonWidth * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeight());
        var added = 0u;
        for (var index = 0; index < results.Count; index++)
        {
            var itemId = results[index];
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            {
                ImGui.TextUnformatted(resultNames[index]);
            }

            ImGui.SameLine(SettingsGroup.InnerRightLocalX() - buttonSize.X);
            if (LootCleanup.IsKept(itemId))
            {
                ImGui.AlignTextToFramePadding();
                using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                {
                    ImGui.TextUnformatted(Loc.T(L.Safety.AlreadyAdded));
                }

                continue;
            }

            ImGui.PushID(ResultListId);
            ImGui.PushID(index);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
            {
                if (ImGui.Button(Loc.T(L.Safety.Add), buttonSize))
                {
                    added = itemId;
                }
            }

            ImGui.PopID();
            ImGui.PopID();
        }

        if (matchCount > results.Count)
        {
            SettingsRow.Note(Loc.Plural(L.Cleanup.MoreMatches, matchCount - results.Count));
        }

        if (added != 0)
        {
            LootCleanup.AddToWhitelist(added);
        }
    }

    private static void Search()
    {
        matchCount = ItemSearch.Find(query, results, MaxResults);
        resultNames.Clear();
        for (var index = 0; index < results.Count; index++)
        {
            resultNames.Add(Loc.T(L.Cleanup.ResultLabel, NotableLoot.Name(results[index]), results[index]));
        }
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
