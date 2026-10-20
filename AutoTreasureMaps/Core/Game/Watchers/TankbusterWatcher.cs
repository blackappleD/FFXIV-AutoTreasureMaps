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
// of Corundum) once the cast is close enough to its end for the buff to cover it, unless one of the job's mitigations is
// already up. A boss's tankbuster, one of the casts listed here, gets the short mitigation and one bigger cooldown, each
// as soon as its cooldown allows; nothing else ever spends a bigger one. One answer covers a window, so a volley from
// several casters spends it once. Some bosses cast their tankbuster through an invisible helper, which has no nameplate
// and so never reads as hostile; a helper's cast counts only when it is listed.
//
// What the tank module did by the HP is done here without the bigger cooldowns: the short mitigation under 80%, a
// warrior's Thrill of Battle and Equilibrium under 50%, a gunbreaker's Aurora under 80%. Its invulnerability stays with
// the combat plugin.
//
// One press per frame, and only while the character is free to act: a press made during an animation lock is queued by
// the game, and that queue was seen to hold Damnation for 4 s, past the hit. A cast being answered comes before the HP.
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

    // The short mitigation comes first; the rest, strongest first, are only used against a boss's tankbuster.
    private static readonly uint[] warriorMitigations = [RawIntuition, Vengeance, Rampart];
    private static readonly uint[] paladinMitigations = [Sheltron, Sentinel, Rampart, Bulwark];
    private static readonly uint[] darkKnightMitigations = [TheBlackestNight, ShadowWall, Rampart, Oblation, DarkMind];
    private static readonly uint[] gunbreakerMitigations = [HeartOfStone, Nebula, Rampart, Camouflage];

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

    // The cast being answered and what has been pressed for it.
    private (uint Caster, uint Action) answering;
    private readonly List<uint> pressed = [];

    private enum Press : byte { Now, Done }

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
        var busy = false;
        var answeringSeen = false;
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
            answeringSeen |= key == answering;
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
            // check keeps it from doubling a mitigation that still covers it. A boss's tankbuster is never left to a
            // window, so a mob's cast just before it does not keep its bigger cooldown away.
            if (inWindow && !tankbuster)
            {
                continue;
            }

            // One cast is answered at a time; another waits for the next frame.
            if (busy)
            {
                continue;
            }

            if (answering != key)
            {
                answering = key;
                pressed.Clear();
            }

            busy = true;
            if (!FreeToAct())
            {
                continue;
            }

            var cast = $"{caster.Name} casts {ActionName(caster.CastActionId)} on the character ({caster.TotalCastTime:F1}s{(tankbuster ? ", a boss's tankbuster" : string.Empty)})";
            if (NextPress(mitigations, tankbuster, player, pressed, out var actionId) == Press.Now)
            {
                if (ActionManager.Instance()->UseAction(ActionType.Action, actionId, player.GameObjectId))
                {
                    pressed.Add(actionId);
                    RunLog.Info($"Tankbuster: {cast}; used {ActionName(actionId)}.");
                }

                continue;
            }

            stillCasting.Add(key);
            if (pressed.Count > 0)
            {
                windowEndsAtMs = Environment.TickCount64 + MitigationWindowMs;
                inWindow = true;
                if (tankbuster && pressed.Count == 1 && pressed[0] == ActionManager.Instance()->GetAdjustedActionId(mitigations[0]))
                {
                    RunLog.Info($"Tankbuster: {cast}; no bigger cooldown is ready.");
                }
            }
            else
            {
                RunLog.Info(actionId != 0
                    ? $"Tankbuster: {cast}; {ActionName(actionId)} is already up."
                    : $"Tankbuster: {cast}, but no mitigation is ready.");
            }
        }

        (handled, stillCasting) = (stillCasting, handled);

        // A boss casts the same tankbuster again and again, so what was pressed for one cast is forgotten once it ends;
        // kept, it read as already pressed and the next casts got nothing.
        if (!answeringSeen)
        {
            answering = default;
            pressed.Clear();
        }
        if (!busy)
        {
            MitigateByHp(mitigations, player);
        }
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

    // What to press next for a cast: the short mitigation unless a mitigation covers the hit already (against a boss's
    // tankbuster only the short one's own buff counts), then, against a boss's tankbuster, the first bigger one that is
    // ready unless one is up. Done once nothing is left to press; actionId then names a mitigation that is up, or 0.
    private static Press NextPress(uint[] mitigations, bool tankbuster, IPlayerCharacter player, List<uint> pressed, out uint actionId)
    {
        actionId = 0;
        var actions = ActionManager.Instance();
        if (actions is null)
        {
            return Press.Done;
        }

        var shortId = actions->GetAdjustedActionId(mitigations[0]);
        var up = MitigationUp(actions, mitigations, player);
        var shortCovered = tankbuster ? BuffUp(player, shortId) : up != 0;
        if (!shortCovered && !pressed.Contains(shortId) && Ready(actions, shortId, player))
        {
            actionId = shortId;
            return Press.Now;
        }

        var bigUp = up != 0 && up != shortId;
        for (var mitigationIndex = 1; tankbuster && !bigUp && mitigationIndex < mitigations.Length; mitigationIndex++)
        {
            var bigId = actions->GetAdjustedActionId(mitigations[mitigationIndex]);
            if (pressed.Contains(bigId))
            {
                break;
            }

            if (Ready(actions, bigId, player))
            {
                actionId = bigId;
                return Press.Now;
            }
        }

        actionId = up;
        return Press.Done;
    }

    // A press now runs at once instead of waiting in the game's queue: no animation lock, nothing queued.
    private static bool FreeToAct()
    {
        var actions = ActionManager.Instance();
        return actions is not null && actions->AnimationLock <= 0 && !actions->ActionQueued;
    }

    // The short mitigation once the HP falls under 80% without its own buff up, and the warrior's and the gunbreaker's
    // heals; never a bigger cooldown. At most one press, and only while the character is free to act.
    private static void MitigateByHp(uint[] mitigations, IPlayerCharacter player)
    {
        var actions = ActionManager.Instance();
        if (actions is null || player.MaxHp == 0 || !FreeToAct())
        {
            return;
        }

        var ratio = (float)player.CurrentHp / player.MaxHp;
        var shortId = actions->GetAdjustedActionId(mitigations[0]);
        if (ratio < ShortMitigationHpRatio && !BuffUp(player, shortId) && UseByHp(actions, shortId, player, ratio))
        {
            return;
        }

        if (mitigations == warriorMitigations && ratio < WarriorHealHpRatio)
        {
            if (!UseByHp(actions, ThrillOfBattle, player, ratio))
            {
                UseByHp(actions, Equilibrium, player, ratio);
            }
        }
        else if (mitigations == gunbreakerMitigations && ratio < AuroraHpRatio && !StatusUp(player, AuroraStatus))
        {
            UseByHp(actions, Aurora, player, ratio);
        }
    }

    private static bool UseByHp(ActionManager* actions, uint actionId, IPlayerCharacter player, float ratio)
    {
        if (!Ready(actions, actionId, player) || !actions->UseAction(ActionType.Action, actionId, player.GameObjectId))
        {
            return false;
        }

        RunLog.Info($"Tankbuster: HP at {ratio:P0}; used {ActionName(actionId)}.");
        return true;
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
