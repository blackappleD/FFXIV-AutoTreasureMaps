using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Game.Watchers;
using AutoTreasureMaps.Core.Localization;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    // A member that fell in the leader's fight lies within the fight's reach.
    private const float FallenSearchMeters = 50f;
    // Raise spells reach 30 m; the leader steps this close first, so a member at the edge is still in reach.
    private const float RaiseReachMeters = 25f;
    private const float RaiseWalkToleranceMeters = 20f;
    private const int RaiseWalkTimeoutMs = 20_000;
    // A job's actions come up a moment after a class change, and the MP for the spell may still be filling.
    private const int RaiseReadyWaitMs = 15_000;
    private const int InstantCastUpWaitMs = 2_000;
    // Swiftcast's animation lock holds the raise back for a moment.
    private const int RaiseAfterSwiftcastWaitMs = 3_000;
    // A raise cast with Swiftcast lands at once; a hard cast one runs 8 s to 10 s.
    private const int InstantRaiseLandMs = 3_000;
    private const int HardcastRaiseLandMs = 14_000;
    private const int RaiseTriesPerMember = 2;

    // Once a fight is over the leader raises every member that fell in it with Swiftcast and the job's raise: on the job
    // it plays if that one can raise, otherwise on the job the settings name, changing back to the gearset it wore
    // afterwards. The class cannot change inside a duty, so there only a job that can raise already does it. Swiftcast
    // still cooling down leaves a hard cast, which the fight being over makes safe. A fight that starts again ends it.
    private protected async Task RaiseFallenMembers(string scope)
    {
        var configuration = Plugin.Instance.Configuration;
        if (!configuration.RaiseAfterFight || !LeadsPartyMode() || CancelToken.IsCancellationRequested
            || Svc.Condition[ConditionFlag.InCombat] || IsKnockedOut() || FallenMember(null) is null)
        {
            return;
        }

        var wornJob = GearsetSwitcher.CurrentClassJobId();
        var wornGearset = GearsetSwitcher.CurrentGearsetIndex();
        var raiseJob = RaiseOps.CanRaise(wornJob) ? wornJob : configuration.RaiseClassJobId;
        if (!RaiseOps.CanRaise(raiseJob))
        {
            Warn($"{scope}: a member is down, but {GearsetSwitcher.ClassJobLabel(raiseJob)} cannot raise; pick a healer, Summoner or Red Mage in the settings.");
            return;
        }

        var changeJob = raiseJob != wornJob;
        if (changeJob && DutyWatcher.InContent())
        {
            Diag($"{scope}: a member is down, but the class cannot change inside a duty and {GearsetSwitcher.ClassJobLabel(wornJob)} cannot raise.");
            return;
        }

        Status = Loc.T(L.Status.RaisingMembers);
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            await SafeDismount($"{scope}-dismount");
        }

        if (changeJob)
        {
            var switched = await GearsetSwitcher.EquipFor(raiseJob, GearsetSwitcher.NoGearset, CancelToken);
            if (switched != GearsetSwitchResult.Switched)
            {
                Warn($"{scope}: a member is down, but changing to {GearsetSwitcher.ClassJobLabel(raiseJob)} did not work ({switched}).");
                return;
            }
        }

        try
        {
            await RaiseEachFallen(scope, raiseJob);
        }
        finally
        {
            // A stop cannot wait for the change back, so it is only asked for.
            if (changeJob && CancelToken.IsCancellationRequested)
            {
                GearsetSwitcher.EquipWithoutWaiting(wornJob, wornGearset);
            }
        }

        if (changeJob && !CancelToken.IsCancellationRequested)
        {
            await ChangeBackAfterRaise(scope, wornJob, wornGearset);
        }
    }

    private async Task RaiseEachFallen(string scope, uint raiseJob)
    {
        var tries = new Dictionary<uint, int>();
        HashSet<uint>? givenUp = null;
        while (!CancelToken.IsCancellationRequested)
        {
            if (Svc.Condition[ConditionFlag.InCombat] || IsKnockedOut())
            {
                Diag($"{scope}: the fight is on again; the raising stops.");
                return;
            }

            if (FallenMember(givenUp) is not { } member)
            {
                return;
            }

            var entityId = member.EntityId;
            if (await RaiseMember(scope, raiseJob, entityId))
            {
                continue;
            }

            tries[entityId] = tries.GetValueOrDefault(entityId) + 1;
            if (tries[entityId] >= RaiseTriesPerMember)
            {
                Warn($"{scope}: {member.Name.TextValue} could not be raised after {RaiseTriesPerMember} tries; leaving them.");
                (givenUp ??= []).Add(entityId);
            }
        }
    }

    // True once the raise has landed on the member, or the member is up again.
    private async Task<bool> RaiseMember(string scope, uint raiseJob, uint entityId)
    {
        var raiseAction = RaiseOps.RaiseActionFor(raiseJob);
        var raiseName = RaiseOps.ActionName(raiseAction);
        if (FallenBody(entityId) is not { } body)
        {
            return true;
        }

        var name = body.Name.TextValue;
        if (DistanceTo(body.Position) > RaiseReachMeters)
        {
            var walkScope = $"{scope}-walk";
            Diag($"{walkScope}: {DistanceTo(body.Position):F0}m from {name}; stepping into reach.");
            var walk = new MoveOp(move => move.MoveInZone(body.Position, MovementConfig.Default.WithTolerance(RaiseWalkToleranceMeters),
                () => FallenBody(entityId) is not { } fallen || DistanceTo(fallen.Position) <= RaiseReachMeters));
            await RunCancellable(walk, RaiseWalkTimeoutMs, walkScope, StuckDetector.MoveStallAbort(walkScope));
        }

        var targetId = body.GameObjectId;
        if (!await WaitUntilTimed(() => RaiseOps.ActionStatus(raiseAction, targetId) == 0 || RaiseLanded(entityId), RaiseReadyWaitMs, $"{scope}-ready"))
        {
            Warn($"{scope}: {raiseName} cannot be cast on {name} (the game answers {RaiseOps.ActionStatus(raiseAction, targetId)}).");
            return false;
        }

        if (RaiseLanded(entityId))
        {
            return true;
        }

        if (Svc.Objects.LocalPlayer is { } player && !RaiseOps.InstantCastUp(player) && !RaiseOps.RecastActive(RaiseOps.SwiftcastActionId)
            && RaiseOps.Use(RaiseOps.SwiftcastActionId, player.GameObjectId))
        {
            await WaitUntilTimed(() => Svc.Objects.LocalPlayer is { } caster && RaiseOps.InstantCastUp(caster), InstantCastUpWaitMs, $"{scope}-swiftcast");
            await WaitUntilTimed(() => RaiseOps.ActionStatus(raiseAction, targetId) == 0, RaiseAfterSwiftcastWaitMs, $"{scope}-after-swiftcast");
        }

        var instant = Svc.Objects.LocalPlayer is { } self && RaiseOps.InstantCastUp(self);
        if (!RaiseOps.Use(raiseAction, targetId))
        {
            Diag($"{scope}: the game did not take {raiseName} on {name}.");
            return false;
        }

        Diag($"{scope}: {(instant ? "instant" : "hard cast, Swiftcast still cooling down,")} {raiseName} on {name}.");
        return await WaitUntilTimed(() => RaiseLanded(entityId), instant ? InstantRaiseLandMs : HardcastRaiseLandMs, $"{scope}-land");
    }

    private async Task ChangeBackAfterRaise(string scope, uint wornJob, int wornGearset)
    {
        var back = await GearsetSwitcher.EquipFor(wornJob, wornGearset, CancelToken);
        if (back == GearsetSwitchResult.InCombat)
        {
            await FightOffAttackers(scope);
            back = await GearsetSwitcher.EquipFor(wornJob, wornGearset, CancelToken);
        }

        if (back is not (GearsetSwitchResult.Switched or GearsetSwitchResult.AlreadyOnClass or GearsetSwitchResult.Cancelled))
        {
            Warn($"{scope}: changing back to {GearsetSwitcher.ClassJobLabel(wornJob)} did not work ({back}).");
        }
    }

    // A raise on the way shows on the member; one that is up again is no longer fallen.
    private static bool RaiseLanded(uint entityId)
        => Svc.Objects.SearchById(entityId) is not IBattleChara body || !Fallen(body) || RaiseOps.RaisePending(body);

    private static IBattleChara? FallenBody(uint entityId)
        => Svc.Objects.SearchById(entityId) is IBattleChara body && Fallen(body) ? body : null;

    // The nearest member in the zone that is down with no raise on the way, within the fight's reach.
    private static IBattleChara? FallenMember(HashSet<uint>? skipped)
    {
        var party = Svc.Party;
        var territoryId = Svc.ClientState.TerritoryType;
        var position = PlayerPosition();
        IBattleChara? nearest = null;
        var nearestMeters = FallenSearchMeters;
        for (var index = 0; index < party.Length; index++)
        {
            if (party[index] is not { } member || PartyPresence.IsLocal(member) || member.Territory.RowId != territoryId
                || skipped?.Contains(member.EntityId) == true
                || Svc.Objects.SearchById(member.EntityId) is not IBattleChara body || !Fallen(body) || RaiseOps.RaisePending(body))
            {
                continue;
            }

            var meters = Vector3.Distance(body.Position, position);
            if (meters <= nearestMeters)
            {
                nearest = body;
                nearestMeters = meters;
            }
        }

        return nearest;
    }

    private static bool Fallen(IBattleChara body) => body.IsDead || body.CurrentHp == 0;
}
