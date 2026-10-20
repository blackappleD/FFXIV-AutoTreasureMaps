using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoTreasureMaps.Core.Game;

// Questions another player can put on screen at any moment: a party invite, a teleport offer. A dungeon object's own
// question has no Addon row to match it against, so these are matched instead, from their Addon sheet templates in the
// client's language, and the run never answers them.
internal static class ForeignPrompts
{
    private const uint JoinPartyRow = 120;
    private const uint TeleportOfferRow = 1800;

    private static string[][]? templates;

    public static bool Matches(string prompt)
    {
        templates ??= [Fragments(JoinPartyRow), Fragments(TeleportOfferRow)];
        var folded = GameText.Fold(prompt);
        for (var templateIndex = 0; templateIndex < templates.Length; templateIndex++)
        {
            var fragments = templates[templateIndex];
            if (fragments.Length > 0 && AllIn(folded, fragments))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] Fragments(uint row)
        => Svc.Data.GetExcelSheet<Addon>().GetRowOrDefault(row) is { } addon ? GameText.FixedFragments(addon.Text) : [];

    private static bool AllIn(string folded, string[] fragments)
    {
        for (var fragmentIndex = 0; fragmentIndex < fragments.Length; fragmentIndex++)
        {
            if (!GameText.Contains(folded, fragments[fragmentIndex]))
            {
                return false;
            }
        }

        return true;
    }
}
