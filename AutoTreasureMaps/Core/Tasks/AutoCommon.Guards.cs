using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    // A wave can end a moment before the next one spawns, so the field must stay clear this long to count as cleared.
    private const int GuardQuietMs = 3_000;
    // Long enough for a wave to spawn and close in, counted from each fight round's start; a round whose guards never
    // show goes on to the lock, which answers that it is sealed if any are left.
    private const int NoGuardsGraceMs = 20_000;
    // Past the director's end the game has had this long to call time on the hunt itself.
    private const long HuntEndGraceSeconds = 10;
    private const int GuardFightCapMs = 600_000;
    private const int MinTrustedHuntMs = 60_000;
    private const float GuardRejoinWithinMeters = 3f;
    // Under attack at half health or less, a wait before fighting back only costs the character more.
    private const float EngageAtOnceAtHealth = 0.5f;

    private enum FightEnd : byte { Cleared, TimedOut, Died, Cancelled }

    // Fights the coffer's guards until the game reports them all defeated, or no guard of ours has been alive, and the
    // character out of combat, for a quiet moment after at least one was seen. The combat plugin gets its own movement
    // back for the fight and the hold returns afterwards.
    private async Task<FightEnd> FightGuards(MapInProgress map)
    {
        const string scope = "map-fight";
        EnterStep(TreasurePhase.Fighting);
        Status = Loc.T(L.Status.FightingGuards);
        var openedAt = map.CofferOpenedAtMs;
        if (Messages.ArrivedSince(TreasureLogMessages.GuardsDefeated, openedAt))
        {
            return FightEnd.Cleared;
        }

        // Only a map's first round waits: a later one goes back to a fight already joined.
        if (map.Wave == 0)
        {
            await HumanDelay(HumanAction.Engage, scope, EngageAtOnce);
            await LetTankPull(scope);
        }

        var roundStartedAt = Environment.TickCount64;
        EnsureCombatPreset();
        ReleaseCombatMovement(scope);
        map.Wave = Math.Max(map.Wave, 1);
        var waveSeenAt = Math.Max(openedAt, map.WaveSeenAtMs);
        var sawGuard = false;
        var quietSince = 0L;
        ulong lastPickedGuard = 0;
        try
        {
            while (true)
            {
                if (CancelToken.IsCancellationRequested)
                {
                    return FightEnd.Cancelled;
                }

                ReadHuntClock();
                NotePortal(map);
                if (Messages.ArrivedSince(TreasureLogMessages.HuntTimeExpired, openedAt))
                {
                    Diag($"{scope}: the game called time on the hunt in wave {map.Wave}.");
                    return FightEnd.TimedOut;
                }

                if (Messages.ArrivedSince(TreasureLogMessages.GuardsDefeated, openedAt))
                {
                    Diag($"{scope}: the game reports every guard defeated after {map.Wave} wave(s).");
                    return FightEnd.Cleared;
                }

                if (IsKnockedOut())
                {
                    if (!await RecoverInHunt(map))
                    {
                        return CancelToken.IsCancellationRequested ? FightEnd.Cancelled : FightEnd.Died;
                    }

                    EnterStep(TreasurePhase.Fighting);
                    EnsureCombatPreset();
                    ReleaseCombatMovement(scope);
                    quietSince = 0;
                    continue;
                }

                var now = Environment.TickCount64;
                if (Messages.TryGetLatest(TreasureLogMessages.Reinforcements, waveSeenAt + 1, out var reinforcements))
                {
                    waveSeenAt = reinforcements.AtMs;
                    map.WaveSeenAtMs = waveSeenAt;
                    map.Wave++;
                    quietSince = 0;
                    Diag($"{scope}: reinforcements, wave {map.Wave}.");
                }

                var survey = TreasureGuards.Survey(map.CofferPosition, GuardSearchMeters);
                ShowFight(map.Wave, survey.Ours);
                if (survey.Ours > 0 && !sawGuard)
                {
                    sawGuard = true;
                    Diag($"{scope}: {survey.Ours} guard(s) of ours in sight ({survey.Others} other hunt guard(s) nearby).");
                }

                var inCombat = Svc.Condition[ConditionFlag.InCombat];
                if (sawGuard && survey.Ours == 0 && !inCombat)
                {
                    if (quietSince == 0)
                    {
                        quietSince = now;
                    }
                    else if (now - quietSince >= GuardQuietMs)
                    {
                        Diag($"{scope}: no guard of ours alive and out of combat for {GuardQuietMs / TimeUnits.MillisecondsPerSecond}s after wave {map.Wave}; treating the field as cleared.");
                        return FightEnd.Cleared;
                    }
                }
                else
                {
                    quietSince = 0;
                }

                if (!sawGuard && !inCombat && now - roundStartedAt >= NoGuardsGraceMs)
                {
                    Diag($"{scope}: no guard of ours showed up within {NoGuardsGraceMs / TimeUnits.MillisecondsPerSecond}s and nothing attacks ({survey.Others} other hunt guard(s) nearby); trying the lock.");
                    return FightEnd.Cleared;
                }

                if (HuntOverdue(openedAt))
                {
                    Warn($"{scope}: the hunt's time is up with no word from the game; giving up on wave {map.Wave}.");
                    return FightEnd.TimedOut;
                }

                await KeepPresetFighting(scope);
                lastPickedGuard = PickGuard(survey, inCombat, lastPickedGuard, scope);
                await NextFrame(FightTickFrames);
            }
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
            HoldCombatMovement(scope);
        }
    }

    private static bool EngageAtOnce()
        => IsKnockedOut()
        || (Svc.Condition[ConditionFlag.InCombat]
            && Svc.Objects.LocalPlayer is { MaxHp: > 0 } player
            && player.CurrentHp <= player.MaxHp * EngageAtOnceAtHealth);

    // The preset only takes attackers, so a guard that has not engaged yet is targeted by hand; a target that is dead,
    // or neither an attacker nor a guard of ours, is dropped so the preset can take the next attacker.
    private ulong PickGuard(in GuardSurvey survey, bool inCombat, ulong lastPicked, string scope)
    {
        if (Svc.Targets.Target is { } target && (target.IsDead || (!EnmityList.Contains(target.EntityId) && !TreasureGuards.IsOurs(target))))
        {
            Svc.Targets.Target = null;
        }

        if (inCombat || Svc.Targets.Target is not null || survey.Nearest is not { } guard)
        {
            return lastPicked;
        }

        if (guard.GameObjectId != lastPicked)
        {
            Diag($"{scope}: out of combat with a guard of ours alive; targeting {guard.Name} ({guard.GameObjectId:X}).");
        }

        Svc.Targets.Target = guard;
        return guard.GameObjectId;
    }

    // A safety net under the game's own time-up message: the director's end counts only once the hunt has run a while,
    // since whether that end is the hunt's limit is still to be checked in game.
    private bool HuntOverdue(long openedAt)
    {
        var elapsed = Environment.TickCount64 - openedAt;
        if (elapsed > GuardFightCapMs)
        {
            return true;
        }

        var endsAt = mapProgress?.HuntEndsAt ?? 0;
        return endsAt > 0 && elapsed > MinTrustedHuntMs && TreasureHuntDirector.ServerNow() - endsAt > HuntEndGraceSeconds;
    }

    // A raise in a party keeps the hunt; a return home leaves its zone, which ends it.
    private async Task<bool> RecoverInHunt(MapInProgress map)
    {
        Diag($"Fight: knocked out in wave {map.Wave}.");
        if (!await RecoverFromKnockout() || CancelToken.IsCancellationRequested)
        {
            return false;
        }

        if (!HuntStillOn(map))
        {
            Diag("Fight: back on its feet, but the hunt is gone.");
            return false;
        }

        if (DistanceTo(map.CofferPosition) > GuardSearchMeters)
        {
            Diag($"Fight: back on its feet {DistanceTo(map.CofferPosition):F0}m from the coffer; heading back.");
            await HoldCombatMovementAndSettle("map-rejoin");
            await TravelTo(map.Spot.TerritoryId, map.CofferPosition, GuardRejoinWithinMeters);
        }

        return !CancelToken.IsCancellationRequested && HuntStillOn(map);
    }
}
