using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Ipc;
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
    // Standing on a sack picks it up at once; one still there after this is asked by interacting instead.
    private const float SackOnTopMeters = 0.6f;
    private const int SackOnTopMs = 800;
    private const float EscapeArriveMeters = 0.5f;

    // The bonus round scatters sacks over the room for ninety seconds, each picked up by walking onto it, and every ten
    // seconds chargers rush across it. The walk goes straight from sack to sack with the next one already queued, so it
    // never stops on a sack, steps out of a lane before its charge lands, and writes the round down for later.
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
        Status = "Bonus round: picking up sacks";
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
        var sprintTriedAtMs = 0L;
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
                capture.Record(now, field, speed);

                if (plan is { } held && SackField.Taken(held.FirstId, held.First))
                {
                    var worth = SackWorth(held.FirstId, field, choices);
                    picked++;
                    points += worth;
                    capture.Note($"picked {held.FirstId:X} for {worth}, {points} points so far");
                    plan = null;
                }

                if (now - sprintTriedAtMs >= SprintRetryMs)
                {
                    sprintTriedAtMs = now;
                    TrySprint();
                }

                choices.Clear();
                foreach (var sack in field.Sacks)
                {
                    if (!passOver.TryGetValue(sack.Id, out var until) || now >= until)
                    {
                        choices.Add(sack);
                    }
                }

                // Out of a lane first, when what the walk would do next stands in it as the charge lands.
                var best = SackPlanner.Choose(at, speed, choices, field.Lanes);
                var threatened = SackPlanner.Threatened(at, field.Lanes);
                if (threatened && (best is null || best.Value.Risky))
                {
                    // Worked out afresh every tick, since a charger that appears on the way can close the spot.
                    if (SackPlanner.Escape(at, field.Lanes, RoomMin(field, at), RoomMax(field, at)) is { } spot
                        && (escape is not { } going || Vector2.Distance(going, spot) > EscapeArriveMeters))
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
                        nearestSoFar = float.MaxValue;
                        onTopSinceMs = 0;
                        capture.Note($"plan {chosen.FirstId:X} at {SackRoundCapture.Format(chosen.First)}{(chosen.HasSecond ? $" then {chosen.SecondId:X}" : string.Empty)}, score {chosen.Score:F2}{(chosen.Risky ? ", risky" : string.Empty)}");
                        MoveStraight(navmesh, chosen.HasSecond ? [chosen.First, chosen.Second] : [chosen.First]);
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
                    if (!navmesh.IsRunning())
                    {
                        MoveStraight(navmesh, walking.HasSecond ? [walking.First, walking.Second] : [walking.First]);
                    }

                    if (distance < nearestSoFar - SackStallMeters)
                    {
                        nearestSoFar = distance;
                        closerAtMs = now;
                    }
                    else if (now - closerAtMs >= SackStallMs && distance > SackOnTopMeters)
                    {
                        capture.Note($"stalled {distance:F1}m from {walking.FirstId:X}; passing it over");
                        passOver[walking.FirstId] = now + SackPassOverMs;
                        plan = null;
                    }

                    onTopSinceMs = distance <= SackOnTopMeters ? onTopSinceMs == 0 ? now : onTopSinceMs : 0;
                    if (onTopSinceMs != 0 && now - onTopSinceMs >= SackOnTopMs && Svc.Objects.SearchById(walking.FirstId) is { } still)
                    {
                        capture.Note($"standing on {walking.FirstId:X} did not pick it up; interacting");
                        navmesh.Stop();
                        await InteractWithObject(still, () => SackField.Taken(walking.FirstId, walking.First), scope);
                        if (!SackField.Taken(walking.FirstId, walking.First))
                        {
                            passOver[walking.FirstId] = now + SackPassOverMs;
                            plan = null;
                        }

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
