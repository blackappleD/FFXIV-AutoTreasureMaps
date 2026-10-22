using AutoTreasureMaps.Core.Cleanup;
using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Game;
using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Game.Watchers;
using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using System.Collections.Immutable;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

// A party member's run in party mode: the leader runs the maps, and this character goes to each dig spot the leader
// posts, fights beside it and rolls on the loot, until the party or its leader is gone. It leaves the moment a spot is
// posted, so it reaches the spot about when the leader does, and it takes no humanizer waits of its own. When the leader
// takes the party into a treasure dungeon, it accepts, and inside keeps by the leader, fights, rolls and picks up the
// bonus round's sacks; the coffers, gates, lure and slot machine are the leader's.
internal sealed class AutoFollowRun(TreasureSession session, TreasureProgress progress) : AutoCommon
{
    private const int TickFrames = 10;
    // The leader may take a city break or a long trip to the board between spots.
    private const int IdleLimitMs = 20 * TimeUnits.MillisecondsPerMinute;
    // A posted spot is matched to a dig spot of the zone within this reach; the link rounds the point a little.
    private const float SpotMatchMeters = 15f;
    private const string DeclineThrottleKey = "atm-follow-decline";
    private const int DeclineThrottleMs = 1_000;
    // The leader's call into the vault reaches every member in the zone; one is answered only with a portal standing near.
    private const float PortalNearMeters = 80f;
    private const int PortalConfirmDelayMs = 400;
    // Inside, the member keeps within this of the leader, walking back once it is farther.
    private const float KeepByMeters = 6f;
    private const float RejoinBeyondMeters = 12f;
    // A leader this far away in a gate dungeon was carried to the next chamber by a gate's circle; chambers are 185m or
    // more apart, and the leader waits for the members before anything else in a chamber.
    private const float CarriedMeters = 60f;
    private const int DungeonWalkMs = 15_000;
    private const float CircleArriveMeters = 1f;
    // The leader's word that it is buying on another world counts this long; the party breaks up right after it.
    private const int HopValidMs = 60_000;
    // The leader buys before it invites, and may wait out a busy board.
    private const int InviteWaitMs = 10 * TimeUnits.MillisecondsPerMinute;
    private const string InviteThrottleKey = "atm-follow-invite";
    // Two characters landing on one point read as bots, so the member goes to a point of its own: the posted spot moved
    // a few metres in a direction drawn afresh for every map, and it stays there.
    private const float OffsetMinMeters = 3f;
    private const float OffsetMaxMeters = 7f;
    private const float OffsetArriveMeters = 1.5f;
    // In a dungeon the member walks toward the leader and stops this far short of it.
    private const float BesideLeaderMeters = 4f;
    // The leader's coffer is looted once the fight is this long over with nothing left to roll on.
    private const int MapSettledMs = 8_000;
    private const int CleanupWaitMs = 60_000;

    private static long portalPromptSeenAtMs;

    // Kept from one following to the next: a spot gone to once is not gone to again when the member starts over.
    private static long handledSpotAtMs;
    private long lastActiveAtMs;
    private FollowedMap? followed;


    private protected override bool KeepsPace => true;

    // False, with the reason in chat, when this character cannot follow: it must be in a party of the local list, not
    // its leader, with party mode on.
    public static bool CanStart()
    {
        if (!PartyPresence.LocalParty() || PartyPresence.LeadsTheParty())
        {
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatFollowNeedsParty)}");
            return false;
        }

        return true;
    }

    protected override async Task Execute()
    {
        // A spot posted before Start and not gone to yet is the leader's current one, which is still worth going to; one
        // from a party left behind was forgotten when it broke up.
        lastActiveAtMs = Environment.TickCount64;
        Svc.Framework.Update += AnswerPrompts;
        try
        {
            await Follow();
        }
        catch (Exception exception)
        {
            session.RecordFault(exception, CancelToken);
            throw;
        }
        finally
        {
            Svc.Framework.Update -= AnswerPrompts;
            ReleaseCombatMovement("follow");
        }
    }

    private async Task Follow()
    {
        await HoldCombatMovementAndSettle("follow");
        while (!CancelToken.IsCancellationRequested)
        {
            if (!await EnsureStanding())
            {
                if (!CancelToken.IsCancellationRequested)
                {
                    session.Finish(RunEnd.StillKnockedOut);
                }

                return;
            }

            // The leader broke the party up to buy on another world: go there and wait for its invite.
            if (!PartyPresence.InParty() && PartyHop.Pending is { } hop && Environment.TickCount64 - hop.ReceivedAtMs < HopValidMs)
            {
                await FollowWorldHop(hop);
                lastActiveAtMs = Environment.TickCount64;
                continue;
            }

            if (!PartyPresence.LocalParty() || PartyPresence.LeadsTheParty())
            {
                Diag("Follow: the party or its leader is gone; following ends.");
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatFollowEnded)}");
                EndFollowedMap(portal: false);
                session.Finish(RunEnd.PartyEnded);
                return;
            }

            if (DungeonRegistry.Find(Svc.ClientState.TerritoryType) is { } handler)
            {
                EndFollowedMap(portal: true);
                await FollowInDungeon(handler);
                lastActiveAtMs = Environment.TickCount64;
                continue;
            }

            RollPartyLoot();
            if (PartyFightOn())
            {
                progress.SetPhase(TreasurePhase.Fighting);
                await FightAlong("follow-fight");
                lastActiveAtMs = Environment.TickCount64;
                if (followed is not null)
                {
                    followed.FoughtAtMs = lastActiveAtMs;
                }

                continue;
            }

            if (PartySpotLink.Latest is { } posted && posted.ReceivedAtMs > handledSpotAtMs)
            {
                handledSpotAtMs = posted.ReceivedAtMs;
                EndFollowedMap(portal: false);
                await GoToSpot(posted);
                lastActiveAtMs = Environment.TickCount64;
                continue;
            }

            if (Environment.TickCount64 - lastActiveAtMs >= IdleLimitMs)
            {
                Diag($"Follow: no spot from the leader for {IdleLimitMs / TimeUnits.MillisecondsPerMinute} minutes; following ends.");
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatFollowEnded)}");
                EndFollowedMap(portal: false);
                session.Finish(RunEnd.PartyEnded);
                return;
            }

            if (followed is { FoughtAtMs: > 0 } settling && Environment.TickCount64 - settling.FoughtAtMs >= MapSettledMs
                && LootRolls.Pending(static _ => true) == 0 && !PortalNear())
            {
                EndFollowedMap(portal: false);
                await CleanUpLoot("Follow");
                continue;
            }

            progress.SetPhase(TreasurePhase.Preparing);
            Status = Loc.T(L.Status.WaitingLeaderSpot);
            await NextFrame(TickFrames);
        }
    }

    // A point on the way from this member to the leader, short of it, so a walk to rejoin stops beside the leader.
    private static Vector3 BesideLeader(Vector3 leader)
    {
        var away = PlayerPosition() - leader;
        away.Y = 0f;
        return away.LengthSquared() < BesideLeaderMeters * BesideLeaderMeters ? PlayerPosition() : leader + Vector3.Normalize(away) * BesideLeaderMeters;
    }

    // The leader's map, as this member took part in it: what the bags gained from the trip there to the loot settling is
    // counted as the map's, recorded on the session and noted for the cleanup like a map of its own. A map whose fight
    // never came (the leader's dig was refused) is not counted.
    private void EndFollowedMap(bool portal)
    {
        if (followed is not { } map)
        {
            return;
        }

        followed = null;
        if (map.FoughtAtMs == 0)
        {
            Diag($"Follow: the {map.Tier.Name} map ended without a fight; not counted.");
            return;
        }

        var (gil, items) = BagSnapshot.Gains(map.BagsBefore, BagSnapshot.Capture());
        var result = new MapResult(map.Tier, MapOutcome.Looted, MapRefusal.None, portal, gil, items, TimeSpan.FromMilliseconds(Environment.TickCount64 - map.StartedAtMs));
        session.RecordMap(result);
        progress.FinishMap(result);
        LootCleanup.Note(map.BagsBefore);
        Diag($"Follow: the {map.Tier.Name} map gave {gil} gil and {result.ItemCount} items in {items.Length} stacks{(portal ? ", and a portal" : string.Empty)}.");
    }

    // The discards answer their own questions, so the vault call is not watched for meanwhile.
    private async Task CleanUpLoot(string scope)
    {
        await DiscardNotedLoot(CleanupWaitMs, scope);
        lastActiveAtMs = Environment.TickCount64;
    }

    private sealed class FollowedMap(TreasureTier tier)
    {
        public TreasureTier Tier { get; } = tier;

        public Dictionary<uint, int> BagsBefore { get; } = BagSnapshot.Capture();

        public long StartedAtMs { get; } = Environment.TickCount64;

        // When the last fight at the spot ended; 0 until one did.
        public long FoughtAtMs { get; set; }
    }

    private async Task GoToSpot(PostedSpot posted)
    {
        if (!DigSpotResolver.TryFindNear(posted.TerritoryId, posted.At, SpotMatchMeters, out var spot))
        {
            Warn($"Follow: the leader's spot in territory {posted.TerritoryId} at ({posted.At.X:F1}, {posted.At.Y:F1}) is no dig spot this plugin knows; staying here.");
            return;
        }

        if (Plugin.Instance.Catalog.TryGetByRank(spot.Rank, out var tier))
        {
            followed = new FollowedMap(tier);
            progress.BeginMap(tier, spot);
        }

        Diag($"Follow: going to the leader's spot in {spot.ZoneName} at ({spot.Position.X:F1}, {spot.Position.Y:F1}, {spot.Position.Z:F1}), {(Environment.TickCount64 - posted.ReceivedAtMs) / 1000.0:F1}s after it was posted.");
        progress.SetPhase(TreasurePhase.Travelling);
        await HoldCombatMovementAndSettle("follow-travel");
        var angle = Random.Shared.NextSingle() * MathF.Tau;
        var meters = OffsetMinMeters + Random.Shared.NextSingle() * (OffsetMaxMeters - OffsetMinMeters);
        var target = spot.Position + new Vector3(MathF.Cos(angle) * meters, 0f, MathF.Sin(angle) * meters);
        Diag($"Follow: aiming {meters:F1}m off the spot, at ({target.X:F1}, {target.Y:F1}, {target.Z:F1}).");
        if (!await TravelTo(spot.TerritoryId, target, OffsetArriveMeters) || CancelToken.IsCancellationRequested)
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"Follow: could not reach the leader's spot in {spot.ZoneName}.");
            }

            return;
        }

        await LandAndDismount(target, LandsOnTheSpot, "follow-land");

        var tookMs = Environment.TickCount64 - posted.ReceivedAtMs;
        Diag($"Follow: at the leader's spot {tookMs / 1000.0:F1}s after it was posted.");
        await TopUpConsumables();
    }

    // Inside the dungeon the leader's run does the dungeon's own steps; this member stays by the leader, fights, rolls and
    // picks up the bonus round's sacks, until the dungeon is behind it.
    private async Task FollowInDungeon(TreasureDungeonHandler handler)
    {
        var profile = handler.Profile;
        const string scope = "follow-dungeon";
        Diag($"{scope}: in {profile.Key} with the leader.");
        await WaitUntilTimed(() => !Zoning(), DungeonWalkMs, "dungeon settled");
        await WaitForNavmeshReady(DungeonWalkMs);
        await HoldCombatMovementAndSettle(scope);
        var dungeon = new DungeonInProgress(profile) { BagsBefore = BagSnapshot.Capture() };
        var context = new DungeonContext(this, dungeon);
        var chamber = 1;
        // The round the duty list showed, the count the record goes by; the own count only stands in when it never showed.
        var listed = 0;
        // Where the leader last stood near this member, which is by the gate it took once it is carried on.
        var leaderNear = Vector3.Zero;
        while (!CancelToken.IsCancellationRequested && Svc.ClientState.TerritoryType == profile.TerritoryId)
        {
            if (Zoning())
            {
                await NextFrame(TickFrames);
                continue;
            }

            // A raise may come from the leader, and returning home would leave the dungeon, so a knockout is waited out.
            if (IsKnockedOut())
            {
                Status = Loc.T(L.Status.KnockedOutRaise);
                await NextFrame(TickFrames);
                continue;
            }

            if (DutyListRound.TryRead(handler, out var shown) && shown > listed)
            {
                listed = shown;
                Diag($"{scope}: the duty list shows round {listed} of {profile.Rounds}.");
            }

            RollPartyLoot();
            if (BonusRoundOn(dungeon) && profile.HasSacks)
            {
                progress.SetPhase(TreasurePhase.Fighting);
                await CollectBonusSacks(context);
                continue;
            }

            if (PartyFightOn())
            {
                progress.SetPhase(TreasurePhase.Fighting);
                await FightAlong($"{scope}-fight", context);
                if (profile.Kind == DungeonKind.Slot)
                {
                    chamber++;
                }

                continue;
            }

            progress.SetPhase(TreasurePhase.Preparing);
            Status = Loc.T(L.Status.KeepingByLeader);
            if (LeaderInZone() is { } leader)
            {
                var distance = Vector3.Distance(leader, PlayerPosition());
                if (distance < CarriedMeters)
                {
                    leaderNear = leader;
                }

                if (profile.Kind == DungeonKind.Gate && distance >= CarriedMeters && leaderNear != Vector3.Zero)
                {
                    await TakeCircleAfter(context, leaderNear, scope);
                    leaderNear = Vector3.Zero;
                    chamber++;
                }
                else if (distance > RejoinBeyondMeters && distance < CarriedMeters)
                {
                    await context.WalkTo(BesideLeader(leader), OffsetArriveMeters, () => LeaderInZone() is { } now && Vector3.Distance(now, PlayerPosition()) <= KeepByMeters, DungeonWalkMs, scope);
                }
            }

            await NextFrame(TickFrames);
        }

        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        // Without the duty list, a slot dungeon's count went up with every fight, the bonus round's included, so the draws
        // are counted near enough.
        var round = listed > 0 ? listed : profile.Kind == DungeonKind.Slot ? Math.Min(chamber - 1, (int)profile.Rounds) : chamber;
        var (gil, items) = BagSnapshot.Gains(dungeon.BagsBefore, BagSnapshot.Capture());
        var result = new DungeonResult(profile, HowItEnded(dungeon), round, gil, items, TimeSpan.FromMilliseconds(Environment.TickCount64 - dungeon.StartedAtMs));
        session.RecordDungeon(result);
        LootCleanup.Note(dungeon.BagsBefore);
        Diag($"{scope}: out of {profile.Key} ({result.Outcome}, round {round}{(listed > 0 ? " from the duty list" : ", counted")}), {gil} gil, {result.ItemCount} items in {items.Length} stacks.");
        await WaitUntilTimed(() => !Zoning() && NpcInteraction.PlayerReady(), DungeonWalkMs, "back outside");
        await CleanUpLoot("Follow");
    }

    // The leader stepped into the circle behind the gate it opened and was carried on; the member walks into the circle
    // nearest where the leader was last seen.
    private async Task TakeCircleAfter(DungeonContext context, Vector3 leaderWas, string scope)
    {
        var circles = context.Profile.GateCircles;
        if (circles.Length == 0)
        {
            return;
        }

        var circle = circles.MinBy(gate => Vector3.Distance(gate.At, leaderWas)).At;
        Diag($"{scope}: the leader was carried on from ({leaderWas.X:F1}, {leaderWas.Z:F1}); walking into the circle at ({circle.X:F1}, {circle.Y:F1}, {circle.Z:F1}).");
        var from = PlayerPosition();
        bool Carried() => context.Zoning || Vector3.Distance(PlayerPosition(), from) > CarriedMeters;
        await context.WalkTo(circle, CircleArriveMeters, Carried, DungeonWalkMs, scope);
        await WaitUntilTimed(Carried, DungeonWalkMs, "carried by the circle");
    }

    private async Task FollowWorldHop(WorldHop hop)
    {
        const string scope = "follow-world";
        EndFollowedMap(portal: false);
        var worldId = PartyHop.WorldIdOf(hop.WorldName);
        if (worldId == 0)
        {
            Warn($"{scope}: the leader named {hop.WorldName}, which is no world of this data centre.");
            PartyHop.ForgetPending();
            return;
        }

        PartyHop.AwaitedInviter = hop.LeaderName;
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatFollowWorld, hop.WorldName)}");
        progress.SetPhase(TreasurePhase.Travelling);
        await HoldCombatMovementAndSettle(scope);
        var started = Environment.TickCount64;
        if (!await ChangeWorld(worldId, scope))
        {
            Warn($"{scope}: could not travel to {hop.WorldName}; waiting for the invite where it stands.");
        }

        Diag($"{scope}: on {Worlds.NameOf(Worlds.Current)} {(Environment.TickCount64 - started) / 1000.0:F1}s after the word; waiting for {hop.LeaderName}'s invite.");
        progress.SetPhase(TreasurePhase.Preparing);
        Status = Loc.T(L.Status.WaitingLeaderInvite);
        var joined = await WaitUntilTimed(PartyPresence.LocalParty, InviteWaitMs, "leader's invite");
        PartyHop.ForgetPending();
        Diag($"{scope}: {(joined ? "back in the leader's party" : "no invite came")}.");
    }

    // The leader's position, while it stands in the character's zone.
    private static Vector3? LeaderInZone()
        => PartyPresence.Leader() is { } leader && leader.Territory.RowId == Svc.ClientState.TerritoryType ? leader.Position : null;

    // The leader's teleport offers the party a ride, which this character does not take: it travels on its own, and an
    // open question would stand in the way of everything else. The leader's call into a treasure vault is accepted,
    // after a moment, while a portal stands near; its text is logged.
    private static void AnswerPrompts(IFramework framework)
    {
        if (!NpcInteraction.SelectYesnoOpen() || LootCleanup.Running)
        {
            portalPromptSeenAtMs = 0;
            return;
        }

        var prompt = NpcInteraction.SelectYesnoText();
        if (PartyHop.AwaitedInviter is { Length: > 0 } inviter && ForeignPrompts.IsPartyInvite(prompt) && prompt.Contains(inviter, StringComparison.Ordinal))
        {
            if (EzThrottler.Throttle(InviteThrottleKey, DeclineThrottleMs))
            {
                RunLog.Info($"Follow: accepting {inviter}'s invite.");
                DialogDriver.Confirm();
            }

            return;
        }

        if (ForeignPrompts.IsTeleportOffer(prompt))
        {
            if (EzThrottler.Throttle(DeclineThrottleKey, DeclineThrottleMs))
            {
                RunLog.Info("Follow: declining the leader's teleport offer; travelling on our own.");
                NpcInteraction.AnswerNo();
            }

            return;
        }

        if (ForeignPrompts.Matches(prompt) || !PortalNear())
        {
            return;
        }

        var now = Environment.TickCount64;
        if (portalPromptSeenAtMs == 0)
        {
            portalPromptSeenAtMs = now;
            RunLog.Info($"Follow: the game asks '{prompt}' with a portal near; answering Yes.");
            DutyWatcher.HoldForDungeonEntry();
            return;
        }

        if (now - portalPromptSeenAtMs >= PortalConfirmDelayMs && EzThrottler.Throttle(DeclineThrottleKey, DeclineThrottleMs))
        {
            DialogDriver.Confirm();
        }
    }

    private static bool PortalNear()
    {
        Span<ulong> found = stackalloc ulong[4];
        return Svc.Objects.LocalPlayer is { } player && TreasurePortals.Collect(player.Position, PortalNearMeters, found) > 0;
    }
}
