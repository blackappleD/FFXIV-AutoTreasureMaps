using AutoTreasureMaps.Core.Localization;

namespace AutoTreasureMaps.Core.Changelog;

internal static class ChangelogData
{
    public static readonly ChangelogEntry[] Entries =
    [
        new("1.3.0.0", "2026-10-22", L.Changelog.Release1300),
        new("1.2.0.0", "2026-10-21", L.Changelog.Release1200),
        new("1.1.0.0", "2026-10-21", L.Changelog.Release1100),
        new("1.0.0.0", "2026-10-13", L.Changelog.Release1000),
    ];

    public static string LatestVersion => Entries[0].Version;

    // Newest first, so every entry above the one the player last saw is new to them. A version that is no longer
    // listed counts as older than all of them.
    public static int UnseenCount(string lastSeenVersion)
    {
        for (var index = 0; index < Entries.Length; index++)
        {
            if (string.Equals(Entries[index].Version, lastSeenVersion, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return Entries.Length;
    }
}
