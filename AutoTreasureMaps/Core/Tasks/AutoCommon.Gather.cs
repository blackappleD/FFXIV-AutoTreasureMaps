using AutoTreasureMaps.Core.Gather;
using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

internal enum GatherOutcome : byte
{
    Gathered,
    NoMap,
    // The game refused the map as over the day's limit, though the allowance read as available.
    AllowanceUsed,
    Failed,
    Cancelled,
}

internal readonly record struct GatherAnswer(GatherOutcome Outcome, LocString Reason, uint MapItemId)
{
    public static GatherAnswer Cancelled => new(GatherOutcome.Cancelled, default, 0);

    public static GatherAnswer Gathered(uint mapItemId) => new(GatherOutcome.Gathered, default, mapItemId);

    public static GatherAnswer NoMap(LocString reason) => new(GatherOutcome.NoMap, reason, 0);

    public static GatherAnswer AllowanceUsed => new(GatherOutcome.AllowanceUsed, L.Gather.ReasonLimitReached, 0);

    public static GatherAnswer Failed(LocString reason) => new(GatherOutcome.Failed, reason, 0);
}

// The daily map gather: switch to the gatherer's gearset, reach the area, open live nodes until one shows the map, gather
// it, and switch back. A node without the map is gathered out, because a node that is only closed stays up with the same
// items, and only a node gathered out makes way for the next one. The gatherer never fights: under attack it leaves.
public abstract partial class AutoCommon
{
    private const int GatherReplyWaitMs = 5_000;
    // Nodes are listed out to about 100 m, so arriving this near the first known node puts the area's live ones in view.
    private const float GatherAreaArriveMeters = 30f;
    private const float GatherSweepArriveMeters = 15f;
    // Sweep spots closer than this to one already searched show the same nodes.
    private const float GatherSweepMergeMeters = 40f;
    private const float GatherNodeArriveMeters = 4f;
    // Lands beside the node rather than on it, inside the 3 m the interaction needs.
    private const float GatherNodeStandOffMeters = 2.5f;
    private const int GatherNodeLookMs = 3_000;
    private const int GatherItemsWaitMs = 2_000;
    // A gather that has not started by then was refused: the item is locked or the bags are full.
    private const int GatherActionStartWaitMs = 2_500;
    private const int GatherActionEndWaitMs = 8_000;
    private const int GatherResultWaitMs = 10_000;
    // A gather misses when the slot's chance is under 100%, and the map then stays in the window for another try.
    private const int MaxMapAttempts = 3;
    private const int GatherCloseWaitMs = 3_000;
    // Room for every gather a node's integrity allows, with its bonuses.
    private const int MaxNodeGathers = 12;
    // A filler that needs a bag slot may not take the last one, which the map needs.
    private const uint MapBagSlots = 1;
    private const int GatherSettleMs = 300;
    private const int GatherPollFrames = 3;
    private const int LeaveAttackersMs = 60_000;
    private const float LeaveArriveMeters = 5f;
    // The game refuses a gearset change for a moment after combat or a gathering window ends.
    private const int SwitchBackRetryMs = 3_000;

    private Vector3? gatherRetreat;

    // Before the run carries on: a gather cut short can leave the character on the gatherer. A class the player picked
    // since is left alone.
    private protected async Task<bool> ReturnFromGatherer()
    {
        if (GatherGearsetReturn.Settling)
        {
            await WaitUntilTimed(static () => !GatherGearsetReturn.Settling, GatherGearsetReturn.SwitchSettleMs, "gather-return-settle", 1);
        }

        if (!GatherGearsetReturn.TryGetOwed(out var owed))
        {
            return true;
        }

        Diag($"Gather: the character is still on {GearsetSwitcher.ClassJobLabel(GearsetSwitcher.CurrentClassJobId())} from an earlier gather; switching back first");
        return await SwitchBack(owed, "gather-return");
    }

    // The caller has planned the gather and confirmed the allowance; the gearset is switched back on every way out.
    private protected async Task<GatherAnswer> GatherDailyMap(DailyMapPlan plan, int maxVisits)
    {
        const string scope = "gather";
        await FightOffAttackers(scope);
        if (CancelToken.IsCancellationRequested)
        {
            return GatherAnswer.Cancelled;
        }

        if (Svc.Condition[ConditionFlag.InCombat])
        {
            return GatherAnswer.Failed(L.Gather.ReasonInCombat);
        }

        var memory = GatherGearsetReturn.Remember(plan.ClassJobId);
        try
        {
            var answer = await SwitchAndGather(plan, maxVisits, scope);
            if (CancelToken.IsCancellationRequested)
            {
                return GatherAnswer.Cancelled with { MapItemId = answer.MapItemId };
            }

            if (await SwitchBack(memory, $"{scope}-switch-back"))
            {
                return answer;
            }

            return CancelToken.IsCancellationRequested
                ? GatherAnswer.Cancelled with { MapItemId = answer.MapItemId }
                : GatherAnswer.Failed(L.Gather.ReasonSwitchBackFailed) with { MapItemId = answer.MapItemId };
        }
        finally
        {
            LetGoOfGearset();
        }
    }

    // Asks the server afresh, as the field may be stale since the last request. Without a reply, Read would fall back
    // to the value from before the request, so the allowance counts as unknown instead.
    private protected async Task<AllowanceReading> RefreshAllowance(string reason)
    {
        MapAllowance.Request(reason);
        var replied = await WaitUntilTimed(() => !MapAllowance.ReplyPending, GatherReplyWaitMs, "allowance reply");
        return replied ? MapAllowance.Read() : default;
    }

    // Every way out of the gather passes here, a cancel or a fault included. One still on the gatherer asks for the switch
    // at once; the game refuses it in combat or while a node is open, so GatherGearsetReturn asks again once it can.
    private static void LetGoOfGearset()
    {
        GatherGearsetReturn.Release();
        if (GatherGearsetReturn.TryGetOwed(out var owed))
        {
            GatherGearsetReturn.Ask(owed, "gather cut short");
        }
    }

    private async Task<GatherAnswer> SwitchAndGather(DailyMapPlan plan, int maxVisits, string scope)
    {
        Status = Loc.T(L.Status.SwitchingToGatherer);
        var switched = await GearsetSwitcher.EquipFor(plan.ClassJobId, plan.GearsetIndex, CancelToken);
        switch (switched)
        {
            case GearsetSwitchResult.Cancelled:
                return GatherAnswer.Cancelled;
            case GearsetSwitchResult.InCombat:
                return GatherAnswer.Failed(L.Gather.ReasonInCombat);
            case GearsetSwitchResult.NoGearset:
                return GatherAnswer.Failed(plan.Gatherer == Gatherer.Miner ? L.Gather.ReasonNoMinerGearset : L.Gather.ReasonNoBotanistGearset);
            case GearsetSwitchResult.Refused or GearsetSwitchResult.TimedOut:
                return GatherAnswer.Failed(L.Gather.ReasonSwitchFailed);
        }

        return await GatherInArea(plan, maxVisits, scope);
    }

    private async Task<GatherAnswer> GatherInArea(DailyMapPlan plan, int maxVisits, string scope)
    {
        var area = plan.Area;
        var start = NearestKnownNode(area, area.Centre);
        gatherRetreat = AetherytePosition(area) ?? start;
        Diag($"Gather: base {area.BaseId} in {TerritoryNames.Of(area.TerritoryId)} for {plan.Group.MapItemIds.Length} map(s) of level {plan.Group.Level}, up to {maxVisits} node(s)");
        if (UnderAttack())
        {
            return await LeaveAttackers($"{scope}-start");
        }

        Status = Loc.T(L.Status.TravellingToGatherArea);
        var arrived = await TravelTo(area.TerritoryId, start, GatherAreaArriveMeters);
        if (UnderAttack())
        {
            return await LeaveAttackers($"{scope}-travel");
        }

        if (!arrived)
        {
            return CancelToken.IsCancellationRequested ? GatherAnswer.Cancelled : GatherAnswer.Failed(L.Gather.ReasonTravelFailed);
        }

        var standing = new List<GatherNodeKey>();
        var searched = new bool[area.Nodes.Length];
        for (var visit = 1; visit <= maxVisits; visit++)
        {
            var node = await FindGatherNode(area, standing, searched, scope);
            if (CancelToken.IsCancellationRequested)
            {
                return GatherAnswer.Cancelled;
            }

            if (UnderAttack())
            {
                return await LeaveAttackers($"{scope}-search");
            }

            if (node is null)
            {
                Diag($"Gather: no live node of base {area.BaseId} left to open after {visit - 1} visit(s)");
                return GatherAnswer.NoMap(visit == 1 ? L.Gather.ReasonNoNode : L.Gather.ReasonNoMap);
            }

            var answer = await VisitNode(plan, node, standing, $"{scope}-node#{visit}");
            if (answer.Outcome != GatherOutcome.NoMap)
            {
                return answer;
            }

            // The node just used makes way for the next pair, which may spawn where the search already looked.
            Array.Clear(searched);
        }

        Diag($"Gather: opened {maxVisits} node(s) without the map");
        return GatherAnswer.NoMap(L.Gather.ReasonNoMap);
    }

    private async Task<IGameObject?> FindGatherNode(MapGatherArea area, List<GatherNodeKey> standing, bool[] searched, string scope)
    {
        if (await WaitUntilTimed(() => GatherOps.FindLiveNode(area, standing) is not null, GatherNodeLookMs, $"{scope}-look"))
        {
            return GatherOps.FindLiveNode(area, standing);
        }

        var nodes = area.Nodes;
        while (!CancelToken.IsCancellationRequested && !UnderAttack())
        {
            var next = NextSearchSpot(nodes.AsSpan(), searched);
            if (next < 0)
            {
                return null;
            }

            MarkSearched(nodes.AsSpan(), searched, next);
            Status = Loc.T(L.Status.LookingForNode);
            await TravelTo(area.TerritoryId, nodes[next], GatherSweepArriveMeters);
            if (await WaitUntilTimed(() => GatherOps.FindLiveNode(area, standing) is not null, GatherNodeLookMs, $"{scope}-look"))
            {
                return GatherOps.FindLiveNode(area, standing);
            }
        }

        return null;
    }

    private async Task<GatherAnswer> VisitNode(DailyMapPlan plan, IGameObject node, List<GatherNodeKey> standing, string scope)
    {
        var key = GatherOps.KeyOf(node);
        var objectId = node.GameObjectId;
        Status = Loc.T(L.Status.HeadingToNode);
        Diag($"{scope}: node {node.BaseId} at {FormatPosition(node.Position)}, {DistanceTo(node.Position):F0}m away");
        if (DistanceTo(node.Position) > GatherNodeArriveMeters)
        {
            await TravelTo(plan.Area.TerritoryId, node.Position, GatherNodeArriveMeters);
        }

        if (UnderAttack())
        {
            return await LeaveAttackers(scope);
        }

        if (CancelToken.IsCancellationRequested)
        {
            return GatherAnswer.Cancelled;
        }

        if (Svc.Condition[ConditionFlag.Mounted])
        {
            await LandAndDismount(key.Position, GatherNodeStandOffMeters, $"{scope}-land");
        }

        var target = await ApproachObject(() => GatherOps.FindById(objectId), "the gathering node", scope);
        if (target is null || !await ReadyToInteract(scope) || !await InteractWithObject(target, GatherOps.WindowOpen, scope))
        {
            if (UnderAttack())
            {
                return await LeaveAttackers(scope);
            }

            if (CancelToken.IsCancellationRequested)
            {
                return GatherAnswer.Cancelled;
            }

            Diag($"{scope}: could not open the node; leaving it");
            standing.Add(key);
            return GatherAnswer.NoMap(L.Gather.ReasonNoMap);
        }

        await WaitUntilTimed(GatherOps.HasItems, GatherItemsWaitMs, $"{scope}-items", GatherPollFrames);
        return await WorkNode(plan, key, standing, scope);
    }

    // Gathers the map when the node shows it, and otherwise spends the node on its other items until it closes.
    private async Task<GatherAnswer> WorkNode(DailyMapPlan plan, GatherNodeKey key, List<GatherNodeKey> standing, string scope)
    {
        var plugin = Plugin.Instance;
        var mapItemIds = plan.Group.MapItemIds;
        var refusedSlots = 0;
        var reportedSibling = false;
        for (var gather = 0; gather < MaxNodeGathers; gather++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                GatherOps.Close();
                return GatherAnswer.Cancelled;
            }

            if (UnderAttack())
            {
                return await LeaveAttackers(scope);
            }

            // The window redraws between gathers, and only the gathering state ending says the node closed.
            await WaitUntilTimed(() => GatherOps.WindowOpen() || !GatherOps.Gathering(), GatherCloseWaitMs, $"{scope}-window", 1);
            if (!GatherOps.WindowOpen())
            {
                Diag($"{scope}: the node is gathered out without showing the map");
                return GatherAnswer.NoMap(L.Gather.ReasonNoMap);
            }

            var mapSlot = GatherOps.FindSlot(mapItemIds, out var mapItemId);
            if (mapSlot != GatherOps.NoSlot && DailyMapPlanner.IsWanted(plugin, mapItemId))
            {
                return await GatherMap(mapSlot, mapItemId, scope);
            }

            if (mapSlot != GatherOps.NoSlot && !reportedSibling)
            {
                reportedSibling = true;
                Diag($"{scope}: the node shows map {mapItemId}, which is switched off or already in the bags; gathering the node out instead");
            }

            var slot = GatherOps.PickFillerSlot(mapItemIds, refusedSlots, GatherOps.FreeBagSlots() > MapBagSlots);
            if (slot == GatherOps.NoSlot)
            {
                break;
            }

            Status = Loc.T(L.Status.GatheringOutNode);
            var since = TreasureMessageWatcher.Now;
            GatherOps.GatherSlot(slot);
            if (!await WaitUntilTimed(() => GatherOps.ActionUnderWay() || !GatherOps.WindowOpen(), GatherActionStartWaitMs, $"{scope}-gather-start", 1))
            {
                if (Messages.ArrivedSince(TreasureLogMessages.GatheringBagsFull, since))
                {
                    await CloseGatherWindow(scope);
                    return GatherAnswer.Failed(L.Gather.ReasonBagsFull);
                }

                refusedSlots |= 1 << slot;
                Diag($"{scope}: slot {slot} would not gather; trying another");
                continue;
            }

            await WaitUntilTimed(() => !GatherOps.ActionUnderWay(), GatherActionEndWaitMs, $"{scope}-gather-end", 1);
            await DelayMs(GatherSettleMs);
        }

        Diag($"{scope}: nothing left that this gatherer can take from the node; leaving it standing");
        await CloseGatherWindow(scope);
        standing.Add(key);
        return GatherAnswer.NoMap(L.Gather.ReasonNoMap);
    }

    private async Task<GatherAnswer> GatherMap(int slot, uint mapItemId, string scope)
    {
        Status = Loc.T(L.Status.GatheringMap);
        var answer = GatherAnswer.Failed(L.Gather.ReasonNotConfirmed);
        for (var attempt = 1; attempt <= MaxMapAttempts; attempt++)
        {
            Diag($"{scope}: the node shows map {mapItemId} in slot {slot}; gathering it (try {attempt}/{MaxMapAttempts})");
            var since = TreasureMessageWatcher.Now;
            GatherOps.GatherSlot(slot);
            var result = await WaitForGatherResult(mapItemId, since);
            if (CancelToken.IsCancellationRequested)
            {
                GatherOps.Close();
                return GatherAnswer.Cancelled;
            }

            await WaitUntilTimed(() => !GatherOps.ActionUnderWay(), GatherActionEndWaitMs, $"{scope}-map-end", 1);
            if (result is { } settled)
            {
                answer = settled;
                break;
            }

            await WaitUntilTimed(() => GatherOps.WindowOpen() || !GatherOps.Gathering(), GatherCloseWaitMs, $"{scope}-map-window", 1);
            if (GatherOps.ItemAt(slot) != mapItemId)
            {
                break;
            }
        }

        Diag(answer.Outcome == GatherOutcome.Gathered
            ? $"{scope}: map {mapItemId} gathered"
            : $"{scope}: the map was not gathered ({answer.Reason.Key})");
        await CloseGatherWindow(scope);
        return answer;
    }

    private async Task<GatherAnswer?> WaitForGatherResult(uint mapItemId, long since)
    {
        var deadline = Environment.TickCount64 + GatherResultWaitMs;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (GatherResultSince(mapItemId, since) is { } result)
            {
                return result;
            }

            await NextFrame(GatherPollFrames);
        }

        return null;
    }

    private static GatherAnswer? GatherResultSince(uint mapItemId, long since)
    {
        if (Messages.TryGetLatest(TreasureLogMessages.ItemObtained, since, out var obtained)
            && obtained.TryGetParameter(0, out var itemId) && (uint)itemId == mapItemId)
        {
            return GatherAnswer.Gathered(mapItemId);
        }

        if (Plugin.Instance.Catalog.TryGetByTimewornItem(mapItemId, out var tier) && MapBag.TryFind(Plugin.Instance.Catalog, tier.Rank, out _))
        {
            return GatherAnswer.Gathered(mapItemId);
        }

        if (Messages.ArrivedSince(TreasureLogMessages.GatheringLimitReached, since) || Messages.ArrivedSince(TreasureLogMessages.GatheringNoLongerAble, since))
        {
            return GatherAnswer.AllowanceUsed;
        }

        if (Messages.ArrivedSince(TreasureLogMessages.GatheringCannotCarryMore, since))
        {
            return GatherAnswer.Failed(L.Gather.ReasonAlreadyCarrying);
        }

        if (Messages.ArrivedSince(TreasureLogMessages.GatheringRateZero, since))
        {
            return GatherAnswer.Failed(L.Gather.ReasonRateZero);
        }

        return Messages.ArrivedSince(TreasureLogMessages.GatheringBagsFull, since) ? GatherAnswer.Failed(L.Gather.ReasonBagsFull) : null;
    }

    private async Task CloseGatherWindow(string scope)
    {
        if (!GatherOps.Gathering() && !GatherOps.WindowOpen())
        {
            return;
        }

        GatherOps.Close();
        if (!await WaitUntilTimed(() => !GatherOps.Gathering(), GatherCloseWaitMs, $"{scope}-close", 1))
        {
            GatherOps.Close();
        }
    }

    private static bool UnderAttack() => Svc.Condition[ConditionFlag.InCombat] || IsKnockedOut();

    // A gatherer cannot fight back, and in combat it cannot mount, so it moves off toward the aetheryte, riding if it
    // still sits on its mount, until whatever hit it gives up.
    private async Task<GatherAnswer> LeaveAttackers(string scope)
    {
        Warn($"{scope}: attacked while on the gatherer ({ConditionTag()}); leaving instead of fighting");
        GatherOps.Close();
        await LeaveCombat(scope);
        return CancelToken.IsCancellationRequested ? GatherAnswer.Cancelled : GatherAnswer.Failed(L.Gather.ReasonAttacked);
    }

    private async Task LeaveCombat(string scope)
    {
        if (!Svc.Condition[ConditionFlag.InCombat] || IsKnockedOut() || gatherRetreat is not { } retreat)
        {
            return;
        }

        Status = Loc.T(L.Status.LeavingUnderAttack);
        var movement = !Svc.Condition[ConditionFlag.Mounted]
            ? walkMovement
            : Svc.Condition[ConditionFlag.InFlight] ? flightMovement : groundMountMovement;
        var leave = new MoveOp(move => move.MoveInZone(retreat, movement.WithTolerance(LeaveArriveMeters), () => !Svc.Condition[ConditionFlag.InCombat]));
        await RunCancellable(leave, LeaveAttackersMs, $"{scope}-leave", IsKnockedOut);
        if (leave.Fault is { } fault)
        {
            Diag($"{scope}-leave: faulted: {fault.Message}");
        }

        if (Svc.Condition[ConditionFlag.InCombat])
        {
            Warn($"{scope}-leave: still in combat after {LeaveAttackersMs / TimeUnits.MillisecondsPerSecond}s");
        }
    }

    private async Task<bool> SwitchBack(GearsetReturn memory, string scope)
    {
        if (GearsetSwitcher.CurrentClassJobId() == memory.ClassJobId)
        {
            GatherGearsetReturn.Done();
            return true;
        }

        Status = Loc.T(L.Status.SwitchingBackGearset);
        await CloseGatherWindow(scope);
        await LeaveCombat(scope);
        var result = await GearsetSwitcher.EquipFor(memory.ClassJobId, memory.GearsetIndex, CancelToken);
        if (result is GearsetSwitchResult.Refused or GearsetSwitchResult.TimedOut or GearsetSwitchResult.InCombat)
        {
            await DelayMs(SwitchBackRetryMs);
            await LeaveCombat(scope);
            result = await GearsetSwitcher.EquipFor(memory.ClassJobId, memory.GearsetIndex, CancelToken);
        }

        if (result is GearsetSwitchResult.Switched or GearsetSwitchResult.AlreadyOnClass)
        {
            GatherGearsetReturn.Done();
            return true;
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn($"{scope}: could not switch back to {GearsetSwitcher.ClassJobLabel(memory.ClassJobId)} ({result}, {ConditionTag()})");
        }

        return false;
    }

    private static Vector3 NearestKnownNode(in MapGatherArea area, Vector3 point)
    {
        var nodes = area.Nodes.AsSpan();
        var best = nodes[0];
        var bestDistance = float.MaxValue;
        for (var index = 0; index < nodes.Length; index++)
        {
            var distance = GroundDistance.SquaredBetween(point, nodes[index]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = nodes[index];
            }
        }

        return best;
    }

    private static Vector3? AetherytePosition(in MapGatherArea area)
    {
        var aetherytes = ZoneAetherytes.TeleportableIn(area.TerritoryId).Span;
        for (var index = 0; index < aetherytes.Length; index++)
        {
            if (aetherytes[index].Id == area.AetheryteId)
            {
                return aetherytes[index].Position;
            }
        }

        return null;
    }

    private static int NextSearchSpot(ReadOnlySpan<Vector3> nodes, bool[] searched)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return -1;
        }

        var position = player.Position;
        var best = -1;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < nodes.Length; index++)
        {
            if (searched[index])
            {
                continue;
            }

            var distance = GroundDistance.SquaredBetween(position, nodes[index]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }

    private static void MarkSearched(ReadOnlySpan<Vector3> nodes, bool[] searched, int spot)
    {
        const float mergeSquared = GatherSweepMergeMeters * GatherSweepMergeMeters;
        for (var index = 0; index < nodes.Length; index++)
        {
            if (GroundDistance.SquaredBetween(nodes[spot], nodes[index]) <= mergeSquared)
            {
                searched[index] = true;
            }
        }
    }
}
