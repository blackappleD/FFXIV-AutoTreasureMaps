using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;

namespace AutoTreasureMaps.Windows;

// What the Start button says, from the readiness check the Treasure page already made this frame.
internal static class TreasureLauncher
{
    private static CachedText sublabel;

    public static void Start() => Plugin.Instance.Controller.Start();

    public static string Sublabel(in RunCheck check)
        => check.Follows ? Loc.T(L.Treasure.StartFollow) : sublabel.Get(check.Available, static key => Loc.Plural(L.Treasure.StartMaps, (int)key));

    // Shown only while Start is disabled.
    public static string Reason(in RunCheck check) => check.Blocker switch
    {
        RunBlocker.NeedsPlugins => Loc.T(L.Treasure.ReasonInstall),
        RunBlocker.NoMaps => Loc.T(L.Treasure.ReasonNoMaps),
        RunBlocker.HeldMap => Loc.T(L.Treasure.ReasonHeldMap),
        _ => Loc.T(L.Treasure.ReasonNotReady),
    };
}
