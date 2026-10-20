using AutoTreasureMaps.Core.Ipc;
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

// With the setting on, while a run is on and the character tanks, this watcher owns the personal mitigations and the
// combat plugin's tank module is told to use none. That module spent the bigger cooldowns whenever the HP fell under
// 60%, so a pack of mobs took Rampart and Damnation away from the boss after it, and it never saw a tankbuster the
// treasure hunt modules do not mark.
//
// A cast aimed at the character gets the job's short mitigation (Bloodwhetting, Holy Sheltron, The Blackest Night, Heart
// of Corundum) once the cast is close enough to its end for the buff to cover it. Only a boss's tankbuster, one of the
// casts listed here, falls back to a bigger cooldown when the short one is down; nothing else ever spends one. One
// mitigation covers a window, so a volley from several casters spends one cooldown, and nothing is used while one of the
// job's mitigations is already up. Some bosses cast their tankbuster through an invisible helper, which has no nameplate
// and so never reads as hostile; a helper's cast counts only when it is listed.
//
// What the tank module did by the HP is done here without the bigger cooldowns: the short mitigation under 80%, a
// warrior's Thrill of Battle and Equilibrium under 50%, a gunbreaker's Aurora under 80%. Its invulnerability stays with
// the combat plugin.
internal sealed unsafe class TankbusterWatcher : IDisposable
{
    private const byte SingleTargetCastType = 1;
    private const byte CircleCastType = 2;
    // Casts shorter than this are a mob's filler, not worth a cooldown.
    private const float MinCastSeconds = 1.5f;
    // The shortest of the short mitigations lasts 7 s, so a longer cast waits until it would still cover the hit.
    private const float MitigateWithinSeconds = 6f;
    // A mitigation used covers every cast that lands within this window.
    private const long MitigationWindowMs = 7_000;
    private const uint ActionKind = 1;
    // The BNpcBase row of the invisible helper a boss casts some of its attacks through.
    private const uint HelperBaseId = 9020;
    private const float ShortMitigationHpRatio = 0.8f;
    private const float WarriorHealHpRatio = 0.5f;
    private const float AuroraHpRatio = 0.8f;
    // The tank module's setting is a transient one that a rewritten preset loses, so it is set again this often.
    private const long BossModReapplyMs = 5_000;

    private const uint Rampart = 7531;
    private const uint RawIntuition = 3551; // Bloodwhetting from level 82
    private const uint Vengeance = 44; // Damnation from level 92
    private const uint ThrillOfBattle = 40;
    private const uint Equilibrium = 3552;
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
    private const uint Aurora = 16151;
    // Aurora is cast on a target, so the Action sheet names no buff of its own for it.
    private const uint AuroraStatus = 1835;

    // The short mitigation comes first; the rest, tried in this order, are only used against a boss's tankbuster.
    private static readonly uint[] warriorMitigations = [RawIntuition, Rampart, Vengeance];
    private static readonly uint[] paladinMitigations = [Sheltron, Rampart, Sentinel, Bulwark];
    private static readonly uint[] darkKnightMitigations = [TheBlackestNight, Oblation, Rampart, ShadowWall, DarkMind];
    private static readonly uint[] gunbreakerMitigations = [HeartOfStone, Camouflage, Rampart, Nebula];

    // Every tankbuster the combat plugin's treasure hunt modules mark, by the boss that casts it.
    private static readonly uint[] bossTankbusters =
    [
        38261, // Blade, Bull Apollyon (Cenote Ja Ja Gural)
        38274, // Lap, Golden Molter
        43607, // Fluid Swing, Vault Fluid Soul (The Vault Oneiron)
        43615, // Mash, Vault Gargantuan
        43623, // Changeless Winds, Vault Wind Spirit
        43630, // Phantom Light, Vault Bat
        43638, // Wheel, Vault Roadripper
        43650, // Thundercrack, Gwyddneu, through its helper
        43668, // Uplift, Fastitocalon, through its helper
        43682, // Hundred-stone Smash, Old Bitter Eye
        43698, // Tormenting Twinkle, Great Gimme Cat, through its helper
        43716, // Ram, Paeonia
        43725, // Skewering Lance, Gilded Sentry
        43748, // Elimination, Mobile Fortress
        43768, // Anti-hero Artillery, Hexapod
        43784, // Hypercondensed Thunder, Spirit of Thunder, through its helper
    ];

    // Casts already answered, keyed by caster and action, kept only while the caster is still casting them.
    private HashSet<(uint Caster, uint Action)> handled = [];
    private HashSet<(uint Caster, uint Action)> stillCasting = [];
    private long windowEndsAtMs;
    private bool bossModMitigationHeld;
    private long bossModNextApplyMs;

    private enum MitigationResult : byte { Used, Refused, NotReady, AlreadyUp }

    public TankbusterWatcher()
    {
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
        HoldBossModMitigation(false);
    }

    private void OnUpdate(IFramework _)
    {
        var plugin = Plugin.Instance;
        var controller = plugin.Controller;
        var player = Svc.Objects.LocalPlayer;
        var mitigations = player is null ? null : MitigationsFor(player.ClassJob.RowId);
        var owned = plugin.Configuration.TankbusterMitigation && controller.Running && !controller.Paused && mitigations is not null;
        HoldBossModMitigation(owned);
        if (!owned || mitigations is null || player is not { IsDead: false } || !Svc.Condition[ConditionFlag.InCombat])
        {
            handled.Clear();
            return;
        }

        stillCasting.Clear();
        var inWindow = Environment.TickCount64 < windowEndsAtMs;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not IBattleNpc { IsCasting: true, IsDead: false } caster
                || caster.CastTargetObjectId != player.GameObjectId || caster.CastActionType != ActionKind)
            {
                continue;
            }

            var tankbuster = Array.IndexOf(bossTankbusters, caster.CastActionId) >= 0;
            if (caster.BaseId == HelperBaseId
                ? !tankbuster
                : caster.BattleNpcKind != BattleNpcSubKind.Combatant || !caster.IsHostile())
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
                || (!tankbuster && !AimedAtOne(caster.CastActionId)))
            {
                continue;
            }

            // Not marked handled: a cast still going when the window ends is looked at again then, and the already-up
            // check keeps it from doubling a mitigation that still covers it.
            if (inWindow)
            {
                continue;
            }

            var cast = $"{caster.Name} casts {ActionName(caster.CastActionId)} on the character ({caster.TotalCastTime:F1}s{(tankbuster ? ", a boss's tankbuster" : string.Empty)})";
            switch (Mitigate(mitigations, tankbuster, player, out var used))
            {
                case MitigationResult.Used:
                    RunLog.Info($"Tankbuster: {cast}; used {ActionName(used)}.");
                    windowEndsAtMs = Environment.TickCount64 + MitigationWindowMs;
                    inWindow = true;
                    stillCasting.Add(key);
                    break;
                case MitigationResult.AlreadyUp:
                    RunLog.Info($"Tankbuster: {cast}; {ActionName(used)} is already up.");
                    stillCasting.Add(key);
                    break;
                case MitigationResult.NotReady:
                    RunLog.Info(tankbuster
                        ? $"Tankbuster: {cast}, but no mitigation is ready."
                        : $"Tankbuster: {cast}, but {ActionName(used)} is not ready.");
                    stillCasting.Add(key);
                    break;
            }
        }

        (handled, stillCasting) = (stillCasting, handled);
        MitigateByHp(mitigations, player);
    }

    // Tells the combat plugin's tank module to use no personal mitigation while this watcher owns them, and hands them
    // back once it does not.
    private void HoldBossModMitigation(bool hold)
    {
        if (!hold)
        {
            if (bossModMitigationHeld)
            {
                bossModMitigationHeld = false;
                bossModNextApplyMs = 0;
                BossModIPC.Instance.ClearTransientStrategy(TreasureCombatPreset.Name, TreasureCombatPreset.TankModule, TreasureCombatPreset.PersonalMitigationTrack);
                RunLog.Info("Tankbuster: the combat plugin's tank module uses its personal mitigations again.");
            }

            return;
        }

        var now = Environment.TickCount64;
        if (now < bossModNextApplyMs)
        {
            return;
        }

        bossModNextApplyMs = now + BossModReapplyMs;
        var done = BossModIPC.Instance.AddTransientStrategy(TreasureCombatPreset.Name, TreasureCombatPreset.TankModule,
            TreasureCombatPreset.PersonalMitigationTrack, TreasureCombatPreset.PersonalMitigationOff);
        if (done && !bossModMitigationHeld)
        {
            RunLog.Info("Tankbuster: the combat plugin's tank module leaves the personal mitigations to this plugin while the run is on.");
        }

        bossModMitigationHeld = done;
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

    // Uses the job's short mitigation unless any of its mitigations is already on the character; against a boss's
    // tankbuster, the first bigger one that is ready when the short one is not. A press the game refuses (an animation
    // lock) is pressed again next frame.
    private static MitigationResult Mitigate(uint[] mitigations, bool tankbuster, IPlayerCharacter player, out uint used)
    {
        used = 0;
        var actions = ActionManager.Instance();
        if (actions is null)
        {
            return MitigationResult.Refused;
        }

        if (MitigationUp(actions, mitigations, player) is var up and not 0)
        {
            used = up;
            return MitigationResult.AlreadyUp;
        }

        var tried = tankbuster ? mitigations.Length : 1;
        for (var mitigationIndex = 0; mitigationIndex < tried; mitigationIndex++)
        {
            var actionId = actions->GetAdjustedActionId(mitigations[mitigationIndex]);
            if (Ready(actions, actionId, player))
            {
                used = actionId;
                return actions->UseAction(ActionType.Action, actionId, player.GameObjectId) ? MitigationResult.Used : MitigationResult.Refused;
            }
        }

        used = actions->GetAdjustedActionId(mitigations[0]);
        return MitigationResult.NotReady;
    }

    // The short mitigation once the HP falls under 80% with none of the job's mitigations up, and the warrior's and the
    // gunbreaker's heals; never a bigger cooldown.
    private static void MitigateByHp(uint[] mitigations, IPlayerCharacter player)
    {
        var actions = ActionManager.Instance();
        if (actions is null || player.MaxHp == 0)
        {
            return;
        }

        var ratio = (float)player.CurrentHp / player.MaxHp;
        if (ratio < ShortMitigationHpRatio && MitigationUp(actions, mitigations, player) == 0)
        {
            UseByHp(actions, actions->GetAdjustedActionId(mitigations[0]), player, ratio);
        }

        if (mitigations == warriorMitigations && ratio < WarriorHealHpRatio)
        {
            UseByHp(actions, ThrillOfBattle, player, ratio);
            UseByHp(actions, Equilibrium, player, ratio);
        }
        else if (mitigations == gunbreakerMitigations && ratio < AuroraHpRatio && !StatusUp(player, AuroraStatus))
        {
            UseByHp(actions, Aurora, player, ratio);
        }
    }

    private static void UseByHp(ActionManager* actions, uint actionId, IPlayerCharacter player, float ratio)
    {
        if (Ready(actions, actionId, player) && actions->UseAction(ActionType.Action, actionId, player.GameObjectId))
        {
            RunLog.Debug($"Tankbuster: HP at {ratio:P0}; used {ActionName(actionId)}.");
        }
    }

    private static bool Ready(ActionManager* actions, uint actionId, IPlayerCharacter player)
        => actions->GetActionStatus(ActionType.Action, actionId, player.GameObjectId) == 0;

    // The first of the job's mitigations whose buff is on the character, or 0.
    private static uint MitigationUp(ActionManager* actions, uint[] mitigations, IPlayerCharacter player)
    {
        for (var mitigationIndex = 0; mitigationIndex < mitigations.Length; mitigationIndex++)
        {
            var actionId = actions->GetAdjustedActionId(mitigations[mitigationIndex]);
            if (BuffUp(player, actionId))
            {
                return actionId;
            }
        }

        return 0;
    }

    // The buff an action puts on its user is read from the Action sheet, so no status ids are kept here.
    private static bool BuffUp(IPlayerCharacter player, uint actionId)
    {
        if (!Svc.Data.GetExcelSheet<LuminaAction>().TryGetRow(actionId, out var action) || action.StatusGainSelf.RowId == 0)
        {
            return false;
        }

        return StatusUp(player, action.StatusGainSelf.RowId);
    }

    private static bool StatusUp(IPlayerCharacter player, uint statusId)
    {
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
