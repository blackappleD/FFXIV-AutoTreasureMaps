using ECommons;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Text.RegularExpressions;

namespace AutoTreasureMaps.Core.Dungeon;

// The duty list names the treasure dungeon's round, the same for every member of the party, so a member that did not
// open the gates or pull the machine itself can still tell which round it is in. Each dungeon words it differently, so
// the handler reads each line of the list; this walks the list and holds the shared parsers. Read only on the framework
// thread.
internal static unsafe partial class DutyListRound
{
    private const string AddonName = "_ToDoList";
    private const int SearchDepth = 4;
    private const string ChineseNumerals = "一二三四五六七八九";
    private const string ChineseFinal = "最终";

    private delegate bool LineReader(string line, out int value);

    private static readonly LineReader sackScore = ReadSackScore;

    public static bool TryRead(TreasureDungeonHandler handler, out int round)
        => TryReadList(handler.TryReadListedRound, out round);

    // The bonus round's sack points so far, "N/100" in the list (gold counted three), the same for the whole party.
    public static bool TryReadSackScore(out int score) => TryReadList(sackScore, out score);

    private static bool TryReadList(LineReader reader, out int value)
    {
        value = 0;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AddonName, out var addon) || !addon->IsVisible)
        {
            return false;
        }

        return Search(&addon->UldManager, reader, SearchDepth, ref value);
    }

    private static bool ReadSackScore(string line, out int score)
    {
        foreach (Match match in Count().Matches(line))
        {
            if (match.Groups[2].ValueSpan.SequenceEqual("100") && int.TryParse(match.Groups[1].ValueSpan, out score))
            {
                return true;
            }
        }

        score = 0;
        return false;
    }

    // "N/M" with M the dungeon's own number of rounds, as the vault's "第2/5次挑战"; 0 when no such count is in the line.
    public static int CountOutOf(string line, int rounds)
    {
        foreach (Match match in Count().Matches(line))
        {
            if (int.TryParse(match.Groups[2].ValueSpan, out var outOf) && outOf == rounds
                && int.TryParse(match.Groups[1].ValueSpan, out var shown) && shown >= 1 && shown <= rounds)
            {
                return shown;
            }
        }

        return 0;
    }

    // "第一区" to "第四区" (or the digits), and "最终区" for the last; 0 when the line names no chamber.
    public static int ChineseChamber(string line, int rounds)
    {
        var match = Chamber().Match(line);
        if (!match.Success)
        {
            return 0;
        }

        var name = match.Groups[1].Value;
        if (name == ChineseFinal)
        {
            return rounds;
        }

        var number = ChineseNumerals.IndexOf(name, StringComparison.Ordinal) + 1;
        if (number == 0 && !int.TryParse(name, out number))
        {
            return 0;
        }

        return number >= 1 && number <= rounds ? number : 0;
    }

    private static bool Search(AtkUldManager* manager, LineReader reader, int depth, ref int round)
    {
        for (var index = 0; index < manager->NodeListCount; index++)
        {
            var node = manager->NodeList[index];
            if (node is null || !node->IsVisible())
            {
                continue;
            }

            var textNode = node->GetAsAtkTextNode();
            if (textNode is not null)
            {
                if (reader(textNode->NodeText.ToString(), out round))
                {
                    return true;
                }

                continue;
            }

            var componentNode = node->GetAsAtkComponentNode();
            if (depth > 0 && componentNode is not null && componentNode->Component is not null
                && Search(&componentNode->Component->UldManager, reader, depth - 1, ref round))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"(\d+)\s*/\s*(\d+)")]
    private static partial Regex Count();

    [GeneratedRegex(@"(\d|[一二三四五六七八九]|最终)区")]
    private static partial Regex Chamber();
}
