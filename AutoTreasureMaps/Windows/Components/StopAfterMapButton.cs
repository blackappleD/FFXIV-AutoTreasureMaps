using AutoTreasureMaps.Core.Localization;
using Dalamud.Interface;

namespace AutoTreasureMaps.Windows.Components;

// Asks the run to end once the map under way is over; mint while that is set, and a second click takes it back.
internal static class StopAfterMapButton
{
    public static bool Draw(bool armed, float width = 0f)
        => HeroButton.Draw(FontAwesomeIcon.FlagCheckered, Loc.T(L.Treasure.StopAfterMap),
            Loc.T(armed ? L.Treasure.StopAfterMapArmedSub : L.Treasure.StopAfterMapSub),
            armed ? Styling.AccentMint : Styling.AccentBlue, true, null, width);
}
