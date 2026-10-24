using ECommons.DalamudServices;
using System.IO;

namespace AutoTreasureMaps.Core.Dungeon;

// Names the files the captures and recordings write: the time stamp followed by the character, so two game instances on
// one PC that share the config folder each keep a file of their own instead of one locking the other out.
internal static class CaptureFiles
{
    private static string character = string.Empty;

    public static string Name(string stamp)
    {
        // The last character seen stands in while the player object is briefly gone, as it is between zones.
        if (Svc.Objects.LocalPlayer?.Name.TextValue is { Length: > 0 } name)
        {
            character = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
        }

        return character.Length > 0 ? $"{stamp}-{character}.txt" : $"{stamp}.txt";
    }
}
