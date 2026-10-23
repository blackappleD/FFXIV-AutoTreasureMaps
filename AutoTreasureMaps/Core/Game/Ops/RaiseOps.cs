using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace AutoTreasureMaps.Core.Game.Ops;

// The jobs that can raise a fallen party member, each with its own spell, and Swiftcast, which every one of them has as
// a role action. Red Mage's Dualcast makes its next spell instant as Swiftcast does.
internal static unsafe class RaiseOps
{
    public const uint WhiteMage = 24;
    public const uint Scholar = 28;
    public const uint Astrologian = 33;
    public const uint Sage = 40;
    public const uint Summoner = 27;
    public const uint RedMage = 35;

    public const uint SwiftcastActionId = 7561;
    private const uint SwiftcastStatusId = 167;
    private const uint DualcastStatusId = 1249;

    private const uint RaiseActionId = 125;
    private const uint ResurrectionActionId = 173;
    private const uint AscendActionId = 3603;
    private const uint EgeiroActionId = 24287;
    private const uint VerraiseActionId = 7523;

    private const int ReviveCommand = (int)clib.Enums.CommandFlag.Revive;
    private const int ReviveAccept = (int)clib.Enums.AgentReviveOp.AcceptRevive;

    // A raise cast on a fallen member shows on it as one of these until it gets up, so it is not raised twice.
    private static readonly uint[] raisePendingStatusIds = [148, 1140, 2648];

    // Healers first, then the casters, in the order the settings list them.
    public static readonly uint[] ClassJobIds = [WhiteMage, Scholar, Astrologian, Sage, Summoner, RedMage];

    public static bool CanRaise(uint classJobId) => RaiseActionFor(classJobId) != 0;

    public static uint RaiseActionFor(uint classJobId) => classJobId switch
    {
        WhiteMage => RaiseActionId,
        Scholar or Summoner => ResurrectionActionId,
        Astrologian => AscendActionId,
        Sage => EgeiroActionId,
        RedMage => VerraiseActionId,
        _ => 0,
    };

    public static bool RaisePending(IBattleChara character) => HasAnyStatus(character, raisePendingStatusIds);

    // Whether the next spell goes off at once: Swiftcast is up, or Dualcast on a Red Mage.
    public static bool InstantCastUp(IBattleChara player)
        => HasStatus(player, SwiftcastStatusId) || (player.ClassJob.RowId == RedMage && HasStatus(player, DualcastStatusId));

    // The LogMessage row the game would print if the action were used on the target now, 0 when it would go through.
    public static uint ActionStatus(uint actionId, ulong targetId)
    {
        var actions = ActionManager.Instance();
        return actions is null ? uint.MaxValue : actions->GetActionStatus(ActionType.Action, actionId, targetId);
    }

    public static bool RecastActive(uint actionId)
    {
        var actions = ActionManager.Instance();
        return actions is not null && actions->IsRecastTimerActive(ActionType.Action, actionId);
    }

    public static bool Use(uint actionId, ulong targetId)
    {
        var actions = ActionManager.Instance();
        return actions is not null && actions->UseAction(ActionType.Action, actionId, targetId);
    }

    // Takes the raise on offer to the knocked-out character; it does nothing while none is.
    public static bool AcceptRaise() => GameMain.ExecuteCommand(ReviveCommand, ReviveAccept);

    public static string ActionName(uint actionId)
        => Svc.Data.GetExcelSheet<LuminaAction>().TryGetRow(actionId, out var action) ? action.Name.ExtractText() : $"action {actionId}";

    // The job's name in the client's language, capitalised, since the English sheet keeps it in lower case.
    public static string JobName(uint classJobId)
    {
        var name = Svc.Data.GetExcelSheet<ClassJob>().TryGetRow(classJobId, out var classJob) ? classJob.Name.ExtractText() : string.Empty;
        return name.Length == 0 ? GearsetSwitcher.ClassJobLabel(classJobId) : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static bool HasAnyStatus(IBattleChara character, uint[] statusIds)
    {
        var statuses = character.StatusList;
        for (var statusIndex = 0; statusIndex < statuses.Length; statusIndex++)
        {
            if (statuses[statusIndex] is { } status && Array.IndexOf(statusIds, status.StatusId) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStatus(IBattleChara character, uint statusId)
    {
        var statuses = character.StatusList;
        for (var statusIndex = 0; statusIndex < statuses.Length; statusIndex++)
        {
            if (statuses[statusIndex] is { } status && status.StatusId == statusId)
            {
                return true;
            }
        }

        return false;
    }
}
