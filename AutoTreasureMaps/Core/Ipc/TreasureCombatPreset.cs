using System.IO;
using System.Reflection;

namespace AutoTreasureMaps.Core.Ipc;

// The combat plugin preset the run fights with, kept as TreasureCombatPreset.json next to this file: AutoDuty's own
// preset, renamed, except AutoTarget. Every job rotation picks its targets and its area attacks itself (Targeting Auto,
// AOE AOE), and the role modules keep the character alive. AutoTarget is the run's own: Aggressive with every category
// off and Retarget NoTarget, so it only ever takes a mob that is attacking the party, and only while the character has
// no target; out in the field, AutoDuty's Everything would pull every wandering mob near the coffer.
internal static class TreasureCombatPreset
{
    public const string Name = "TreasureHelper";

    // Bumped whenever the JSON changes, so the preset in the combat plugin is rewritten once.
    public const int Revision = 3;

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
