using System.IO;
using System.Reflection;

namespace AutoTreasureMaps.Core.Ipc;

// The combat plugin preset the run fights with, kept as TreasureCombatPreset.json next to this file: AutoDuty's own
// preset, renamed. Every job rotation picks its targets and its area attacks itself (Targeting Auto, AOE AOE), and
// AutoTarget retargets to any hostile nearby, so a wave falls to area attacks instead of one mob at a time. The role
// modules keep the character alive (second wind, mitigation, heals, stuns), and Pathfind movement closes to range and
// dodges once the fight is on.
internal static class TreasureCombatPreset
{
    public const string Name = "TreasureHelper";

    // Bumped whenever the JSON changes, so the preset in the combat plugin is rewritten once.
    public const int Revision = 2;

    private const string ResourceName = "AutoTreasureMaps.Core.Ipc.TreasureCombatPreset.json";

    private static string? serialized;

    public static string GetSerialized() => serialized ??= Read();

    private static string Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the bundled combat preset '{ResourceName}' is missing from the assembly");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
