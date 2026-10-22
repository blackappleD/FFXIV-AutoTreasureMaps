using System.Numerics;

namespace AutoTreasureMaps.Core.Dungeon;

// A sack on the floor, on the ground plane (x, z), with the points it is worth.
// Shown is whether the game draws it or lets it be targeted, kept so a sack that stops showing counts as picked up.
internal readonly record struct SackSpot(ulong Id, Vector2 At, int Points, bool Shown = true);

// A charge that is due: a strip from where the charger stands along the way it faces, landing in ImpactIn seconds.
internal readonly record struct ChargeLane(Vector2 From, Vector2 Direction, float ImpactIn)
{
    // The combat plugin's module draws the charge as an 8m wide rectangle from the charger on.
    public const float HalfWidth = 4f;
    // Longer than the room's diagonal, so a corner charge reaches the far corner.
    private const float Length = 60f;
    // The character's own width, and the charger's facing read a little off.
    private const float Margin = 1f;

    public bool Covers(Vector2 point)
    {
        var offset = point - From;
        var along = Vector2.Dot(offset, Direction);
        var across = MathF.Abs(offset.X * Direction.Y - offset.Y * Direction.X);
        return along >= -Margin && along <= Length && across <= HalfWidth + Margin;
    }

    // How far the character must walk across the lane to stand clear of it.
    public float MetersToLeave(Vector2 point)
    {
        var offset = point - From;
        var across = MathF.Abs(offset.X * Direction.Y - offset.Y * Direction.X);
        return MathF.Max(0f, HalfWidth + Margin - across);
    }
}

// The plan the bonus round walks: the sack to make for and the one after it, so the walk does not stop between them.
internal readonly record struct SackPlan(ulong FirstId, Vector2 First, ulong SecondId, Vector2 Second, float Score, bool Risky)
{
    public bool HasSecond => SecondId != 0;
}

// Picks the bonus round's next sacks. Each pair of sacks is weighed by the points it brings per second of walking, the
// second counted for less since the field changes before it is reached. A plan that has the character in a lane when
// its charge lands, on the way, while it stands on a sack, or while it steps off the last one, is never taken: being
// caught leaves it Heavy for five seconds, which costs more than any sack is worth. Pure arithmetic over what the caller
// read from the field, so a recording can be played back through it.
internal static class SackPlanner
{
    private const float SecondSackWeight = 0.6f;
    // Turning onto the next sack and the movement plugin picking the new waypoint up.
    private const float TurnSeconds = 0.15f;
    // How long the character stands on a sack to pick it up.
    public const float StandSeconds = 0.3f;
    // After the last sack of a plan the next way is not known yet; this long there is counted as still standing.
    private const float AfterLastSeconds = 1.2f;
    // The charge is checked against where the character stands this long either side of its landing. Read from the
    // logs: the hit lands about 0.3 s after the cast bar ends, which is when the lane's countdown reaches zero.
    private const float ImpactEarlySeconds = 0.6f;
    private const float ImpactLateSeconds = 0.6f;
    private const float SampleSeconds = 0.05f;
    // A spot in a lane is left once its charge lands within the time to walk out of it and this much more.
    private const float ClearAheadSeconds = 1.2f;
    private const float EscapeGridMeters = 1f;

    public static SackPlan? Choose(Vector2 from, float speed, IReadOnlyList<SackSpot> sacks, IReadOnlyList<ChargeLane> lanes)
    {
        SackPlan? best = null;
        for (var first = 0; first < sacks.Count; first++)
        {
            var one = sacks[first];
            var toFirst = Vector2.Distance(from, one.At) / speed + TurnSeconds;
            if (Caught(from, one.At, 0f, toFirst, toFirst + StandSeconds, lanes))
            {
                continue;
            }

            var leaveFirst = toFirst + StandSeconds;
            if (!Caught(one.At, one.At, leaveFirst, leaveFirst, leaveFirst + AfterLastSeconds, lanes))
            {
                best = Better(best, new SackPlan(one.Id, one.At, 0, default, one.Points / leaveFirst, false));
            }

            for (var second = 0; second < sacks.Count; second++)
            {
                if (second == first)
                {
                    continue;
                }

                var two = sacks[second];
                var toSecond = Vector2.Distance(one.At, two.At) / speed + TurnSeconds;
                var atSecond = leaveFirst + toSecond;
                if (Caught(one.At, two.At, leaveFirst, atSecond, atSecond + StandSeconds + AfterLastSeconds, lanes))
                {
                    continue;
                }

                var points = one.Points + SecondSackWeight * two.Points;
                var seconds = leaveFirst + SecondSackWeight * (toSecond + StandSeconds);
                best = Better(best, new SackPlan(one.Id, one.At, two.Id, two.At, points / seconds, false));
            }
        }

        return best;
    }

    // The plan's score as it stands now, or null once a charge would catch it, so the walk drops it at once.
    public static float? Rescore(Vector2 from, float speed, SackSpot first, SackSpot? second, float standLeft, IReadOnlyList<ChargeLane> lanes)
    {
        var toFirst = Vector2.Distance(from, first.At) / speed + (standLeft < StandSeconds ? 0f : TurnSeconds);
        var leaveFirst = toFirst + standLeft;
        if (second is not { } two)
        {
            return Caught(from, first.At, 0f, toFirst, leaveFirst + AfterLastSeconds, lanes) ? null : first.Points / Math.Max(leaveFirst, 0.05f);
        }

        if (Caught(from, first.At, 0f, toFirst, leaveFirst, lanes))
        {
            return null;
        }

        var toSecond = Vector2.Distance(first.At, two.At) / speed + TurnSeconds;
        var atSecond = leaveFirst + toSecond;
        if (Caught(first.At, two.At, leaveFirst, atSecond, atSecond + StandSeconds + AfterLastSeconds, lanes))
        {
            return null;
        }

        return (first.Points + SecondSackWeight * two.Points) / Math.Max(leaveFirst + SecondSackWeight * (toSecond + StandSeconds), 0.05f);
    }

    // The lane the character stands in whose charge lands before it could walk out with time to spare, or null.
    public static ChargeLane? Danger(Vector2 at, float speed, IReadOnlyList<ChargeLane> lanes)
    {
        ChargeLane? soonest = null;
        for (var index = 0; index < lanes.Count; index++)
        {
            var lane = lanes[index];
            if (!lane.Covers(at))
            {
                continue;
            }

            if (lane.ImpactIn - ImpactEarlySeconds <= lane.MetersToLeave(at) / speed + ClearAheadSeconds
                && (soonest is not { } kept || lane.ImpactIn < kept.ImpactIn))
            {
                soonest = lane;
            }
        }

        return soonest;
    }

    // Whether a spot is clear of every charge that is due.
    public static bool Clear(Vector2 at, IReadOnlyList<ChargeLane> lanes)
    {
        for (var index = 0; index < lanes.Count; index++)
        {
            if (lanes[index].Covers(at))
            {
                return false;
            }
        }

        return true;
    }

    // The nearest spot of the room, on a one-metre grid, that no due charge covers and that the character reaches
    // without being caught on the way. When every spot is covered, the one whose charge lands last. Null only with no
    // room known.
    public static Vector2? Escape(Vector2 from, float speed, IReadOnlyList<ChargeLane> lanes, Vector2 roomMin, Vector2 roomMax)
    {
        Vector2? best = null;
        var bestDistance = float.MaxValue;
        Vector2? leastBad = null;
        var leastBadImpact = float.MinValue;
        for (var x = roomMin.X; x <= roomMax.X; x += EscapeGridMeters)
        {
            for (var y = roomMin.Y; y <= roomMax.Y; y += EscapeGridMeters)
            {
                var spot = new Vector2(x, y);
                var distance = Vector2.Distance(from, spot);
                if (Clear(spot, lanes))
                {
                    if (distance < bestDistance && !Caught(from, spot, 0f, distance / speed, distance / speed, lanes))
                    {
                        bestDistance = distance;
                        best = spot;
                    }

                    continue;
                }

                var firstImpact = float.MaxValue;
                for (var index = 0; index < lanes.Count; index++)
                {
                    if (lanes[index].Covers(spot))
                    {
                        firstImpact = MathF.Min(firstImpact, lanes[index].ImpactIn);
                    }
                }

                if (firstImpact > leastBadImpact)
                {
                    leastBadImpact = firstImpact;
                    leastBad = spot;
                }
            }
        }

        return best ?? leastBad;
    }

    // Whether walking a to b between the two times, and standing at b until holdUntil, puts the character in a lane
    // while its charge lands.
    private static bool Caught(Vector2 a, Vector2 b, float startsAt, float arrivesAt, float holdUntil, IReadOnlyList<ChargeLane> lanes)
    {
        for (var index = 0; index < lanes.Count; index++)
        {
            var lane = lanes[index];
            var windowStart = MathF.Max(lane.ImpactIn - ImpactEarlySeconds, startsAt);
            var windowEnd = MathF.Min(lane.ImpactIn + ImpactLateSeconds, holdUntil);
            for (var time = windowStart; time <= windowEnd; time += SampleSeconds)
            {
                var progress = arrivesAt > startsAt ? Math.Clamp((time - startsAt) / (arrivesAt - startsAt), 0f, 1f) : 1f;
                if (lane.Covers(Vector2.Lerp(a, b, progress)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static SackPlan Better(SackPlan? current, SackPlan candidate)
        => current is { } kept && kept.Score >= candidate.Score ? kept : candidate;
}
