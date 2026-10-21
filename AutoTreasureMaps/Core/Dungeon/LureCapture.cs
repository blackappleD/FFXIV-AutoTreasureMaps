using AutoTreasureMaps.Core.Localization;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using NativeEventObject = FFXIVClientStructs.FFXIV.Client.Game.Object.EventObject;
using NativeDrawObject = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.DrawObject;
using NativeGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using NativeTreasure = FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure;

namespace AutoTreasureMaps.Core.Dungeon;

// Writes down everything the Gambler's Lure shows and does, so the high-or-low game can be read and answered later:
// the lure window's values and visible nodes whenever they change, the events and callbacks its buttons fire, the
// lure's log messages and the item ids of the coffer it sits on. The cards are dealt after the window closes, so from
// the trap springing until the game says how it ended, every other window that opens, every callback, log message,
// condition change and object that comes or goes nearby is written down too. Always on, and quiet until a lure
// springs. Each day is one plain text file in the plugin's config folder. Read only on the framework thread.
internal sealed unsafe class LureCapture : IDisposable
{
    private const string AddonName = "TreasureHighLow";
    private const string CapturesFolder = "lure-captures";
    private const int PollIntervalFrames = 5;
    private const int NodeDepth = 4;
    private const float CofferMeters = 8f;
    private const uint FirstLureMessage = DungeonLogMessages.LureTied;
    private const uint LastLureMessage = DungeonLogMessages.LureTemptedAnother;
    private const float FieldMeters = 20f;
    private const float CardMeters = 4f;
    private const int MaxCardChanges = 96;
    private const ulong ModelKey = 1UL << 63;

    private static readonly AddonEvent[] snapshotEvents = [AddonEvent.PostSetup, AddonEvent.PostRefresh, AddonEvent.PostRequestedUpdate];
    private static readonly AddonEvent[] otherAddonEvents = [AddonEvent.PostSetup, AddonEvent.PostRefresh, AddonEvent.PreFinalize];

    private readonly Hook<AtkUnitBase.Delegates.FireCallback>? callbackHook;
    private readonly Dictionary<ulong, byte[]> cardMemory = [];
    private readonly Dictionary<string, string> otherSnapshots = [];
    private readonly Dictionary<ulong, string> fieldObjects = [];
    private string lastSnapshot = string.Empty;
    private bool lureRunning;
    private int framesUntilPoll;
    private bool open;
    private bool noticeShown;

    public LureCapture()
    {
        foreach (var snapshotEvent in snapshotEvents)
        {
            Svc.AddonLifecycle.RegisterListener(snapshotEvent, AddonName, OnSnapshotEvent);
        }

        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, AddonName, OnReceiveEvent);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnFinalize);
        foreach (var otherEvent in otherAddonEvents)
        {
            Svc.AddonLifecycle.RegisterListener(otherEvent, OnOtherAddon);
        }

        Svc.Condition.ConditionChange += OnConditionChange;
        Svc.Chat.LogMessage += OnLogMessage;
        Svc.Framework.Update += OnUpdate;
        try
        {
            callbackHook = Svc.Hook.HookFromAddress<AtkUnitBase.Delegates.FireCallback>(AtkUnitBase.MemberFunctionPointers.FireCallback, OnFireCallback);
            callbackHook.Enable();
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Lure capture: the callback hook could not be placed; button callbacks are not written down.");
        }
    }

    public static string Folder => Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), CapturesFolder);

    public void Dispose()
    {
        callbackHook?.Dispose();
        Svc.Framework.Update -= OnUpdate;
        Svc.Chat.LogMessage -= OnLogMessage;
        Svc.Condition.ConditionChange -= OnConditionChange;
        foreach (var otherEvent in otherAddonEvents)
        {
            Svc.AddonLifecycle.UnregisterListener(otherEvent, OnOtherAddon);
        }

        foreach (var snapshotEvent in snapshotEvents)
        {
            Svc.AddonLifecycle.UnregisterListener(snapshotEvent, AddonName, OnSnapshotEvent);
        }

        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, AddonName, OnReceiveEvent);
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonName, OnFinalize);
    }

    private void OnSnapshotEvent(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (type == AddonEvent.PostSetup)
        {
            open = true;
            BeginLure();
            lastSnapshot = string.Empty;
            Write("open");
            WriteCoffers();
            if (!noticeShown)
            {
                noticeShown = true;
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatLureCapture, FilePath())}");
            }
        }

        Snapshot(addon, type.ToString());
    }

    private void OnReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (args is AddonReceiveEventArgs receive)
        {
            Write($"event: type {receive.AtkEventType}, param {receive.EventParam}");
        }
    }

    private void OnFinalize(AddonEvent type, AddonArgs args)
    {
        Snapshot((AtkUnitBase*)args.Addon.Address, "final");
        Write("close");
        open = false;
    }

    private bool OnFireCallback(AtkUnitBase* addon, uint valueCount, AtkValue* values, bool close)
    {
        try
        {
            if (addon is not null && (lureRunning || addon->NameString == AddonName))
            {
                var line = new StringBuilder("callback ").Append(addon->NameString).Append(": close ").Append(close).Append(", values [");
                for (var index = 0; index < valueCount; index++)
                {
                    line.Append(index > 0 ? ", " : string.Empty).Append(DescribeValue(values[index]));
                }

                Write(line.Append(']').ToString());
            }
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Lure capture: a callback could not be written down.");
        }

        return callbackHook!.Original(addon, valueCount, values, close);
    }

    // Catches what changes without a refresh, such as the second card turning over.
    private void OnUpdate(IFramework framework)
    {
        if (!(open || lureRunning) || --framesUntilPoll > 0)
        {
            return;
        }

        framesUntilPoll = PollIntervalFrames;
        if (open && GenericHelpers.TryGetAddonByName<AtkUnitBase>(AddonName, out var addon))
        {
            Snapshot(addon, "poll");
        }

        if (lureRunning)
        {
            ScanField();
        }
    }

    private void BeginLure()
    {
        if (lureRunning)
        {
            return;
        }

        lureRunning = true;
        otherSnapshots.Clear();
        fieldObjects.Clear();
        cardMemory.Clear();
        Write("lure running");
        ScanField();
    }

    private void EndLure(uint messageId)
    {
        if (!lureRunning)
        {
            return;
        }

        ScanField();
        lureRunning = false;
        Write($"lure over: {messageId}");
    }

    // The window the cards are guessed in is not known yet, so every window but the HUD's is written down while a
    // lure runs.
    private void OnOtherAddon(AddonEvent type, AddonArgs args)
    {
        var name = args.AddonName;
        if (!lureRunning || name == AddonName || name.StartsWith('_'))
        {
            return;
        }

        if (type == AddonEvent.PreFinalize)
        {
            Write($"addon {name}: close");
            otherSnapshots.Remove(name);
            return;
        }

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon is null)
        {
            return;
        }

        var text = Describe(addon);
        if (otherSnapshots.TryGetValue(name, out var before) && before == text)
        {
            return;
        }

        otherSnapshots[name] = text;
        Write($"addon {name} ({type}):\n{text.TrimEnd()}");
    }

    private void OnConditionChange(ConditionFlag flag, bool value)
    {
        if (lureRunning)
        {
            Write($"condition {flag} = {value}");
        }
    }

    // The cards may be objects of their own above the coffer, so whatever comes, changes or goes nearby is written.
    private void ScanField()
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return;
        }

        var still = new HashSet<ulong>();
        foreach (var candidate in Svc.Objects)
        {
            if (candidate.ObjectKind is ObjectKind.Pc or ObjectKind.Companion
                || candidate.Address == 0 || Vector3.Distance(player.Position, candidate.Position) > FieldMeters)
            {
                continue;
            }

            var id = candidate.GameObjectId;
            still.Add(id);
            var native = (NativeGameObject*)candidate.Address;
            var position = candidate.Position;
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{candidate.ObjectKind} base {candidate.BaseId} '{candidate.Name.TextValue}' ({id:X}) at ({position.X:F2}, {position.Y:F2}, {position.Z:F2}) rot {candidate.Rotation:F2}, targetable {candidate.IsTargetable}, render {native->RenderFlags}, event state {((NativeEventObject*)native)->EventState}, arg {((NativeEventObject*)native)->Arg}");
            if (fieldObjects.TryGetValue(id, out var before) && before == line)
            {
                continue;
            }

            Write($"{(before is null ? "object" : "object change")}: {line}");
            fieldObjects[id] = line;
        }

        WatchCards();
        foreach (var id in fieldObjects.Keys.Where(id => !still.Contains(id)).ToArray())
        {
            Write($"object gone: {fieldObjects[id]}");
            fieldObjects.Remove(id);
        }
    }

    // The two cards are nameless event objects either side of the coffer, and nothing the object table reports
    // changes when one turns over. Their memory is compared from one look to the next instead, so whatever the game
    // writes when a card shows its face turns up as changed bytes.
    private void WatchCards()
    {
        IGameObject? coffer = null;
        foreach (var candidate in Svc.Objects)
        {
            if (candidate.ObjectKind == ObjectKind.Treasure && fieldObjects.ContainsKey(candidate.GameObjectId))
            {
                coffer = candidate;
                break;
            }
        }

        if (coffer is null)
        {
            return;
        }

        var size = sizeof(NativeEventObject);
        foreach (var candidate in Svc.Objects)
        {
            if (candidate.ObjectKind != ObjectKind.EventObj || candidate.Name.TextValue.Length > 0 || candidate.Address == 0
                || Vector3.Distance(coffer.Position, candidate.Position) > CardMeters)
            {
                continue;
            }

            var id = candidate.GameObjectId;
            CompareCard(id, "object", (byte*)candidate.Address, size);
            var model = ((NativeGameObject*)candidate.Address)->DrawObject;
            if (model is not null)
            {
                CompareCard(id | ModelKey, "model", (byte*)model, sizeof(NativeDrawObject));
            }
        }
    }

    private void CompareCard(ulong key, string part, byte* memory, int size)
    {
        var now = new ReadOnlySpan<byte>(memory, size).ToArray();
        var id = key & ~ModelKey;
        if (!cardMemory.TryGetValue(key, out var before) || before.Length != size)
        {
            cardMemory[key] = now;
            Write($"card {id:X} {part}: {Convert.ToHexString(now)}");
            return;
        }

        var changes = new StringBuilder();
        var shown = 0;
        for (var offset = 0; offset < size && shown < MaxCardChanges; offset++)
        {
            if (now[offset] != before[offset])
            {
                changes.Append(string.Create(CultureInfo.InvariantCulture, $" {offset:X}:{before[offset]:X2}>{now[offset]:X2}"));
                shown++;
            }
        }

        if (shown > 0)
        {
            cardMemory[key] = now;
            Write($"card {id:X} {part} changed:{changes}");
        }
    }

    private void OnLogMessage(ILogMessage message)
    {
        var lureMessage = message.LogMessageId is >= FirstLureMessage and <= LastLureMessage;
        if (!lureMessage && !lureRunning)
        {
            return;
        }

        if (message.LogMessageId == DungeonLogMessages.LureSprung)
        {
            BeginLure();
        }

        var line = new StringBuilder("log ").Append(message.LogMessageId).Append(": params [");
        for (var index = 0; index < message.ParameterCount; index++)
        {
            line.Append(index > 0 ? ", " : string.Empty);
            line.Append(message.TryGetIntParameter(index, out var number) ? number.ToString(CultureInfo.InvariantCulture) : "?");
        }

        Write(line.Append("] \"").Append(Flatten(message.FormatLogMessageForDebugging().ExtractText())).Append('"').ToString());
        if (message.LogMessageId is DungeonLogMessages.LureTemptedAnother or DungeonLogMessages.LureRound)
        {
            WriteCoffers();
        }

        if (message.LogMessageId is DungeonLogMessages.LureDeclined or DungeonLogMessages.LureStopped
            or DungeonLogMessages.LureAllDisarmed or DungeonLogMessages.LureFailed)
        {
            EndLure(message.LogMessageId);
        }
    }

    private void Snapshot(AtkUnitBase* addon, string reason)
    {
        if (addon is null)
        {
            return;
        }

        var text = Describe(addon);
        if (text == lastSnapshot)
        {
            return;
        }

        lastSnapshot = text;
        Write($"snapshot ({reason}):\n{text.TrimEnd()}");
    }

    private static string Describe(AtkUnitBase* addon)
    {
        var body = new StringBuilder();
        body.Append("  visible ").Append(addon->IsVisible).Append(", values ").Append(addon->AtkValuesCount).AppendLine();
        for (var index = 0; index < addon->AtkValuesCount; index++)
        {
            var value = addon->AtkValues[index];
            if (value.Type != 0)
            {
                body.Append("  value[").Append(index).Append("] ").AppendLine(DescribeValue(value));
            }
        }

        DescribeNodes(&addon->UldManager, body, "  ", NodeDepth);
        return body.ToString();
    }

    private static void DescribeNodes(AtkUldManager* manager, StringBuilder body, string indent, int depth)
    {
        for (var index = 0; index < manager->NodeListCount; index++)
        {
            var node = manager->NodeList[index];
            if (node is null || !node->IsVisible())
            {
                continue;
            }

            switch (node->Type)
            {
                case NodeType.Text:
                    var textNode = node->GetAsAtkTextNode();
                    body.Append(indent).Append("node ").Append(node->NodeId).Append(" text \"").Append(Flatten(textNode->NodeText.ToString())).AppendLine("\"");
                    break;
                case NodeType.Counter:
                    var counterNode = (AtkCounterNode*)node;
                    body.Append(indent).Append("node ").Append(node->NodeId).Append(" counter \"").Append(counterNode->NodeText.ToString()).AppendLine("\"");
                    break;
                case NodeType.Image:
                    body.Append(indent).Append("node ").Append(node->NodeId).Append(" image ").AppendLine(DescribeImage((AtkImageNode*)node));
                    break;
                default:
                    var componentNode = node->GetAsAtkComponentNode();
                    if (componentNode is not null && componentNode->Component is not null)
                    {
                        body.Append(indent).Append("node ").Append(node->NodeId).Append(" component ").Append(componentNode->Component->GetComponentType()).AppendLine();
                        if (depth > 0)
                        {
                            DescribeNodes(&componentNode->Component->UldManager, body, indent + "  ", depth - 1);
                        }
                    }

                    break;
            }
        }
    }

    private static string DescribeImage(AtkImageNode* image)
    {
        var builder = new StringBuilder("part ").Append(image->PartId);
        if (image->PartsList is null || image->PartId >= image->PartsList->PartCount)
        {
            return builder.ToString();
        }

        var part = &image->PartsList->Parts[image->PartId];
        builder.Append(" uv (").Append(part->U).Append(", ").Append(part->V).Append(") size ").Append(part->Width).Append('x').Append(part->Height);
        if (part->UldAsset is null)
        {
            return builder.ToString();
        }

        var texture = &part->UldAsset->AtkTexture;
        if (texture->TextureType == TextureType.Resource && texture->Resource is not null)
        {
            var resource = texture->Resource;
            builder.Append(" icon ").Append(resource->IconId);
            if (resource->TexFileResourceHandle is not null)
            {
                builder.Append(" tex ").Append(resource->TexFileResourceHandle->ResourceHandle.FileName.ToString());
            }
        }

        return builder.ToString();
    }

    private void WriteCoffers()
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return;
        }

        foreach (var candidate in Svc.Objects)
        {
            if (candidate.ObjectKind != ObjectKind.Treasure || candidate.Address == 0
                || Vector3.Distance(player.Position, candidate.Position) > CofferMeters)
            {
                continue;
            }

            var treasure = (NativeTreasure*)candidate.Address;
            var line = new StringBuilder("coffer: base ").Append(candidate.BaseId).Append(" (").Append(candidate.GameObjectId.ToString("X", CultureInfo.InvariantCulture))
                .Append("), state ").Append(treasure->State).Append(", flags ").Append(treasure->Flags).Append(", items ").Append(treasure->ItemCount).Append(", ids [");
            var first = true;
            foreach (var itemId in treasure->LootableItemIds)
            {
                if (itemId == 0)
                {
                    continue;
                }

                var name = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Name.ExtractText() ?? "?";
                line.Append(first ? string.Empty : ", ").Append(itemId).Append(" '").Append(name).Append('\'');
                first = false;
            }

            Write(line.Append(']').ToString());
        }
    }

    private static string DescribeValue(AtkValue value)
        => value.Type switch
        {
            AtkValueType.Int => $"int {value.Int}",
            AtkValueType.UInt => $"uint {value.UInt}",
            AtkValueType.Bool => $"bool {value.Byte != 0}",
            AtkValueType.Float => $"float {value.Float.ToString(CultureInfo.InvariantCulture)}",
            AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString
                => $"string \"{(value.String.HasValue ? Flatten(value.String.ToString()) : string.Empty)}\"",
            _ => $"{value.Type}",
        };

    private static string FilePath()
        => Path.Combine(Folder, $"{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.txt");

    private static void Write(string text)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(FilePath(), $"[{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}] {text}\n", Encoding.UTF8);
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Lure capture: the file could not be written.");
        }

        RunLog.Info($"Lure: {text.Split('\n')[0]}");
    }

    private static string Flatten(string text) => text.Replace('\n', ' ').Replace('\r', ' ');
}
