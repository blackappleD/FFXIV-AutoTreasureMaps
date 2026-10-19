using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace AutoTreasureMaps.Core.Game.Watchers;

// The combat plugin's tank module only mitigates a tankbuster its boss module has marked, and treasure dungeon bosses
// have none, so its short mitigation waited for the HP to drop under 75% and the hit landed bare. While a run is on and
// the character tanks, an enemy's cast aimed at the character is mitigated as soon as it starts: the job's short
// cooldown first (Bloodwhetting, Holy Sheltron, The Blackest Night, Heart of Corundum), a bigger one when that is down.
internal sealed unsafe class TankbusterWatcher : IDisposable
{
    private const byte TankRole = 1;
    private const byte SingleTargetCastType = 1;
    private const byte CircleCastType = 2;
    // Casts shorter than this are a mob's filler, not worth a cooldown.
    private const float MinCastSeconds = 1.5f;
    // The shortest of the short mitigations lasts 7 s, so a longer cast waits until it would still cover the hit.
    private const float MitigateWithinSeconds = 6f;
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

    private static readonly uint[] WarriorMits = [RawIntuition, Rampart, Vengeance];
    private static readonly uint[] PaladinMits = [Sheltron, Rampart, Sentinel, Bulwark];
    private static readonly uint[] DarkKnightMits = [TheBlackestNight, Oblation, Rampart, ShadowWall, DarkMind];
    private static readonly uint[] GunbreakerMits = [HeartOfStone, Camouflage, Rampart, Nebula];

    // Casts already answered, keyed by caster and action, kept only while the caster is still casting them.
    private HashSet<(uint Caster, uint Action)> handled = [];
    private HashSet<(uint Caster, uint Action)> stillCasting = [];

    private enum MitResult : byte { Used, Refused, NoneReady }

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
        var controller = Plugin.Instance.Controller;
        if (!controller.Running || controller.Paused || !Svc.Condition[ConditionFlag.InCombat]
            || Svc.Objects.LocalPlayer is not { IsDead: false } player || player.ClassJob.ValueNullable?.Role != TankRole
            || MitsFor(player.ClassJob.RowId) is not { } mits)
        {
            handled.Clear();
            return;
        }

        stillCasting.Clear();
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

            switch (Mitigate(mits, player.GameObjectId, out var used))
            {
                case MitResult.Used:
                    RunLog.Info($"Tankbuster: {caster.Name} casts {ActionName(caster.CastActionId)} on the character ({caster.TotalCastTime:F1}s); used {ActionName(used)}.");
                    stillCasting.Add(key);
                    break;
                case MitResult.NoneReady:
                    RunLog.Info($"Tankbuster: {caster.Name} casts {ActionName(caster.CastActionId)} on the character, but every mitigation is on cooldown.");
                    stillCasting.Add(key);
                    break;
            }
        }

        (handled, stillCasting) = (stillCasting, handled);
    }

    private static uint[]? MitsFor(uint classJobId) => classJobId switch
    {
        1 or 19 => PaladinMits,
        3 or 21 => WarriorMits,
        32 => DarkKnightMits,
        37 => GunbreakerMits,
        _ => null,
    };

    // A single-target cast, or a circle centred on the character, both land on the tank whatever it does.
    private static bool AimedAtOne(uint actionId)
        => Svc.Data.GetExcelSheet<LuminaAction>().TryGetRow(actionId, out var action)
        && action.CastType is SingleTargetCastType or CircleCastType;

    // Takes the first mitigation the game would accept now; the list puts the short ones first. A press the game refuses
    // (an animation lock) is pressed again next frame.
    private static MitResult Mitigate(uint[] mits, ulong selfId, out uint used)
    {
        used = 0;
        var actions = ActionManager.Instance();
        if (actions is null)
        {
            return MitResult.Refused;
        }

        foreach (var mit in mits)
        {
            var actionId = actions->GetAdjustedActionId(mit);
            if (actions->GetActionStatus(ActionType.Action, actionId, selfId) != 0)
            {
                continue;
            }

            used = actionId;
            return actions->UseAction(ActionType.Action, actionId, selfId) ? MitResult.Used : MitResult.Refused;
        }

        return MitResult.NoneReady;
    }

    private static string ActionName(uint actionId)
        => Svc.Data.GetExcelSheet<LuminaAction>().TryGetRow(actionId, out var action) ? action.Name.ExtractText() : $"action {actionId}";
}
