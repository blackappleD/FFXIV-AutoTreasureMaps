using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;

namespace AutoTreasureMaps.Windows.Components;

// The label of the patch a map arrived in, cached per language so a settings row draws it without allocating.
internal static class PatchBadge
{
    private static readonly Dictionary<GamePatch, string> labels = [];
    private static LanguageInfo? labelLanguage;

    public static string? Label(GamePatch patch)
    {
        if (!patch.IsKnown)
        {
            return null;
        }

        if (!ReferenceEquals(labelLanguage, Loc.Current))
        {
            labels.Clear();
            labelLanguage = Loc.Current;
        }

        if (!labels.TryGetValue(patch, out var label))
        {
            label = Loc.T(L.Maps.PatchBadge, patch.ToString());
            labels[patch] = label;
        }

        return label;
    }
}
