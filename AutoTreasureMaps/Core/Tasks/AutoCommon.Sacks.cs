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
    // A player picks a sack up by running over it: in the reference video the pickup flash and the point come 0.2 to
    // 0.4 s after the character reaches the sack, without stopping. So the walk runs through each sack and goes on as
    // soon as the duty list's point count rises. The plugin's first round saw no pickup from a stand alone and each sack
    // go about 2 s after an interaction, so a sack whose point has not come this long after contact is asked by
    // interacting, once, without waiting on it; one with no point this long after contact is passed over for a while.
    private const float SackOnTopMeters = 0.6f;
    private const int SackInteractAfterMs = 600;
    private const int SackHoldMs = 4_000;
    // The walk aims this far past a last sack, so it runs over it rather than stopping short.
    private const float SackOvershootMeters = 0.8f;
    // A rise in the count is this sack's while the character stands this close to it.
    private const float SackCreditMeters = 1.5f;
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
        var interactedOnPlan = false;
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
                        var rise = scoreRead ? nowScore - score : 0;
                        var onPlan = plan is { } scored && Vector2.Distance(at, scored.First) <= SackCreditMeters;
                        var standing = onTopSinceMs == 0 ? "not on it" : $"{now - onTopSinceMs} ms on it";
                        capture.Note($"score {nowScore} (+{rise}){(plan is { } p ? $", {Vector2.Distance(at, p.First):F2}m from {p.FirstId:X}, {standing}" : string.Empty)}");
                        if (onPlan && rise > 0 && plan is { } mine)
                        {
                            credited.Add(mine.FirstId);
                            picked++;
                            points += rise;
                            plan = null;
                            onTopSinceMs = 0;
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
                    if (!credited.Contains(sack.Id) && (!passOver.TryGetValue(sack.Id, out var until) || now >= until))
                    {
                        choices.Add(sack);
                    }
                }

                // In a party, each member takes the sacks nearer to it than to the others, so they do not run for the
                // same one; with none of its own left it takes any.
                KeepOwnSacks(at, choices);

                // Out of a lane first, when what the walk would do next stands in it as the charge lands.
                var best = SackPlanner.Choose(at, speed, choices, field.Lanes);
                var threatened = SackPlanner.Threatened(at, field.Lanes);
                if (threatened && (best is null || best.Value.Risky))
                {
                    // Worked out afresh every tick, since a charger that appears on the way can close the spot.
                    // A new spot only once the one under way is threatened itself, so the walk does not dither.
                    if ((escape is not { } going || SackPlanner.Threatened(going, field.Lanes))
                        && SackPlanner.Escape(at, field.Lanes, RoomMin(field, at), RoomMax(field, at)) is { } spot)
                    {
                        escape = spot;
                        plan = null;
                        capture.Note($"escape to {SackRoundCapture.Format(spot)}");
                        MoveStraight(navmesh, [spot]);
                    }
                }
                else
                {
                    escape = null;
                    if (best is { } chosen && (plan is not { } current || Replaces(chosen, current, at, speed, field, choices)))
                    {
                        plan = chosen;
                        planShown = Find(choices, chosen.FirstId)?.Shown ?? true;
                        nearestSoFar = float.MaxValue;
                        onTopSinceMs = 0;
                        interactedOnPlan = false;
                        capture.Note($"plan {chosen.FirstId:X} at {SackRoundCapture.Format(chosen.First)}{(chosen.HasSecond ? $" then {chosen.SecondId:X}" : string.Empty)}, score {chosen.Score:F2}{(chosen.Risky ? ", risky" : string.Empty)}");
                        MoveStraight(navmesh, Through(at, chosen));
                    }
                    else if (plan is null)
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
                    var onTop = distance <= SackOnTopMeters;
                    if (onTop && onTopSinceMs == 0)
                    {
                        onTopSinceMs = now;
                        capture.Note($"on {walking.FirstId:X}, {distance:F2}m, running over it");
                    }
                    else if (onTopSinceMs == 0 && !navmesh.IsRunning())
                    {
                        MoveStraight(navmesh, Through(at, walking));
                    }

                    if (onTopSinceMs != 0 && !interactedOnPlan && now - onTopSinceMs >= SackInteractAfterMs
                        && Svc.Objects.SearchById(walking.FirstId) is { } unpicked)
                    {
                        interactedOnPlan = true;
                        navmesh.Stop();
                        NpcInteraction.Target(unpicked);
                        var result = NpcInteraction.Interact(unpicked);
                        capture.Note($"no point {now - onTopSinceMs} ms after reaching {walking.FirstId:X} ({distance:F2}m now); interacting (result {result})");
                    }

                    // Past the sack the distance grows again, which is no stall.
                    if (onTopSinceMs == 0 && distance < nearestSoFar - SackStallMeters)
                    {
                        nearestSoFar = distance;
                        closerAtMs = now;
                    }
                    else if (onTopSinceMs == 0 && now - closerAtMs >= SackStallMs && distance > SackOnTopMeters)
                    {
                        capture.Note($"stalled {distance:F1}m from {walking.FirstId:X}; passing it over");
                        passOver[walking.FirstId] = now + SackPassOverMs;
                        plan = null;
                    }

                    if (onTopSinceMs != 0 && now - onTopSinceMs >= SackHoldMs)
                    {
                        capture.Note($"no point {SackHoldMs} ms after reaching {walking.FirstId:X}; passing it over");
                        passOver[walking.FirstId] = now + SackPassOverMs;
                        plan = null;
                        onTopSinceMs = 0;
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

    // Through the plan's sack to the next one, or a little past a last one along the way the character comes.
    private static Vector2[] Through(Vector2 at, SackPlan plan)
    {
        if (plan.HasSecond)
        {
            return [plan.First, plan.Second];
        }

        var heading = plan.First - at;
        return heading.LengthSquared() < 0.01f ? [plan.First] : [plan.First, plan.First + Vector2.Normalize(heading) * SackOvershootMeters];
    }

    // The plan under way is kept unless its first sack is gone or the new one is clearly better.
    private static bool Replaces(SackPlan chosen, SackPlan current, Vector2 at, float speed, SackField field, List<SackSpot> choices)
    {
        if (chosen.FirstId == current.FirstId && chosen.SecondId == current.SecondId)
        {
            return false;
        }

        var first = Find(choices, current.FirstId);
        if (first is null)
        {
            return true;
        }

        var second = current.HasSecond ? Find(choices, current.SecondId) : null;
        return chosen.Score > SackPlanner.Rescore(at, speed, first.Value, second, field.Lanes) * SackRetargetGain;
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

    // The sacks have been seen across the room by the time a charge matters; before that, a stretch around the character.
    private static Vector2 RoomMin(SackField field, Vector2 at)
        => field.SackSeen ? field.SacksMin : at - new Vector2(10f);

    private static Vector2 RoomMax(SackField field, Vector2 at)
        => field.SackSeen ? field.SacksMax : at + new Vector2(10f);

    private static unsafe void TrySprint()
    {
        var actions = ActionManager.Instance();
        if (actions is not null && actions->GetActionStatus(ActionType.GeneralAction, SprintGeneralActionId) == 0)
        {
            actions->UseAction(ActionType.GeneralAction, SprintGeneralActionId);
        }
    }
}
