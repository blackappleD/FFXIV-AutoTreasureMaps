using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace AutoTreasureMaps.Core.Game.Watchers;

// The combat plugin's tank module only mitigates a tankbuster its boss module has marked, and treasure hunt enemies have
// none, so its short mitigation waited for the HP to drop under 75% and the hit landed bare. With the setting on, while
// a run is on and the character tanks, a cast aimed at the character gets the job's short mitigation (Bloodwhetting,
// Holy Sheltron, The Blackest Night, Heart of Corundum) once the cast is close enough to its end for the buff to cover
// it. Only a boss's tankbuster, told apart by its long cast, falls back to a bigger cooldown when the short one is down,
// so a mob's cast never spends one. One mitigation covers a window, so a volley from several casters spends one
// cooldown, and nothing is used while one of the job's mitigations is already up.
internal sealed unsafe class TankbusterWatcher : IDisposable
{
    private const byte TankRole = 1;
    private const byte SingleTargetCastType = 1;
    private const byte CircleCastType = 2;
    // Casts shorter than this are a mob's filler, not worth a cooldown.
    private const float MinCastSeconds = 1.5f;
    // Treasure hunt bosses cast their tankbusters for 4.7 s and mobs their spells for 1.5 to 2.5 s, so a cast this long
    // is a tankbuster and may take a bigger cooldown.
    private const float HeavyCastSeconds = 4f;
    // The shortest of the short mitigations lasts 7 s, so a longer cast waits until it would still cover the hit.
    private const float MitigateWithinSeconds = 6f;
    // A mitigation used covers every cast that lands within this window.
    private const long MitigationWindowMs = 7_000;
    private const uint ActionKind = 1;

    private const uint Rampart = 7531;
    private const uint RawIntuition = 3551; // Bloodwhetting from level 82
    private const uint Vengeance = 44; // Damnation from level 92
    private const uint Sheltron = 3542; // Holy Sheltron from level 82
    private const uint Bulwark = 22;
    private const uint Sentinel = 17; // Guardian from level 92
    private const uint TheBlackestNight = 7393;
    private const uint Oblation = 25754;
    private const uint ShadowWall = 3636; // Shadowed Vigil from level 92
    private const uint DarkMind = 3634;
    private const uint HeartOfStone = 16161; // Heart of Corundum from level 82
    private const uint Camouflage = 16140;
    private const uint Nebula = 16148; // Great Nebula from level 92

    // The short mitigation comes first; the rest, tried in this order, are only used against a tankbuster.
    private static readonly uint[] warriorMitigations = [RawIntuition, Rampart, Vengeance];
    private static readonly uint[] paladinMitigations = [Sheltron, Rampart, Sentinel, Bulwark];
    private static readonly uint[] darkKnightMitigations = [TheBlackestNight, Oblation, Rampart, ShadowWall, DarkMind];
    private static readonly uint[] gunbreakerMitigations = [HeartOfStone, Camouflage, Rampart, Nebula];

    // Casts already answered, keyed by caster and action, kept only while the caster is still casting them.
    private HashSet<(uint Caster, uint Action)> handled = [];
    private HashSet<(uint Caster, uint Action)> stillCasting = [];
    private long windowEndsAtMs;

    private enum MitigationResult : byte { Used, Refused, NotReady, AlreadyUp }

    public TankbusterWatcher()
    {
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
    }

    private void OnUpdate(IFramework _)
    {
        var plugin = Plugin.Instance;
        if (!plugin.Configuration.TankbusterMitigation)
        {
            return;
        }

        var controller = plugin.Controller;
        if (!controller.Running || controller.Paused || !Svc.Condition[ConditionFlag.InCombat]
            || Svc.Objects.LocalPlayer is not { IsDead: false } player || player.ClassJob.ValueNullable?.Role != TankRole
            || MitigationsFor(player.ClassJob.RowId) is not { } mitigations)
        {
            handled.Clear();
            return;
        }

        stillCasting.Clear();
        var inWindow = Environment.TickCount64 < windowEndsAtMs;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant, IsCasting: true, IsDead: false } caster
                || caster.CastTargetObjectId != player.GameObjectId || caster.CastActionType != ActionKind || !caster.IsHostile())
            {
                continue;
            }

            var key = (caster.EntityId, caster.CastActionId);
            if (handled.Contains(key))
            {
                stillCasting.Add(key);
                continue;
            }

            if (caster.TotalCastTime < MinCastSeconds || caster.TotalCastTime - caster.CurrentCastTime > MitigateWithinSeconds
                || !AimedAtOne(caster.CastActionId))
            {
                continue;
            }

            if (inWindow)
            {
                stillCasting.Add(key);
                continue;
            }

            var heavy = caster.TotalCastTime >= HeavyCastSeconds;
            switch (Mitigate(mitigations, heavy, player, out var used))
            {
                case MitigationResult.Used:
                    RunLog.Info($"Tankbuster: {caster.Name} casts {ActionName(caster.CastActionId)} on the character ({caster.TotalCastTime:F1}s); used {ActionName(used)}.");
                    windowEndsAtMs = Environment.TickCount64 + MitigationWindowMs;
                    inWindow = true;
                    stillCasting.Add(key);
                    break;
                case MitigationResult.AlreadyUp:
                    RunLog.Info($"Tankbuster: {caster.Name} casts {ActionName(caster.CastActionId)} on the character; {ActionName(used)} is already up.");
                    stillCasting.Add(key);
                    break;
                case MitigationResult.NotReady:
                    RunLog.Info(heavy
                        ? $"Tankbuster: {caster.Name} casts {ActionName(caster.CastActionId)} on the character ({caster.TotalCastTime:F1}s), but no mitigation is ready."
                        : $"Tankbuster: {caster.Name} casts {ActionName(caster.CastActionId)} on the character ({caster.TotalCastTime:F1}s), but {ActionName(used)} is not ready.");
                    stillCasting.Add(key);
                    break;
            }
        }

        (handled, stillCasting) = (stillCasting, handled);
    }

    private static uint[]? MitigationsFor(uint classJobId) => classJobId switch
    {
        1 or 19 => paladinMitigations,
        3 or 21 => warriorMitigations,
        32 => darkKnightMitigations,
        37 => gunbreakerMitigations,
        _ => null,
    };

    // A single-target cast, or a circle centred on the character, both land on the tank whatever it does.
    private static bool AimedAtOne(uint actionId)
        => Svc.Data.GetExcelSheet<LuminaAction>().TryGetRow(actionId, out var action)
        && action.CastType is SingleTargetCastType or CircleCastType;

    // Uses the job's short mitigation unless any of its mitigations is already on the character; against a tankbuster,
    // the first bigger one that is ready when the short one is not. A press the game refuses (an animation lock) is
    // pressed again next frame.
    private static MitigationResult Mitigate(uint[] mitigations, bool heavy, IPlayerCharacter player, out uint used)
    {
        used = 0;
        var actions = ActionManager.Instance();
        if (actions is null)
        {
            return MitigationResult.Refused;
        }

        for (var mitigationIndex = 0; mitigationIndex < mitigations.Length; mitigationIndex++)
        {
            var actionId = actions->GetAdjustedActionId(mitigations[mitigationIndex]);
            if (BuffUp(player, actionId))
            {
                used = actionId;
                return MitigationResult.AlreadyUp;
            }
        }

        var tried = heavy ? mitigations.Length : 1;
        for (var mitigationIndex = 0; mitigationIndex < tried; mitigationIndex++)
        {
            var actionId = actions->GetAdjustedActionId(mitigations[mitigationIndex]);
            if (actions->GetActionStatus(ActionType.Action, actionId, player.GameObjectId) == 0)
            {
                used = actionId;
                return actions->UseAction(ActionType.Action, actionId, player.GameObjectId) ? MitigationResult.Used : MitigationResult.Refused;
            }
        }

        used = actions->GetAdjustedActionId(mitigations[0]);
        return MitigationResult.NotReady;
    }

    // The buff an action puts on its user is read from the Action sheet, so no status ids are kept here.
    private static bool BuffUp(IPlayerCharacter player, uint actionId)
    {
        if (!Svc.Data.GetExcelSheet<LuminaAction>().TryGetRow(actionId, out var action) || action.StatusGainSelf.RowId == 0)
        {
            return false;
        }

        var statusId = action.StatusGainSelf.RowId;
        var statuses = player.StatusList;
        for (var statusIndex = 0; statusIndex < statuses.Length; statusIndex++)
        {
            if (statuses[statusIndex] is { } status && status.StatusId == statusId)
            {
                return true;
            }
        }

        return false;
    }

    private static string ActionName(uint actionId)
        => Svc.Data.GetExcelSheet<LuminaAction>().TryGetRow(actionId, out var action) ? action.Name.ExtractText() : $"action {actionId}";
}
