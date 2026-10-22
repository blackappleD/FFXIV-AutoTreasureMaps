using ECommons;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Text.RegularExpressions;

namespace AutoTreasureMaps.Core.Dungeon;

// The duty list shows the treasure dungeon's round as "N/M" ("第2/5次挑战" in the Chinese client), the same for every
// member of the party. It is read from the list's text, as the one count that holds however the member got there. Read
// only on the framework thread.
internal static unsafe partial class DutyListRound
{
    private const string AddonName = "_ToDoList";
    private const int SearchDepth = 4;

    // The round shown, when the list shows a count out of the dungeon's own number of rounds; the sack round's count
    // (out of a hundred) and the clock do not match.
    public static bool TryRead(int rounds, out int round)
    {
        round = 0;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AddonName, out var addon) || !addon->IsVisible)
        {
            return false;
        }

        return Search(&addon->UldManager, rounds, SearchDepth, ref round);
    }

    private static bool Search(AtkUldManager* manager, int rounds, int depth, ref int round)
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
                foreach (Match match in Count().Matches(textNode->NodeText.ToString()))
                {
                    if (int.TryParse(match.Groups[2].ValueSpan, out var outOf) && outOf == rounds
                        && int.TryParse(match.Groups[1].ValueSpan, out var shown) && shown >= 1 && shown <= rounds)
                    {
                        round = shown;
                        return true;
                    }
                }

                continue;
            }

            var componentNode = node->GetAsAtkComponentNode();
            if (depth > 0 && componentNode is not null && componentNode->Component is not null
                && Search(&componentNode->Component->UldManager, rounds, depth - 1, ref round))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"(\d+)\s*/\s*(\d+)")]
    private static partial Regex Count();
}
