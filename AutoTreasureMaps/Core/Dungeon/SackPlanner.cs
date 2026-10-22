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
}

// The plan the bonus round walks: the sack to make for and the one after it, so the walk does not stop between them.
internal readonly record struct SackPlan(ulong FirstId, Vector2 First, ulong SecondId, Vector2 Second, float Score, bool Risky)
{
    public bool HasSecond => SecondId != 0;
}

// Picks the bonus round's next sacks. Each pair of sacks is weighed by the points it brings per second of walking, the
// second counted for less since the field changes before it is reached; a pair whose walk stands in a lane when its
// charge lands costs the seconds that being Heavy loses. Pure arithmetic over what the caller read from the field, so a
// recording can be played back through it.
internal static class SackPlanner
{
    // Being caught leaves the character Heavy for five seconds, which walks at about half speed.
    private const float HitCostSeconds = 3f;
    private const float SecondSackWeight = 0.6f;
    // Turning onto the next sack and the movement plugin picking the new waypoint up.
    private const float TurnSeconds = 0.15f;
    // The charge is checked against where the character stands this long either side of its landing.
    private const float ImpactEarlySeconds = 0.6f;
    private const float ImpactLateSeconds = 0.4f;
    private const float SampleSeconds = 0.1f;
    private const int EscapeDirections = 16;
    private const float EscapeStepMeters = 1f;
    private const float EscapeMaxMeters = 12f;

    public static SackPlan? Choose(Vector2 from, float speed, IReadOnlyList<SackSpot> sacks, IReadOnlyList<ChargeLane> lanes)
    {
        SackPlan? best = null;
        for (var first = 0; first < sacks.Count; first++)
        {
            var one = sacks[first];
            var toFirst = Vector2.Distance(from, one.At) / speed + TurnSeconds;
            var firstHit = Caught(from, one.At, 0f, toFirst, lanes, stays: false);
            var alone = new SackPlan(one.Id, one.At, 0, default, one.Points / (toFirst + (firstHit ? HitCostSeconds : 0f)), firstHit);
            best = Better(best, alone);
            for (var second = 0; second < sacks.Count; second++)
            {
                if (second == first)
                {
                    continue;
                }

                var two = sacks[second];
                var toSecond = Vector2.Distance(one.At, two.At) / speed + TurnSeconds;
                var hit = firstHit || Caught(one.At, two.At, toFirst, toFirst + toSecond, lanes, stays: true);
                var points = one.Points + SecondSackWeight * two.Points;
                var seconds = toFirst + SecondSackWeight * toSecond + (hit ? HitCostSeconds : 0f);
                best = Better(best, new SackPlan(one.Id, one.At, two.Id, two.At, points / seconds, hit));
            }
        }

        return best;
    }

    // The plan's score as it stands now, for keeping to a sack already under way unless another is clearly better.
    public static float Rescore(Vector2 from, float speed, SackSpot first, SackSpot? second, IReadOnlyList<ChargeLane> lanes)
    {
        var toFirst = Vector2.Distance(from, first.At) / speed + TurnSeconds;
        var hit = Caught(from, first.At, 0f, toFirst, lanes, stays: second is null);
        if (second is not { } two)
        {
            return first.Points / (toFirst + (hit ? HitCostSeconds : 0f));
        }

        var toSecond = Vector2.Distance(first.At, two.At) / speed + TurnSeconds;
        hit = hit || Caught(first.At, two.At, toFirst, toFirst + toSecond, lanes, stays: true);
        return (first.Points + SecondSackWeight * two.Points) / (toFirst + SecondSackWeight * toSecond + (hit ? HitCostSeconds : 0f));
    }

    // Whether standing here until the charges land gets the character caught.
    public static bool Threatened(Vector2 at, IReadOnlyList<ChargeLane> lanes)
        => Caught(at, at, 0f, 0f, lanes, stays: true);

    // The nearest spot inside the room that no due charge lands on, the quickest way out of the lanes; null when none is
    // within reach.
    public static Vector2? Escape(Vector2 from, IReadOnlyList<ChargeLane> lanes, Vector2 roomMin, Vector2 roomMax)
    {
        for (var distance = EscapeStepMeters; distance <= EscapeMaxMeters; distance += EscapeStepMeters)
        {
            Vector2? found = null;
            var nearestToCentre = float.MaxValue;
            var centre = (roomMin + roomMax) / 2f;
            for (var index = 0; index < EscapeDirections; index++)
            {
                var angle = MathF.Tau * index / EscapeDirections;
                var spot = from + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
                if (spot.X < roomMin.X || spot.Y < roomMin.Y || spot.X > roomMax.X || spot.Y > roomMax.Y || InAnyLane(spot, lanes))
                {
                    continue;
                }

                // Of the spots this far, the one towards the middle keeps the most sacks in reach.
                var toCentre = Vector2.DistanceSquared(spot, centre);
                if (toCentre < nearestToCentre)
                {
                    nearestToCentre = toCentre;
                    found = spot;
                }
            }

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    // Whether walking a to b between the two times (and staying at b after, when stays) puts the character in a lane
    // while its charge lands.
    private static bool Caught(Vector2 a, Vector2 b, float startsAt, float arrivesAt, IReadOnlyList<ChargeLane> lanes, bool stays)
    {
        for (var index = 0; index < lanes.Count; index++)
        {
            var lane = lanes[index];
            var windowStart = MathF.Max(lane.ImpactIn - ImpactEarlySeconds, startsAt);
            var windowEnd = lane.ImpactIn + ImpactLateSeconds;
            if (!stays)
            {
                windowEnd = MathF.Min(windowEnd, arrivesAt);
            }

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

    private static bool InAnyLane(Vector2 spot, IReadOnlyList<ChargeLane> lanes)
    {
        for (var index = 0; index < lanes.Count; index++)
        {
            if (lanes[index].Covers(spot))
            {
                return true;
            }
        }

        return false;
    }

    private static SackPlan Better(SackPlan? current, SackPlan candidate)
        => current is { } kept && kept.Score >= candidate.Score ? kept : candidate;
}
