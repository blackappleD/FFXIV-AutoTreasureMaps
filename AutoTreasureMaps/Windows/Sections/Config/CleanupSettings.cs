using AutoTreasureMaps.Core.Cleanup;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
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
    private const string RareListId = "##atm_cleanup_rare";
    private const float RareHeaderHeight = 30f;
    private const float RareIndent = 22f;
    private const float RareChipHeight = 26f;
    private const float RareChipGap = 6f;

    private static readonly List<uint> results = [];
    private static readonly List<string> resultNames = [];
    private static string query = string.Empty;
    private static bool queryFocused;
    private static int matchCount;

    // Which rare loot sources are unfolded, by their index in RareLoot.Sources.
    private static readonly HashSet<int> openSources = [];
    private static LanguageInfo? rareLanguage;
    private static string[] sourceTitles = [];
    private static string[][] sourceItemNames = [];

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

        SettingsRow.DrawBlock(Loc.T(L.Cleanup.Rare),
            Loc.T(L.Cleanup.RareHelp),
            DrawRareLoot);
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

    // One foldable row per map and per dungeon behind its portal; unfolded, each rare item is a chip that is lit while
    // the item is kept and dimmed once the player picks it to be discarded. An item listed under several sources is one
    // choice, so it switches under all of them.
    private static void DrawRareLoot()
    {
        SettingsRow.Note(Loc.T(L.Cleanup.RareWarning), Styling.AccentAmber);
        Styling.VSpace(6f);
        BuildRareNames();
        var sources = RareLoot.Sources;
        var toggled = 0u;
        for (var index = 0; index < sources.Length; index++)
        {
            var items = sources[index].Items;
            var kept = 0;
            for (var item = 0; item < items.Length; item++)
            {
                if (LootCleanup.KeepsRare(items[item]))
                {
                    kept++;
                }
            }

            var open = openSources.Contains(index);
            if (DrawSourceHeader(index, sourceTitles[index], Loc.T(L.Cleanup.RareKept, kept, items.Length), open))
            {
                open = !open;
                if (open)
                {
                    openSources.Add(index);
                }
                else
                {
                    openSources.Remove(index);
                }
            }

            using var body = Motion.PushSection($"{RareListId}_body{index}", open);
            if (body is null)
            {
                continue;
            }

            var picked = DrawRareChips(index, items, sourceItemNames[index]);
            if (picked != 0)
            {
                toggled = picked;
            }
        }

        if (toggled != 0)
        {
            LootCleanup.SetRareKept(toggled, !LootCleanup.KeepsRare(toggled));
        }
    }

    private static bool DrawSourceHeader(int index, string title, string count, bool open)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(SettingsGroup.InnerRightLocalX() - ImGui.GetCursorPosX(), RareHeaderHeight * scale);
        var id = $"{RareListId}_head{index}";
        var hit = Hit.Area(id, size);
        var hover = Motion.Hover(Motion.Key(id), hit.Hovered);
        var dl = ImGui.GetWindowDrawList();
        if (hover > 0f)
        {
            Paint.Fill(dl, origin, origin + size, Styling.WithAlpha(Styling.Surface2, 0.7f * hover), 6f * scale);
        }

        var midY = origin.Y + size.Y * 0.5f;
        var pad = 6f * scale;
        var chevron = open ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight;
        var chevronSize = TextDraw.IconSize(chevron);
        TextDraw.Icon(chevron, new Vector2(origin.X + pad, midY - chevronSize.Y * 0.5f), Vector4.Lerp(Styling.TextDim, Styling.TextStrong, hover));

        var x = origin.X + RareIndent * scale;
        var right = origin.X + size.X - pad;
        var countSize = TextDraw.Measure(count);
        TextDraw.Right(count, right, midY - countSize.Y * 0.5f, Styling.TextMuted);
        var shown = TextDraw.Truncate(title, right - countSize.X - 12f * scale - x);
        TextDraw.At(shown, new Vector2(x, midY - TextDraw.Measure(shown).Y * 0.5f), Styling.TextStrong);
        return hit.Clicked;
    }

    // Chips laid out left to right, wrapping at the card's inner edge. Returns the item clicked, or 0.
    private static uint DrawRareChips(int sourceIndex, uint[] items, string[] names)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var indent = RareIndent * scale;
        var gap = RareChipGap * scale;
        var height = RareChipHeight * scale;
        ImGui.Indent(indent);
        Styling.VSpace(4f);
        var clicked = 0u;
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
        {
            var left = ImGui.GetCursorPosX();
            var right = SettingsGroup.InnerRightLocalX();
            var x = left;
            for (var index = 0; index < items.Length; index++)
            {
                var width = FilterChip.Width(names[index], null);
                if (index > 0 && x + gap + width <= right)
                {
                    ImGui.SameLine();
                    x += gap + width;
                }
                else
                {
                    x = left + width;
                }

                if (FilterChip.Draw($"{RareListId}_{sourceIndex}_{index}", names[index], null, Styling.AccentMint, LootCleanup.KeepsRare(items[index]), height))
                {
                    clicked = items[index];
                }
            }
        }

        Styling.VSpace(6f);
        ImGui.Unindent(indent);
        return clicked;
    }

    // Names looked up once per language instead of on every frame: a map by its item, a dungeon by its duty.
    private static void BuildRareNames()
    {
        if (ReferenceEquals(rareLanguage, Loc.Current))
        {
            return;
        }

        rareLanguage = Loc.Current;
        var sources = RareLoot.Sources;
        sourceTitles = new string[sources.Length];
        sourceItemNames = new string[sources.Length][];
        for (var index = 0; index < sources.Length; index++)
        {
            var source = sources[index];
            sourceTitles[index] = source.IsDungeon ? DutyName(source.ContentFinderConditionId) : NotableLoot.Name(source.MapItemId);
            var names = new string[source.Items.Length];
            for (var item = 0; item < names.Length; item++)
            {
                names[item] = NotableLoot.Name(source.Items[item]);
            }

            sourceItemNames[index] = names;
        }
    }

    private static string DutyName(uint contentFinderConditionId)
    {
        var name = Svc.Data.GetExcelSheet<ContentFinderCondition>().GetRowOrDefault(contentFinderConditionId)?.Name.ExtractText();
        if (string.IsNullOrEmpty(name))
        {
            return contentFinderConditionId.ToString(Loc.Culture);
        }

        return GameGlyphs.Cover(char.ToUpper(name[0], Loc.Culture) + name[1..]);
    }
}
