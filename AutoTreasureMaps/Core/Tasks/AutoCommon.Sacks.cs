using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int SackTickFrames = 2;
    private const uint SprintGeneralActionId = 4;
    private const int SprintRetryMs = 1_000;
    // Run speed outside combat; the measured speed takes over once the character moves, Sprint and Heavy included.
    private const float SackRunSpeed = 6f;
    private const float SackSpeedFloor = 3f;
    private const float SackSpeedCeiling = 10f;
    private const float SackSpeedSmoothing = 0.2f;
    // Another plan replaces the one under way only when it scores this much better, so the walk does not dither.
    private const float SackRetargetGain = 1.25f;
    // A walk that gets no closer than this over that long is blocked; its sack is passed over for a while.
    private const float SackStallMeters = 0.5f;
    private const int SackStallMs = 1_500;
    private const int SackPassOverMs = 4_000;
    // A sack cannot be interacted with; it is picked up by standing right on it for a moment. The walk goes onto the
    // sack's centre, stops there for a fixed 0.3 s and goes on to the next. A sack's object lingers for a few seconds
    // after its pickup, so a sack just stood on is not walked to again while the point for it may still come; once the
    // duty list's count rose for it, never again.
    private const float SackOnTopMeters = 0.25f;
    // The pathfinder may end a walk a little short of the centre; that close and stopped counts as on it.
    private const float SackStoppedOnMeters = 0.5f;
    private const int SackStandMs = 300;
    // A sack's object lingers 2.5 to 3.2 s after its pickup, so one stood on is left alone this long; still there after
    // it, it was not picked up and is walked to again.
    private const int SackCreditWindowMs = 3_500;
    // The point comes 0.03 to 0.15 s after the character steps on its sack; a rise this soon after the stand is its.
    private const int SackPointLateMs = 500;
    // A partner this close to a sack may be picking it up as well.
    private const float PartnerOnSackMeters = 0.8f;
    private const float EscapeArriveMeters = 0.5f;
    // The sacks come a few seconds into the round; none by this time means the profile's ids are not the sacks'.
    private const int NoSackDescribeMs = 10_000;

    // The bonus round scatters sacks over the room for ninety seconds, each picked up by standing on it, and every ten
    // seconds chargers rush across it. The walk goes straight from sack to sack, stays on each until the duty list's
    // point count rises, steps out of a lane before its charge lands, and writes the round down for later.
    private protected async Task CollectBonusSacks(DungeonContext context)
    {
        const string scope = "dungeon-sacks";
        var dungeon = context.Dungeon;
        if (!dungeon.BonusNoticeShown)
        {
            dungeon.BonusNoticeShown = true;
            Diag("Dungeon: a bonus round began; picking up its sacks.");
        }

        EnterStep(TreasurePhase.Fighting);
        Status = Loc.T(L.Status.BonusSacks);
        HoldCombatMovement(scope);
        var field = new SackField(dungeon.Profile);
        using var capture = new SackRoundCapture(dungeon.Profile);
        if (capture.FilePath is { } path)
        {
            Diag($"{scope}: writing the round down to {path}.");
        }

        var navmesh = NavmeshIPC.Instance;
        var passOver = new Dictionary<ulong, long>();
        var choices = new List<SackSpot>();
        SackPlan? plan = null;
        Vector2? escape = null;
        var picked = 0;
        var points = 0;
        var speed = SackRunSpeed;
        var lastAt = SackField.Flat(DungeonField.PlayerPosition);
        var lastAtMs = Environment.TickCount64;
        var nearestSoFar = float.MaxValue;
        var closerAtMs = lastAtMs;
        var onTopSinceMs = 0L;
        // The sacks stood on, and when, until their point came or the window for it passed.
        var stoodOn = new Dictionary<ulong, long>();
        ulong lastStood = 0;
        var sprintTriedAtMs = 0L;
        var startedAtMs = Environment.TickCount64;
        // The sacks the count already rose for; their objects may linger a moment and are not walked to again.
        var credited = new HashSet<ulong>();
        var scoreRead = DutyListRound.TryReadSackScore(out var score);
        capture.Note(scoreRead ? $"score {score} at the start" : "the duty list shows no sack count; pickups are read from the sacks going");
        // Whether the sack the plan makes for showed when it was chosen.
        var planShown = true;
        var roomDescribed = false;
        var deadline = Environment.TickCount64 + BonusRoundCapMs;
        try
        {
            while (!CancelToken.IsCancellationRequested && Environment.TickCount64 < deadline && context.InDungeon && !context.Zoning && !IsKnockedOut() && BonusRoundOn(dungeon))
            {
                var now = Environment.TickCount64;
                field.Read(now);
                var at = SackField.Flat(DungeonField.PlayerPosition);
                if (now > lastAtMs)
                {
                    var moved = Vector2.Distance(at, lastAt) / ((now - lastAtMs) / 1000f);
                    if (moved > SackSpeedFloor)
                    {
                        speed = Math.Clamp(speed + (moved - speed) * SackSpeedSmoothing, SackSpeedFloor, SackSpeedCeiling);
                    }
                }

                lastAt = at;
                lastAtMs = now;
                if (!roomDescribed && !field.AnySackObject && now - startedAtMs >= NoSackDescribeMs)
                {
                    roomDescribed = true;
                    field.DescribeRoom();
                    Warn($"{scope}: no sack object of the profile's ids showed in {NoSackDescribeMs / 1000}s; the room's objects are written to the recording.");
                }

                capture.Record(now, field, speed);

                // The count is the party's: a rise while standing on the planned sack is that sack's, any other is a
                // partner's. Without the count, a planned sack that goes counts as picked up.
                if (DutyListRound.TryReadSackScore(out var nowScore))
                {
                    if (!scoreRead || nowScore != score)
                    {
                        // The count is the party's. A rise while the character stands on its sack, or just after, is that
                        // sack's, unless a partner stands on a sack too and the rise does not tell the two apart; then
                        // neither is taken as picked up, and the sack is checked by its object instead.
                        var rise = scoreRead ? nowScore - score : 0;
                        var standingOn = onTopSinceMs != 0 && plan is { } current ? current.FirstId : 0UL;
                        var candidate = standingOn != 0 ? standingOn
                            : lastStood != 0 && stoodOn.TryGetValue(lastStood, out var stoodAt) && now - stoodAt <= SackPointLateMs ? lastStood : 0UL;
                        var worth = candidate == 0 ? 0 : SackWorth(candidate, field, choices);
                        var partnerWorth = PartnerOnSack(field, at);
                        var mine = rise > 0 && candidate != 0 && (partnerWorth == 0 || rise == worth + partnerWorth || rise != partnerWorth && rise == worth);
                        var reason = rise <= 0 ? string.Empty
                            : mine ? $", for {candidate:X}{(standingOn != 0 ? $" {now - onTopSinceMs} ms after stepping on it" : " just after the stand")}"
                            : candidate != 0 ? $", unclear: a partner stands on a sack worth {partnerWorth}"
                            : ", a partner's";
                        capture.Note($"score {nowScore} (+{rise}){reason}");
                        if (mine)
                        {
                            credited.Add(candidate);
                            stoodOn[candidate] = now;
                            lastStood = 0;
                            picked++;
                            points += worth;
                            if (standingOn != 0)
                            {
                                // The point is in, so the walk goes on at once.
                                plan = null;
                                onTopSinceMs = 0;
                            }
                        }

                        score = nowScore;
                        scoreRead = true;
                    }
                }
                else if (!scoreRead && plan is { } held && SackField.Taken(held.FirstId, held.First, planShown))
                {
                    var worth = SackWorth(held.FirstId, field, choices);
                    picked++;
                    points += worth;
                    capture.Note($"picked {held.FirstId:X} for {worth}, {points} points so far");
                    plan = null;
                }

                // A planned sack gone before the count rose, a wave's end or a partner's pickup, is let go.
                if (plan is { } vanished && scoreRead && SackField.Taken(vanished.FirstId, vanished.First, planShown))
                {
                    capture.Note($"{vanished.FirstId:X} went before the count rose for it");
                    plan = null;
                    onTopSinceMs = 0;
                }

                if (now - sprintTriedAtMs >= SprintRetryMs)
                {
                    sprintTriedAtMs = now;
                    TrySprint();
                }

                choices.Clear();
                foreach (var sack in field.Sacks)
                {
                    // A credited sack is never walked to again; one stood on without a clear point is, once its object
                    // outlasts the time a picked-up sack lingers.
                    var justStood = stoodOn.TryGetValue(sack.Id, out var stood) && now - stood <= SackCreditWindowMs;
                    if (!justStood && stood != 0 && !credited.Contains(sack.Id))
                    {
                        capture.Note($"{sack.Id:X} still there {now - stood} ms after the stand; it was not picked up");
                        stoodOn.Remove(sack.Id);
                    }

                    if (!credited.Contains(sack.Id) && !justStood && (!passOver.TryGetValue(sack.Id, out var until) || now >= until))
                    {
                        choices.Add(sack);
                    }
                }

                // In a party, each member takes the sacks nearer to it than to the others, so they do not run for the
                // same one; with none of its own left it takes any.
                KeepOwnSacks(at, choices);

                // Every plan the planner gives keeps the character out of each lane while its charge lands, on the way,
                // on the sack and stepping off it. A plan under way that a new charge would catch is dropped at once,
                // a stand on its sack included; with no safe plan, a character standing in a lane that is due walks to
                // the nearest clear spot, and otherwise waits where it is.
                var walkingThere = onTopSinceMs == 0;
                var standLeft = walkingThere ? SackPlanner.StandSeconds : Math.Max(0f, SackPlanner.StandSeconds - (now - onTopSinceMs) / 1000f);
                if (plan is { } kept && PlanScore(kept, at, speed, field, choices, standLeft) is null)
                {
                    capture.Note($"a charge would catch the plan for {kept.FirstId:X}; dropping it{(walkingThere ? string.Empty : " and stepping off")}");
                    plan = null;
                    onTopSinceMs = 0;
                    navmesh.Stop();
                }

                var best = SackPlanner.Choose(at, speed, choices, field.Lanes);
                var danger = SackPlanner.Danger(at, speed, field.Lanes);
                if (best is null && plan is null && danger is { } due)
                {
                    // Worked out afresh once the spot under way is covered too, since a charger that appears on the way
                    // can close it.
                    if ((escape is not { } going || !SackPlanner.Clear(going, field.Lanes))
                        && SackPlanner.Escape(at, speed, field.Lanes, RoomMin(field, at), RoomMax(field, at)) is { } spot)
                    {
                        escape = spot;
                        capture.Note($"in a lane landing in {due.ImpactIn:F1}s, {due.MetersToLeave(at):F1}m from its edge; escape to {SackRoundCapture.Format(spot)}");
                        MoveStraight(navmesh, [spot]);
                    }
                }
                else
                {
                    if (best is null && plan is null && escape is not null)
                    {
                        escape = null;
                        navmesh.Stop();
                    }

                    if (best is { } chosen && (plan is not { } current || onTopSinceMs == 0 && Replaces(chosen, current, at, speed, field, choices)))
                    {
                        escape = null;
                        plan = chosen;
                        planShown = Find(choices, chosen.FirstId)?.Shown ?? true;
                        nearestSoFar = float.MaxValue;
                        onTopSinceMs = 0;
                        capture.Note($"plan {chosen.FirstId:X} at {SackRoundCapture.Format(chosen.First)}{(chosen.HasSecond ? $" then {chosen.SecondId:X}" : string.Empty)}, score {chosen.Score:F2}, {field.Lanes.Count} lanes");
                        MoveStraight(navmesh, [chosen.First]);
                    }
                    else if (plan is null && escape is null)
                    {
                        navmesh.Stop();
                    }
                }

                if (escape is { } goingTo)
                {
                    if (Vector2.Distance(at, goingTo) <= EscapeArriveMeters)
                    {
                        navmesh.Stop();
                    }
                }
                else if (plan is { } walking)
                {
                    var distance = Vector2.Distance(at, walking.First);
                    var onTop = distance <= SackOnTopMeters || distance <= SackStoppedOnMeters && !navmesh.IsRunning();
                    if (onTop && onTopSinceMs == 0)
                    {
                        onTopSinceMs = now;
                        navmesh.Stop();
                        capture.Note($"on {walking.FirstId:X}, {distance:F2}m; standing {SackStandMs} ms");
                    }
                    else if (onTopSinceMs == 0 && !navmesh.IsRunning())
                    {
                        MoveStraight(navmesh, [walking.First]);
                    }

                    if (onTopSinceMs != 0 && now - onTopSinceMs >= SackStandMs)
                    {
                        stoodOn[walking.FirstId] = now;
                        lastStood = walking.FirstId;
                        capture.Note($"stood on {walking.FirstId:X} {now - onTopSinceMs} ms, {Vector2.Distance(at, walking.First):F2}m; going on");
                        plan = null;
                        onTopSinceMs = 0;
                    }

                    if (plan is not null && onTopSinceMs == 0 && distance < nearestSoFar - SackStallMeters)
                    {
                        nearestSoFar = distance;
                        closerAtMs = now;
                    }
                    else if (plan is not null && onTopSinceMs == 0 && now - closerAtMs >= SackStallMs && distance > SackStoppedOnMeters)
                    {
                        capture.Note($"stalled {distance:F1}m from {walking.FirstId:X}; passing it over");
                        passOver[walking.FirstId] = now + SackPassOverMs;
                        plan = null;
                    }

                }

                await NextFrame(SackTickFrames);
            }
        }
        finally
        {
            navmesh.Stop();
        }

        capture.Note($"over: {picked} sacks, {points} points");
        Diag($"{scope}: the bonus round is over; {picked} sack(s) picked up for {points} point(s).");
    }

    private static readonly List<Vector2> partnerSpots = [];

    // The points of the sack a partner stands on, or 0.
    private static int PartnerOnSack(SackField field, Vector2 at)
    {
        var party = Svc.Party;
        var territoryId = Svc.ClientState.TerritoryType;
        for (var index = 0; index < party.Length; index++)
        {
            if (party[index] is not { } member || Game.Player.PartyPresence.IsLocal(member) || member.Territory.RowId != territoryId)
            {
                continue;
            }

            var spot = SackField.Flat(member.Position);
            foreach (var sack in field.Sacks)
            {
                if (Vector2.Distance(spot, sack.At) <= PartnerOnSackMeters && Vector2.Distance(at, sack.At) > PartnerOnSackMeters)
                {
                    return sack.Points;
                }
            }
        }

        return 0;
    }
    private static readonly List<SackSpot> ownSacks = [];

    private static void KeepOwnSacks(Vector2 at, List<SackSpot> choices)
    {
        partnerSpots.Clear();
        var party = Svc.Party;
        var territoryId = Svc.ClientState.TerritoryType;
        for (var index = 0; index < party.Length; index++)
        {
            if (party[index] is { } member && !Game.Player.PartyPresence.IsLocal(member) && member.Territory.RowId == territoryId)
            {
                partnerSpots.Add(SackField.Flat(member.Position));
            }
        }

        if (partnerSpots.Count == 0)
        {
            return;
        }

        ownSacks.Clear();
        foreach (var sack in choices)
        {
            var mine = Vector2.DistanceSquared(at, sack.At);
            var nearest = true;
            foreach (var partner in partnerSpots)
            {
                if (Vector2.DistanceSquared(partner, sack.At) < mine)
                {
                    nearest = false;
                    break;
                }
            }

            if (nearest)
            {
                ownSacks.Add(sack);
            }
        }

        if (ownSacks.Count > 0)
        {
            choices.Clear();
            choices.AddRange(ownSacks);
        }
    }

    // The plan under way is kept unless its first sack is gone or the new one is clearly better.
    private static bool Replaces(SackPlan chosen, SackPlan current, Vector2 at, float speed, SackField field, List<SackSpot> choices)
    {
        if (chosen.FirstId == current.FirstId && chosen.SecondId == current.SecondId)
        {
            return false;
        }

        return PlanScore(current, at, speed, field, choices, SackPlanner.StandSeconds) is not { } score || chosen.Score > score * SackRetargetGain;
    }

    // The score of a plan as it stands now; null when its sack is gone or a charge would catch it.
    private static float? PlanScore(SackPlan plan, Vector2 at, float speed, SackField field, List<SackSpot> choices, float standLeft)
    {
        var first = Find(choices, plan.FirstId) ?? Find(field.Sacks, plan.FirstId);
        if (first is null)
        {
            return null;
        }

        var second = plan.HasSecond ? Find(choices, plan.SecondId) : null;
        return SackPlanner.Rescore(at, speed, first.Value, second, standLeft, field.Lanes);
    }

    private static SackSpot? Find(List<SackSpot> sacks, ulong id)
    {
        foreach (var sack in sacks)
        {
            if (sack.Id == id)
            {
                return sack;
            }
        }

        return null;
    }

    // The worth of a sack just picked up, from the last read that still showed it where it was.
    private static int SackWorth(ulong id, SackField field, List<SackSpot> lastChoices)
        => Find(lastChoices, id)?.Points ?? Find(field.Sacks, id)?.Points ?? 1;

    // The room is flat and open, so the walk goes straight at the sacks with no path search, at the character's height.
    private static void MoveStraight(NavmeshIPC navmesh, Vector2[] points)
    {
        var height = DungeonField.PlayerPosition.Y;
        var waypoints = new List<Vector3>(points.Length);
        foreach (var point in points)
        {
            waypoints.Add(new Vector3(point.X, height, point.Y));
        }

        navmesh.MoveAlong(waypoints, fly: false);
    }

    // The room from the chargers' spawn points on its edges and the sacks inside, a metre in from the edge; before any is
    // seen, a stretch around the character.
    private const float RoomInsetMeters = 1f;

    private static Vector2 RoomMin(SackField field, Vector2 at)
        => field.RoomSeen ? field.RoomMin + new Vector2(RoomInsetMeters) : at - new Vector2(10f);

    private static Vector2 RoomMax(SackField field, Vector2 at)
        => field.RoomSeen ? field.RoomMax - new Vector2(RoomInsetMeters) : at + new Vector2(10f);

    private static unsafe void TrySprint()
    {
        var actions = ActionManager.Instance();
        if (actions is not null && actions->GetActionStatus(ActionType.GeneralAction, SprintGeneralActionId) == 0)
        {
            actions->UseAction(ActionType.GeneralAction, SprintGeneralActionId);
        }
    }
}
