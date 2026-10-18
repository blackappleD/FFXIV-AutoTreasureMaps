using AutoTreasureMaps.Core.Travel;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using NativeTreasure = FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure;

namespace AutoTreasureMaps.Core.Dungeon;

// Records what a treasure dungeon shows while the player clears it by hand: the event objects, coffers and enemies as
// they come and go, every log message with its parameters, the dialogs that open and the zone changes. The dungeon
// profiles are filled in from these recordings, so nothing in them is guessed. Each recording is one plain text file in
// the plugin's config folder, and every line also goes to the console. Read only on the framework thread.
internal sealed unsafe class DungeonRecorder : IDisposable
{
    private const int ScanIntervalFrames = 10;
    private const float ScanMeters = 120f;
    private const string RecordingsFolder = "dungeon-recordings";
    private const string LinePrefix = "Record:";

    private static readonly string[] dialogAddons = ["SelectYesno", "SelectString", "Talk", "TreasureHighLow", "NeedGreed", "ContentsFinderConfirm"];

    private readonly Dictionary<ulong, SeenObject> seen = [];
    private readonly HashSet<ulong> stillHere = [];
    private StreamWriter? writer;
    private int framesUntilScan;
    private uint territoryId;
    private bool betweenAreas;
    private long startedAtMs;

    public static DungeonRecorder Instance { get; } = new();

    public bool Recording => writer is not null;

    public string? FilePath { get; private set; }

    public void Start()
    {
        if (Recording)
        {
            return;
        }

        var directory = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), RecordingsFolder);
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, $"{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt");
        writer = new StreamWriter(FilePath, append: false, Encoding.UTF8) { AutoFlush = true };
        startedAtMs = Environment.TickCount64;
        seen.Clear();
        territoryId = 0;
        framesUntilScan = 0;

        Svc.Framework.Update += OnUpdate;
        Svc.Chat.LogMessage += OnLogMessage;
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, dialogAddons, OnDialogSetup);
        Write($"start: client {Svc.ClientState.ClientLanguage}, plugin {Plugin.PluginInterface.Manifest.AssemblyVersion}");
        NoteTerritory();
    }

    public void Stop()
    {
        if (!Recording)
        {
            return;
        }

        Write($"stop: {seen.Count} object(s) in sight");
        Svc.Framework.Update -= OnUpdate;
        Svc.Chat.LogMessage -= OnLogMessage;
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, dialogAddons, OnDialogSetup);
        writer!.Dispose();
        writer = null;
        seen.Clear();
    }

    public void Dispose() => Stop();

    // One look at everything nearby, for a quick check without a recording.
    public static void DumpNearby()
    {
        var player = Svc.Objects.LocalPlayer;
        var lines = new List<string> { DescribeTerritory(Svc.ClientState.TerritoryType) };
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is { } candidate && Worth(candidate) && (player is null || Vector3.Distance(player.Position, candidate.Position) <= ScanMeters))
            {
                lines.Add(Describe(candidate));
            }
        }

        for (var index = 0; index < lines.Count; index++)
        {
            RunLog.Info($"{LinePrefix} dump: {lines[index]}");
        }

        Svc.Chat.Print($"{AtmConstants.LogPrefix} {lines.Count - 1} object(s) nearby written to the console (/atm log).");
    }

    private void OnUpdate(IFramework framework)
    {
        var zoning = Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];
        if (zoning != betweenAreas)
        {
            betweenAreas = zoning;
            Write(zoning ? "zoning: started" : $"zoning: done at {DescribePlayer()}");
        }

        if (Svc.ClientState.TerritoryType != territoryId)
        {
            NoteTerritory();
        }

        if (--framesUntilScan > 0)
        {
            return;
        }

        framesUntilScan = ScanIntervalFrames;
        Scan();
    }

    private void NoteTerritory()
    {
        territoryId = Svc.ClientState.TerritoryType;
        seen.Clear();
        Write($"territory: {DescribeTerritory(territoryId)}, player at {DescribePlayer()}");
    }

    private void Scan()
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return;
        }

        stillHere.Clear();
        var limit = ScanMeters * ScanMeters;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            var candidate = objects[index];
            if (candidate is null || !Worth(candidate) || Vector3.DistanceSquared(player.Position, candidate.Position) > limit)
            {
                continue;
            }

            var id = candidate.GameObjectId;
            stillHere.Add(id);
            var now = Snapshot(candidate);
            if (!seen.TryGetValue(id, out var before))
            {
                seen[id] = now;
                Write($"appear: {Describe(candidate)}");
                continue;
            }

            if (now != before)
            {
                seen[id] = now;
                Write($"change: {Describe(candidate)} (was targetable {before.Targetable}, dead {before.Dead}, state {before.State})");
            }
        }

        if (stillHere.Count == seen.Count)
        {
            return;
        }

        foreach (var (id, last) in seen.ToArray())
        {
            if (stillHere.Contains(id))
            {
                continue;
            }

            seen.Remove(id);
            Write($"gone: {last.Kind} base {last.BaseId} '{last.Name}' ({id:X})");
        }
    }

    private void OnLogMessage(ILogMessage message)
    {
        var builder = new StringBuilder();
        builder.Append("log ").Append(message.LogMessageId).Append(": params [");
        for (var index = 0; index < message.ParameterCount; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            if (message.TryGetIntParameter(index, out var number))
            {
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
            }
            else if (message.TryGetStringParameter(index, out var text))
            {
                builder.Append('"').Append(text.ExtractText()).Append('"');
            }
            else
            {
                builder.Append('?');
            }
        }

        builder.Append("] \"").Append(Flatten(message.FormatLogMessageForDebugging().ExtractText())).Append('"');
        Write(builder.ToString());
    }

    private void OnDialogSetup(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        var line = new StringBuilder("dialog ").Append(args.AddonName);
        switch (args.AddonName)
        {
            case "SelectYesno":
                line.Append(": \"").Append(Flatten(new AddonMaster.SelectYesno(addon).Text)).Append('"');
                break;
            case "SelectString":
                var entries = new AddonMaster.SelectString(addon).Entries;
                line.Append(": ");
                for (var index = 0; index < entries.Length; index++)
                {
                    line.Append(index).Append("=\"").Append(Flatten(entries[index].Text)).Append("\" ");
                }

                break;
        }

        Write(line.ToString());
    }

    private void Write(string text)
    {
        var elapsed = (Environment.TickCount64 - startedAtMs) / (double)TimeUnits.MillisecondsPerSecond;
        var line = $"[{elapsed,8:F1}s] {text}";
        writer?.WriteLine(line);
        RunLog.Info($"{LinePrefix} {text}");
    }

    private static bool Worth(IGameObject candidate)
        => candidate.ObjectKind switch
        {
            ObjectKind.EventObj or ObjectKind.Treasure or ObjectKind.EventNpc or ObjectKind.Aetheryte => true,
            ObjectKind.BattleNpc => candidate is IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant },
            _ => false,
        };

    private static SeenObject Snapshot(IGameObject candidate)
        => new(candidate.ObjectKind, candidate.BaseId, candidate.Name.TextValue, candidate.IsTargetable, candidate.IsDead, TreasureState(candidate));

    private static int TreasureState(IGameObject candidate)
        => candidate.ObjectKind == ObjectKind.Treasure && candidate.Address != 0 ? (int)((NativeTreasure*)candidate.Address)->State : -1;

    private static string Describe(IGameObject candidate)
    {
        var builder = new StringBuilder();
        builder.Append(candidate.ObjectKind).Append(" base ").Append(candidate.BaseId)
            .Append(" '").Append(candidate.Name.TextValue).Append("' (").Append(candidate.GameObjectId.ToString("X", CultureInfo.InvariantCulture)).Append(')')
            .Append(" at ").Append(FormatVector(candidate.Position))
            .Append(" rot ").Append(candidate.Rotation.ToString("F2", CultureInfo.InvariantCulture))
            .Append(", targetable ").Append(candidate.IsTargetable);
        if (candidate.ObjectKind == ObjectKind.EventObj && Svc.Data.GetExcelSheet<EObjName>().GetRowOrDefault(candidate.BaseId) is { } eobjName)
        {
            builder.Append(", eobj name '").Append(eobjName.Singular.ExtractText()).Append('\'');
        }

        if (candidate.ObjectKind == ObjectKind.Treasure && candidate.Address != 0)
        {
            var treasure = (NativeTreasure*)candidate.Address;
            builder.Append(", coffer kind ").Append(treasure->CofferKind).Append(", state ").Append(treasure->State)
                .Append(", flags ").Append(treasure->Flags).Append(", items ").Append(treasure->ItemCount);
        }

        if (candidate is IBattleNpc npc)
        {
            builder.Append(", hp ").Append(npc.CurrentHp).Append('/').Append(npc.MaxHp).Append(", dead ").Append(npc.IsDead)
                .Append(", name id ").Append(npc.NameId);
        }

        if (Svc.Objects.LocalPlayer is { } player)
        {
            builder.Append(", ").Append(Vector3.Distance(player.Position, candidate.Position).ToString("F1", CultureInfo.InvariantCulture)).Append("m away");
        }

        return builder.ToString();
    }

    private static string DescribeTerritory(uint territory)
    {
        var row = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory);
        var content = row?.ContentFinderCondition.RowId ?? 0;
        return $"{territory} '{TerritoryNames.Of(territory)}', content finder condition {content}";
    }

    private static string DescribePlayer()
        => Svc.Objects.LocalPlayer is { } player ? FormatVector(player.Position) : "nowhere";

    private static string FormatVector(Vector3 position)
        => string.Create(CultureInfo.InvariantCulture, $"({position.X:F2}, {position.Y:F2}, {position.Z:F2})");

    private static string Flatten(string text) => text.Replace('\n', ' ').Replace('\r', ' ');

    private readonly record struct SeenObject(ObjectKind Kind, uint BaseId, string Name, bool Targetable, bool Dead, int State);
}
