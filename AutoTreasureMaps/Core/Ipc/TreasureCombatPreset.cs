using System.Buffers;
using System.Text;
using System.Text.Json;

namespace AutoTreasureMaps.Core.Ipc;

// AutoTarget stays Aggressive with every category off and Retarget NoTarget. The combat plugin ranks a mob that is not
// fighting the party below zero and Aggressive only picks from zero up, so it only ever takes an attacker, and only while
// the character has no target: the current target keeps focus, and whatever is still attacking takes over once it is dead.
// Every rotation picks its own targets and area attacks (Targeting Auto): every mob that spawns at a coffer is hostile,
// so spreading the attacks over a wave only ends it sooner. FATE targeting stays off.
// Tracks left out resolve to the combat plugin's own defaults; the role modules are only switched on, so they heal,
// mitigate and stun with those defaults. Nothing that acts on other players or on mobs outside the fight is enabled.
// Pathfind movement lets the combat plugin close to range and dodge once the fight is on.
internal static class TreasureCombatPreset
{
    public const string Name = "TreasureHelper";

    // Bumped whenever the preset changes, so the copy in the combat plugin is rewritten once.
    public const int Revision = 4;

    private const string AutoTargetModule = "BossMod.Autorotation.MiscAI.AutoTarget";
    private const string NormalMovementModule = "BossMod.Autorotation.MiscAI.NormalMovement";
    private const string LeylinesModule = "BossMod.Autorotation.MiscAI.StayWithinLeylines";
    private const string WarriorModule = "BossMod.Autorotation.VeynWAR";
    private const string ViperModule = "BossMod.Autorotation.xan.VPR";
    private const string RotationModulePrefix = "BossMod.Autorotation.xan.";

    private static readonly string[] rotationJobs =
    [
        "AST", "BLM", "BRD", "BST", "DNC", "DRG", "DRK", "GNB", "MCH", "MNK", "NIN",
        "PCT", "PLD", "RDM", "RPR", "SAM", "SCH", "SGE", "SMN", "WHM",
    ];

    private static readonly string[] roleModules = ["Caster", "HealerAI", "MeleeAI", "RangedAI", "TankAI"];

    private static readonly PresetTrack[] autoTargetTracks =
    [
        new("General", "Aggressive"),
        new("Retarget", "NoTarget"),
        new("FATE", "Disabled"),
    ];

    private static readonly PresetTrack[] movementTracks = [new("Destination", "Pathfind")];
    private static readonly PresetTrack[] leylinesTracks = [new("Use Retrace", "Yes"), new("Use Between The Lines", "Yes")];
    private static readonly PresetTrack[] rotationTracks = [new("Targeting", "Auto")];
    private static readonly PresetTrack[] viperTracks = [new("Targeting", "Auto"), new("WrithingSnap", "Ranged")];

    // The warrior rotation defaults to single target, so it is told to finish its area combo and spend its gauge.
    private static readonly PresetTrack[] warriorTracks =
    [
        new("AOE", "AutoFinishCombo"),
        new("Burst", "Spend"),
        new("Infuriate", "ForceIfNoNC"),
        new("Tomahawk", "Opener"),
    ];

    public const string TargetingTrack = "Targeting";
    // The rotation then attacks only what the character has targeted.
    public const string ManualTargeting = "Manual";

    private static string? serialized;
    private static string[]? targetingModules;

    public static string GetSerialized() => serialized ??= Serialize();

    // The job rotations that pick their own target, which a fight that wants one enemy first switches to Manual for a while.
    public static string[] TargetingModules => targetingModules ??= BuildTargetingModules();

    private static string[] BuildTargetingModules()
    {
        var modules = new string[rotationJobs.Length + 1];
        for (var jobIndex = 0; jobIndex < rotationJobs.Length; jobIndex++)
        {
            modules[jobIndex] = RotationModulePrefix + rotationJobs[jobIndex];
        }

        modules[rotationJobs.Length] = ViperModule;
        return modules;
    }

    private static string Serialize()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("Name", Name);
            writer.WriteStartObject("Modules");
            WriteModule(writer, AutoTargetModule, autoTargetTracks);
            WriteModule(writer, NormalMovementModule, movementTracks);
            WriteModule(writer, LeylinesModule, leylinesTracks);
            WriteModule(writer, WarriorModule, warriorTracks);
            WriteModule(writer, ViperModule, viperTracks);
            for (var jobIndex = 0; jobIndex < rotationJobs.Length; jobIndex++)
            {
                WriteModule(writer, RotationModulePrefix + rotationJobs[jobIndex], rotationTracks);
            }

            for (var roleIndex = 0; roleIndex < roleModules.Length; roleIndex++)
            {
                WriteModule(writer, RotationModulePrefix + roleModules[roleIndex], []);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteModule(Utf8JsonWriter writer, string module, ReadOnlySpan<PresetTrack> tracks)
    {
        writer.WriteStartArray(module);
        for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
        {
            writer.WriteStartObject();
            writer.WriteString("Track", tracks[trackIndex].Track);
            writer.WriteString("Option", tracks[trackIndex].Option);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private readonly record struct PresetTrack(string Track, string Option);
}
