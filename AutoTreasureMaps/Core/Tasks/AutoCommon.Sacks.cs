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
    // A sack cannot be interacted with; it is picked up as soon as the character steps on it. The walk goes onto the
    // sack's centre and on to the next without stopping. A sack's object lingers for a few seconds after its pickup, so
    // a sack just reached is not walked to again while the point for it may still come; once the duty list's count rose
    // for it, never again.
    private const float SackOnTopMeters = 0.25f;
    // The pathfinder may end a walk a little short of the centre; that close and stopped counts as on it.
    private const float SackStoppedOnMeters = 0.5f;
    // A sack's object lingers 2.5 to 3.2 s after its pickup, so one reached is left alone this long; still there after
    // it, it was not picked up and is walked to again.
    private const int SackCreditWindowMs = 3_500;
    // The point comes 0.03 to 0.15 s after the character steps on its sack; a rise this soon after is its.
    private const int SackPointLateMs = 500;
    // A partner this close to a sack may be picking it up as well.
    private const float PartnerOnSackMeters = 0.8f;
    // A partner this close to a sack has stepped on it; the sack is left to it while its object lingers.
    private const float PartnerTookMeters = 0.6f;
    // Within this of each other, a partner and the character are equally near a sack, and it goes by the split below.
    private const float SackTieMeters = 1.5f;
    // A partner and the character this close split the room at the line north to south halfway between them: the one
    // with the lower content id takes the west side. Both work this out alike without talking, and a partner's position
    // read a moment late moves the line by only half the lag. Nearest-takes-it would leave both, each seeing the other a
    // step behind, making for the same sack; the 91-point round of 2026-10-24 ran 50 s like that.
    private const float SackSplitMeters = 6f;
    // A plan this close to its sack is kept even once the sack counts as a partner's; the character gets there first.
    private const float SackKeepNearMeters = 2f;
    private const float EscapeArriveMeters = 0.5f;
    // The sacks come a few seconds into the round; none by this time means the profile's ids are not the sacks'.
    private const int NoSackDescribeMs = 10_000;

    // The bonus round scatters sacks over the room for ninety seconds, each picked up by stepping on it, and every ten
    // seconds chargers rush across it. The walk goes straight from sack to sack, counts its points from the duty list,
    // steps out of a lane before its charge lands, and writes the round down for later.
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
        // The sacks stood on, and when, until their point came or the window for it passed.
        var stoodOn = new Dictionary<ulong, long>();
        // The sacks a partner stepped on, and when; left to it while a picked-up sack's object lingers.
        var partnerTook = new Dictionary<ulong, long>();
        var self = Svc.PlayerState.ContentId;
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
        // Known by when it began, so a walk taken up again after a pause keeps one line for the round.
        var roundBeganAtMs = Messages.TryGetLatest(DungeonLogMessages.BonusRoundStarted, dungeon.RoundStartedAtMs, out var began) ? began.AtMs : Environment.TickCount64;
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

                ReadPartners();
                capture.Record(now, field, speed, partners);
                NotePartnersOnSacks(field, at, now, partnerTook, capture);

                // Without the duty list's count, a planned sack that goes counts as picked up.
                if (DutyListRound.TryReadSackScore(out var nowScore))
                {
                    if (!scoreRead || nowScore != score)
                    {
                        // The count is the party's. A rise just after the character reached its sack is that sack's,
                        // unless a partner stands on a sack too and the rise does not tell the two apart; then neither
                        // is taken as picked up, and the sack is checked by its object instead.
                        var rise = scoreRead ? nowScore - score : 0;
                        var candidate = lastStood != 0 && stoodOn.TryGetValue(lastStood, out var stoodAt) && now - stoodAt <= SackPointLateMs ? lastStood : 0UL;
                        // The rise read in the same tick the character reached its sack, before the walk below notes it.
                        if (candidate == 0 && plan is { } reaching && !credited.Contains(reaching.FirstId) && Vector2.Distance(at, reaching.First) <= SackStoppedOnMeters)
                        {
                            candidate = reaching.FirstId;
                            stoodOn[candidate] = now;
                        }

                        var worth = candidate == 0 ? 0 : SackWorth(candidate, field, choices);
                        var partnerWorth = PartnerOnSack(field, at);
                        var mine = rise > 0 && candidate != 0 && (partnerWorth == 0 || rise == worth + partnerWorth || rise != partnerWorth && rise == worth);
                        var reason = rise <= 0 ? string.Empty
                            : mine ? $", for {candidate:X} reached {now - stoodOn[candidate]} ms ago"
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
                        capture.Note($"{sack.Id:X} still there {now - stood} ms after reaching it; it was not picked up");
                        stoodOn.Remove(sack.Id);
                    }

                    if (!credited.Contains(sack.Id) && !justStood && !TookByPartner(partnerTook, sack.Id, now) && (!passOver.TryGetValue(sack.Id, out var until) || now >= until))
                    {
                        choices.Add(sack);
                    }
                }

                // In a party, each member takes its own sacks (PartnersSack), so they do not run for the same one; with
                // none of its own left it takes those the others are least near.
                var ownLeft = KeepOwnSacks(at, self, choices);

                // A plan for a sack a partner stepped on is let go, and one that turned the partner's while the character
                // has its own to go to, unless the character is about to step on it.
                if (plan is { } claimed
                    && (TookByPartner(partnerTook, claimed.FirstId, now)
                        || ownLeft && Vector2.Distance(at, claimed.First) > SackKeepNearMeters && Find(field.Sacks, claimed.FirstId) is { } aimed && PartnersSack(aimed, at, self)))
                {
                    capture.Note($"{claimed.FirstId:X} is a partner's; dropping it");
                    plan = null;
                    navmesh.Stop();
                }

                // Every plan the planner gives keeps the character out of each lane while its charge lands, on the way,
                // on the sack and moving on from it. A plan under way that a new charge would catch is dropped at once;
                // with no safe plan, a character standing in a lane that is due walks to the nearest clear spot, and
                // otherwise waits where it is.
                if (plan is { } kept && PlanScore(kept, at, speed, field, choices) is null)
                {
                    capture.Note($"a charge would catch the plan for {kept.FirstId:X}; dropping it");
                    plan = null;
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
                        capture.Note($"in a lane landing in {due.ImpactIn:F1}s, {SackPlanner.MetersToClear(at, field.Lanes):F1}m from clear ground; escape to {SackRoundCapture.Format(spot)}");
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

                    if (best is { } chosen && (plan is not { } current || Replaces(chosen, current, at, speed, field, choices)))
                    {
                        escape = null;
                        plan = chosen;
                        planShown = Find(choices, chosen.FirstId)?.Shown ?? true;
                        nearestSoFar = float.MaxValue;
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
                    if (onTop)
                    {
                        // Picked up on contact, so the next plan is made on the next tick and the walk does not stop. A sack
                        // the count already rose for in this tick is not waited on for a second point.
                        stoodOn[walking.FirstId] = now;
                        lastStood = credited.Contains(walking.FirstId) ? 0 : walking.FirstId;
                        capture.Note($"on {walking.FirstId:X}, {distance:F2}m; going on");
                        plan = null;
                    }
                    else if (!navmesh.IsRunning())
                    {
                        MoveStraight(navmesh, [walking.First]);
                    }

                    if (plan is not null && distance < nearestSoFar - SackStallMeters)
                    {
                        nearestSoFar = distance;
                        closerAtMs = now;
                    }
                    else if (plan is not null && now - closerAtMs >= SackStallMs && distance > SackStoppedOnMeters)
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

        // The walk ends on the game's word that the round is over, which can come in the same tick as the last pickup, so
        // the count is read once more, and taken from that word too where it names the sacks gathered.
        var final = FinalSackScore(roundBeganAtMs, scoreRead ? score : 0);
        if (final != score)
        {
            capture.Note($"final count {final}");
        }

        capture.Note($"over: {picked} sacks, {points} points");
        // The map log keeps the party's count, which the round is won by; without any count, this character's own.
        dungeon.NoteSpecialRound(roundBeganAtMs, new SpecialRound(SpecialRoundKind.SackRound, final > 0 ? final : points));
        Diag($"{scope}: the bonus round is over; {picked} sack(s) picked up for {points} point(s).");
    }

    // The round asks for a hundred; a parameter above this is not a sack count.
    private const int MaxSackScore = 200;

    // The highest of the count last read, the duty list now, and the integer parameters of the round's closing message
    // ("got 95 glowing sacks, a silver coffer appeared").
    private static int FinalSackScore(long roundBeganAtMs, int lastRead)
    {
        var final = lastRead;
        if (DutyListRound.TryReadSackScore(out var now))
        {
            final = Math.Max(final, now);
        }

        if (Messages.TryGetLatest(DungeonLogMessages.BonusRoundCoffer, roundBeganAtMs, out var closing))
        {
            for (var index = 0; index < TreasureMessage.MaxParameters; index++)
            {
                if (closing.TryGetParameter(index, out var value) && value <= MaxSackScore)
                {
                    final = Math.Max(final, value);
                }
            }
        }

        return final;
    }

    // The other members in the room: where the object table shows them, and where the party list does, which can lag.
    private static readonly List<SackPartner> partners = [];

    private static void ReadPartners()
    {
        partners.Clear();
        var party = Svc.Party;
        var territoryId = Svc.ClientState.TerritoryType;
        for (var index = 0; index < party.Length; index++)
        {
            if (party[index] is not { } member || Game.Player.PartyPresence.IsLocal(member) || member.Territory.RowId != territoryId)
            {
                continue;
            }

            var listed = SackField.Flat(member.Position);
            var at = member.GameObject is { } body ? SackField.Flat(body.Position) : listed;
            partners.Add(new SackPartner((ulong)member.ContentId, member.Name.TextValue, at, listed));
        }
    }

    // The points of the sack a partner stands on, or 0.
    private static int PartnerOnSack(SackField field, Vector2 at)
    {
        foreach (var partner in partners)
        {
            foreach (var sack in field.Sacks)
            {
                if (Vector2.Distance(partner.At, sack.At) <= PartnerOnSackMeters && Vector2.Distance(at, sack.At) > PartnerOnSackMeters)
                {
                    return sack.Points;
                }
            }
        }

        return 0;
    }

    // A sack a partner steps on is its; its object lingers a few seconds, and walking to it meanwhile follows the partner.
    private static void NotePartnersOnSacks(SackField field, Vector2 at, long now, Dictionary<ulong, long> partnerTook, SackRoundCapture capture)
    {
        foreach (var partner in partners)
        {
            foreach (var sack in field.Sacks)
            {
                if (Vector2.Distance(partner.At, sack.At) > PartnerTookMeters || Vector2.Distance(at, sack.At) <= PartnerTookMeters)
                {
                    continue;
                }

                if (!partnerTook.TryGetValue(sack.Id, out var took) || now - took > SackCreditWindowMs)
                {
                    capture.Note($"{partner.Name} stepped on {sack.Id:X}; leaving it");
                }

                partnerTook[sack.Id] = now;
            }
        }
    }

    // Whether the sack is a partner's: on its side of the split while the two are close, otherwise clearly nearer to it or
    // about as near and on its side.
    private static bool PartnersSack(SackSpot sack, Vector2 at, ulong self)
    {
        var mine = Vector2.Distance(at, sack.At);
        foreach (var partner in partners)
        {
            var theirs = Vector2.Distance(partner.At, sack.At);
            var theirSide = sack.At.X < (at.X + partner.At.X) / 2 != self < partner.ContentId;
            var toPartner = Vector2.Distance(at, partner.At) < SackSplitMeters
                ? theirSide
                : theirs < mine - SackTieMeters || theirs <= mine + SackTieMeters && theirSide;
            if (toPartner)
            {
                return true;
            }
        }

        return false;
    }

    private static readonly List<SackSpot> ownSacks = [];

    // Narrows the choices to the character's own sacks; false when it has none and is left the others' least near.
    private static bool KeepOwnSacks(Vector2 at, ulong self, List<SackSpot> choices)
    {
        if (partners.Count == 0 || choices.Count == 0)
        {
            return true;
        }

        ownSacks.Clear();
        foreach (var sack in choices)
        {
            if (!PartnersSack(sack, at, self))
            {
                ownSacks.Add(sack);
            }
        }

        var own = ownSacks.Count > 0;
        if (!own)
        {
            // None of its own: the sacks the others are least near, by how much farther they are than the character.
            var most = float.MinValue;
            foreach (var sack in choices)
            {
                most = Math.Max(most, Lead(sack, at));
            }

            foreach (var sack in choices)
            {
                if (Lead(sack, at) >= most - SackTieMeters)
                {
                    ownSacks.Add(sack);
                }
            }
        }

        choices.Clear();
        choices.AddRange(ownSacks);
        return own;
    }

    private static bool TookByPartner(Dictionary<ulong, long> partnerTook, ulong id, long now)
        => partnerTook.TryGetValue(id, out var took) && now - took <= SackCreditWindowMs;

    // How much farther the nearest partner is from the sack than the character.
    private static float Lead(SackSpot sack, Vector2 at)
    {
        var nearest = float.MaxValue;
        foreach (var partner in partners)
        {
            nearest = Math.Min(nearest, Vector2.Distance(partner.At, sack.At));
        }

        return nearest - Vector2.Distance(at, sack.At);
    }

    // The plan under way is kept unless its first sack is gone or the new one is clearly better.
    private static bool Replaces(SackPlan chosen, SackPlan current, Vector2 at, float speed, SackField field, List<SackSpot> choices)
    {
        if (chosen.FirstId == current.FirstId && chosen.SecondId == current.SecondId)
        {
            return false;
        }

        return PlanScore(current, at, speed, field, choices) is not { } score || chosen.Score > score * SackRetargetGain;
    }

    // The score of a plan as it stands now; null when its sack is gone or a charge would catch it.
    private static float? PlanScore(SackPlan plan, Vector2 at, float speed, SackField field, List<SackSpot> choices)
    {
        var first = Find(choices, plan.FirstId) ?? Find(field.Sacks, plan.FirstId);
        if (first is null)
        {
            return null;
        }

        var second = plan.HasSecond ? Find(choices, plan.SecondId) : null;
        return SackPlanner.Rescore(at, speed, first.Value, second, SackPlanner.StandSeconds, field.Lanes);
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
