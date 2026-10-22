using AutoTreasureMaps.Core.Game;
using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    // The members land a few metres off the spot, so the leader digs once all of them are this close.
    private const float FollowersNearMeters = 10f;
    private const int FollowersWaitMs = 90_000;
    // A member that comes later than this after the leader is reported, so the lag can be checked run by run.
    private protected const int FollowerLagWarnMs = 5_000;
    // The leader's fight is joined within this reach; the guards stay by their coffer.
    private const float LeaderFightMeters = 50f;
    // A treasure hunt runs about five minutes at most.
    private const int FightAlongCapMs = 330_000;
    private const int FightAlongQuietMs = 3_000;
    private const float MembersInRoomMeters = 25f;
    private const int MembersInRoomWaitMs = 60_000;
    private const string PartyRollThrottleKey = "atm-party-roll";
    private const int PartyRollThrottleMs = 1_000;

    // Party mode needs no setting: a party on the character's own world is taken to be members that all run this plugin.
    private protected static bool LeadsPartyMode()
        => PartyPresence.LocalParty() && PartyPresence.LeadsTheParty();

    // The leader posts the spot as it sets off, so the members leave at the same moment.
    private void PostSpotToParty(DigSpot spot)
    {
        if (!LeadsPartyMode())
        {
            return;
        }

        var posted = PartySpotLink.Post(spot);
        Diag($"Party: {(posted ? "posted" : "could not post")} the spot in {spot.ZoneName} at {FormatPosition(spot.Position)} to the party.");
    }

    // On the spot, the leader waits for every member before it digs; the wait is capped, and how long it took is logged.
    // False only when the run was stopped meanwhile.
    private async Task<bool> WaitForFollowers(DigSpot spot)
    {
        if (!LeadsPartyMode())
        {
            return true;
        }

        var arrivedAt = Environment.TickCount64;
        Status = "Waiting for the party";
        var gathered = PartyPresence.FarthestMemberFrom(spot.Position) <= FollowersNearMeters
            || await WaitUntilTimed(() => PartyPresence.FarthestMemberFrom(spot.Position) <= FollowersNearMeters, FollowersWaitMs, "party at the spot");
        if (CancelToken.IsCancellationRequested)
        {
            return false;
        }

        var lagMs = Environment.TickCount64 - arrivedAt;
        if (!gathered)
        {
            Warn($"Party: the members did not reach the spot within {FollowersWaitMs / TimeUnits.MillisecondsPerSecond}s (farthest {PartyPresence.FarthestMemberFrom(spot.Position):F0}m); digging anyway.");
        }
        else if (lagMs > FollowerLagWarnMs)
        {
            Warn($"Party: the members reached the spot {lagMs / 1000.0:F1}s after the leader.");
        }
        else
        {
            Diag($"Party: the members reached the spot {lagMs / 1000.0:F1}s after the leader.");
        }

        return true;
    }

    // In a dungeon the leader opens the next coffer or moves the run on only once every member is in the room with it,
    // so no one is left behind a gate or out of a fight; the wait is capped.
    private async Task WaitForMembersInRoom(string scope)
    {
        if (!LeadsPartyMode() || PartyPresence.FarthestMemberFrom(PlayerPosition()) <= MembersInRoomMeters)
        {
            return;
        }

        var startedAt = Environment.TickCount64;
        Status = "Waiting for the party";
        var gathered = await WaitUntilTimed(() => PartyPresence.FarthestMemberFrom(PlayerPosition()) <= MembersInRoomMeters, MembersInRoomWaitMs, "party in the room");
        var waitedMs = Environment.TickCount64 - startedAt;
        if (!gathered && !CancelToken.IsCancellationRequested)
        {
            Warn($"{scope}: the members did not join within {MembersInRoomWaitMs / TimeUnits.MillisecondsPerSecond}s (farthest {PartyPresence.FarthestMemberFrom(PlayerPosition()):F0}m); going on.");
        }
        else if (waitedMs > FollowerLagWarnMs)
        {
            Warn($"{scope}: the members joined {waitedMs / 1000.0:F1}s later.");
        }
        else
        {
            Diag($"{scope}: the members joined {waitedMs / 1000.0:F1}s later.");
        }
    }

    // Whether the follower has a fight to join: its own, or the leader's close by. A member standing next to the leader
    // is not in combat until something turns on it, so the leader's fight is joined as soon as it starts.
    private protected static bool PartyFightOn()
        => Svc.Condition[ConditionFlag.InCombat] || LeaderNearby() is { } leader && leader.StatusFlags.HasFlag(StatusFlags.InCombat);

    private protected static IBattleChara? LeaderNearby()
        => PartyPresence.Leader() is { } member && Svc.Objects.SearchById(member.EntityId) is IBattleChara leader
            && Vector3.Distance(leader.Position, PlayerPosition()) <= LeaderFightMeters
            ? leader
            : null;

    // Fights beside the leader until neither of them is in combat: the leader's target is taken when the character has
    // none, and the combat plugin has its movement back meanwhile.
    private protected async Task FightAlong(string scope)
    {
        EnsureCombatPreset();
        Status = "Fighting beside the leader";
        Diag($"{scope}: the party is fighting ({ConditionTag()}); joining in.");
        ReleaseCombatMovement(scope);
        var deadline = Environment.TickCount64 + FightAlongCapMs;
        var quietSince = 0L;
        try
        {
            while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested && !IsKnockedOut())
            {
                if (PartyFightOn())
                {
                    quietSince = 0;
                }
                else if (quietSince == 0)
                {
                    quietSince = Environment.TickCount64;
                }
                else if (Environment.TickCount64 - quietSince >= FightAlongQuietMs)
                {
                    break;
                }

                if (Svc.Targets.Target is not { IsDead: false } && LeaderNearby()?.TargetObject is IBattleNpc { IsDead: false } enemy && enemy.IsTargetable)
                {
                    Svc.Targets.Target = enemy;
                }

                await KeepPresetFighting(scope);
                RollPartyLoot();
                await NextFrame(FightTickFrames);
            }
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
            HoldCombatMovement(scope);
        }

        Diag($"{scope}: the fight is over.");
    }

    // Every item waiting on this character's roll is answered the way the settings say; Manual leaves them.
    private protected static void RollPartyLoot()
    {
        var choice = Plugin.Instance.Configuration.LootRoll;
        if (choice != LootRoll.Manual && LootRolls.Available && LootRolls.Pending(static _ => true) > 0
            && EzThrottler.Throttle(PartyRollThrottleKey, PartyRollThrottleMs))
        {
            LootRolls.Roll(choice, static _ => true);
        }
    }
}
