using Newtonsoft.Json.Linq;
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

    public const string TargetingTrack = "Targeting";
    // The rotation then attacks only what the character has targeted.
    public const string ManualTargeting = "Manual";

    private static string? serialized;
    private static string[]? targetingModules;

    public static string GetSerialized() => serialized ??= Read();

    // The job rotations that pick their own target, which a fight that wants one enemy first switches to Manual for a while.
    public static string[] TargetingModules => targetingModules ??= ReadTargetingModules();

    private static string[] ReadTargetingModules()
    {
        var modules = JObject.Parse(GetSerialized())["Modules"] as JObject;
        if (modules is null)
        {
            return [];
        }

        var found = new List<string>();
        foreach (var (module, tracks) in modules)
        {
            if (tracks is JArray entries && entries.Any(entry => (string?)entry["Track"] == TargetingTrack))
            {
                found.Add(module);
            }
        }

        return [.. found];
    }

    private static string Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the bundled combat preset '{ResourceName}' is missing from the assembly");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
