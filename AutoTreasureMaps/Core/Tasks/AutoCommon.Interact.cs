using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Travel;
using clib.Extensions;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int InteractObjectWaitMs = 10_000;
    private const int InteractApproachWatchdogMs = 20_000;
    // Travel arrival includes slack; like the triad NPC approach, use a strict distance before interacting.
    private const float InteractRangeMeters = 3f;
    // The treasure vault's gates stand 3 to 4m above the floor in front of them.
    private const float InteractHeightMeters = 5f;
    private const float InteractApproachToleranceMeters = 0.5f;
    // An object on a wall or a ledge cannot be stood on, so the walk aims at the reachable floor in front of it instead.
    private const float InteractStandSearchMeters = 5f;
    private const int InteractReadyTimeoutMs = 10_000;
    private const int InteractTargetSettleMs = 1_000;
    private const int InteractResultTimeoutMs = 5_000;
    private const int InteractUiCheckFrames = 2;

    private protected Task<IGameObject?> ApproachObject(uint baseId, string scope)
        => ApproachObject(() => NpcInteraction.FindNearest(baseId), $"object {baseId}", scope);

    // find is asked afresh each time, so an object that respawns or changes is never held past its frame.
    private protected async Task<IGameObject?> ApproachObject(Func<IGameObject?> find, string name, string scope)
    {
        if (!await WaitUntilTimed(() => find() is not null, InteractObjectWaitMs, $"{scope}-object"))
        {
            Diag($"{scope}: {name} is not among the nearby objects");
            return null;
        }

        if (find() is not { } target)
        {
            return null;
        }

        if (WithinInteractRange(target))
        {
            return target;
        }

        var targetPosition = target.Position;
        var standPoint = NavmeshIPC.Instance.NearestPointReachable(targetPosition, InteractStandSearchMeters, InteractStandSearchMeters) ?? targetPosition;
        var approachScope = $"{scope}-approach";
        Diag($"{approachScope}: {DistanceTo(targetPosition):F1}m from {name}, out of interact range; stepping to {FormatPosition(standPoint)}");
        var approach = new MoveOp(move => move.MoveInZone(
            standPoint,
            MovementConfig.InteractRange.WithTolerance(InteractApproachToleranceMeters),
            () => WithinInteractRange(target)));
        await RunCancellable(approach, InteractApproachWatchdogMs, approachScope, StuckDetector.MoveStallAbort(approachScope));
        if (approach.Fault is { } fault)
        {
            Diag($"{approachScope}: faulted: {fault.Message}");
        }

        if (!CancelToken.IsCancellationRequested && WithinInteractRange(target))
        {
            return target;
        }

        Diag($"{approachScope}: still out of interact range, {DistanceTo(targetPosition):F1}m from {name}");
        return null;
    }

    // Measured across the floor: an object set into a wall or a gate stands a few metres above the floor in front of
    // it, and the game lets it be used from there.
    private static bool WithinInteractRange(IGameObject target)
        => Svc.Objects.LocalPlayer is { } player
            && GroundDistance.Between(player.Position, target.Position) <= InteractRangeMeters
            && MathF.Abs(player.Position.Y - target.Position.Y) <= InteractHeightMeters
            && target.IsInInteractRange();

    private protected async Task<bool> ReadyToInteract(string scope)
    {
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            Diag($"{scope}: dismounting to interact");
            await SafeDismount($"{scope}-dismount");
        }

        if (await WaitUntilTimed(NpcInteraction.PlayerReady, InteractReadyTimeoutMs, $"{scope}-ready", InteractUiCheckFrames))
        {
            return true;
        }

        Diag($"{scope}: cannot interact yet ({NpcInteraction.DescribeBlockers()})");
        return false;
    }

    // True once doneWhen holds after the interaction; a message the object shows first is moved past.
    private protected async Task<bool> InteractWithObject(IGameObject target, Func<bool> doneWhen, string scope)
    {
        NpcInteraction.Target(target);
        await WaitUntilTimed(() => NpcInteraction.IsTargeted(target), InteractTargetSettleMs, $"{scope}-target", checkFrames: 1);
        if (CancelToken.IsCancellationRequested || !WithinInteractRange(target))
        {
            Diag($"{scope}: not interacting, {DistanceTo(target.Position):F1}m away; approach will be retried");
            return false;
        }

        var result = NpcInteraction.Interact(target);
        if (result == 0)
        {
            Diag($"{scope}: the game rejected the interaction ({NpcInteraction.DescribeBlockers()})");
            return false;
        }

        Diag($"{scope}: interacted (result {result}); waiting for it to take effect");
        return await WaitForInteraction(doneWhen, scope);
    }

    private async Task<bool> WaitForInteraction(Func<bool> doneWhen, string scope)
    {
        var deadline = Environment.TickCount64 + InteractResultTimeoutMs;
        var sawMessage = false;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (doneWhen())
            {
                return true;
            }

            if (NpcInteraction.TalkOpen())
            {
                if (!sawMessage)
                {
                    Diag($"{scope}: a message is showing; moving past it");
                    sawMessage = true;
                }

                NpcInteraction.ProgressTalk();
            }

            await NextFrame(InteractUiCheckFrames);
        }

        Diag($"{scope}: the interaction did not take effect within {InteractResultTimeoutMs / TimeUnits.MillisecondsPerSecond}s ({NpcInteraction.DescribeBlockers()})");
        return false;
    }
}
