using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Game;
using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Game.Watchers;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Throttlers;
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
    // The member stands this far off the spot, clear of the leader's dig.
    private const float StandOffMeters = 4f;
    private const float ArriveWithinMeters = 6f;
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

    private static long portalPromptSeenAtMs;

    private long handledSpotAtMs;
    private long lastActiveAtMs;

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
        // A spot posted before Start is the leader's current one, which is still worth going to.
        handledSpotAtMs = 0;
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

            if (!PartyPresence.LocalParty() || PartyPresence.LeadsTheParty())
            {
                Diag("Follow: the party or its leader is gone; following ends.");
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatFollowEnded)}");
                session.Finish(RunEnd.PartyEnded);
                return;
            }

            if (DungeonRegistry.Find(Svc.ClientState.TerritoryType) is { } handler)
            {
                await FollowInDungeon(handler.Profile);
                lastActiveAtMs = Environment.TickCount64;
                continue;
            }

            RollPartyLoot();
            if (PartyFightOn())
            {
                progress.SetPhase(TreasurePhase.Fighting);
                await FightAlong("follow-fight");
                lastActiveAtMs = Environment.TickCount64;
                continue;
            }

            if (PartySpotLink.Latest is { } posted && posted.ReceivedAtMs > handledSpotAtMs)
            {
                handledSpotAtMs = posted.ReceivedAtMs;
                await GoToSpot(posted);
                lastActiveAtMs = Environment.TickCount64;
                continue;
            }

            if (Environment.TickCount64 - lastActiveAtMs >= IdleLimitMs)
            {
                Diag($"Follow: no spot from the leader for {IdleLimitMs / TimeUnits.MillisecondsPerMinute} minutes; following ends.");
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatFollowEnded)}");
                session.Finish(RunEnd.PartyEnded);
                return;
            }

            progress.SetPhase(TreasurePhase.Preparing);
            Status = "Waiting for the leader's next spot";
            await NextFrame(TickFrames);
        }
    }

    private async Task GoToSpot(PostedSpot posted)
    {
        if (!DigSpotResolver.TryFindNear(posted.TerritoryId, posted.At, SpotMatchMeters, out var spot))
        {
            Warn($"Follow: the leader's spot in territory {posted.TerritoryId} at ({posted.At.X:F1}, {posted.At.Y:F1}) is no dig spot this plugin knows; staying here.");
            return;
        }

        Diag($"Follow: going to the leader's spot in {spot.ZoneName} at ({spot.Position.X:F1}, {spot.Position.Y:F1}, {spot.Position.Z:F1}), {(Environment.TickCount64 - posted.ReceivedAtMs) / 1000.0:F1}s after it was posted.");
        progress.SetPhase(TreasurePhase.Travelling);
        await HoldCombatMovementAndSettle("follow-travel");
        if (!await TravelTo(spot.TerritoryId, spot.Position, ArriveWithinMeters) || CancelToken.IsCancellationRequested)
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"Follow: could not reach the leader's spot in {spot.ZoneName}.");
            }

            return;
        }

        await LandAndDismount(spot.Position, StandOffMeters, "follow-land");
        var tookMs = Environment.TickCount64 - posted.ReceivedAtMs;
        Diag($"Follow: at the leader's spot {tookMs / 1000.0:F1}s after it was posted.");
        await TopUpConsumables();
    }

    // Inside the dungeon the leader's run does the dungeon's own steps; this member stays by the leader, fights, rolls and
    // picks up the bonus round's sacks, until the dungeon is behind it.
    private async Task FollowInDungeon(DungeonProfile profile)
    {
        const string scope = "follow-dungeon";
        Diag($"{scope}: in {profile.Key} with the leader.");
        await WaitUntilTimed(() => !Zoning(), DungeonWalkMs, "dungeon settled");
        await WaitForNavmeshReady(DungeonWalkMs);
        await HoldCombatMovementAndSettle(scope);
        var dungeon = new DungeonInProgress(profile);
        var context = new DungeonContext(this, dungeon);
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
                Status = "Knocked out, waiting for a raise";
                await NextFrame(TickFrames);
                continue;
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
                await FightAlong($"{scope}-fight");
                continue;
            }

            progress.SetPhase(TreasurePhase.Preparing);
            Status = "Keeping by the leader";
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
                }
                else if (distance > RejoinBeyondMeters && distance < CarriedMeters)
                {
                    await context.WalkTo(leader, KeepByMeters, () => LeaderInZone() is { } now && Vector3.Distance(now, PlayerPosition()) <= KeepByMeters, DungeonWalkMs, scope);
                }
            }

            await NextFrame(TickFrames);
        }

        Diag($"{scope}: out of {profile.Key}.");
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

    // The leader's position, while it stands in the character's zone.
    private static Vector3? LeaderInZone()
        => PartyPresence.Leader() is { } leader && leader.Territory.RowId == Svc.ClientState.TerritoryType ? leader.Position : null;

    // The leader's teleport offers the party a ride, which this character does not take: it travels on its own, and an
    // open question would stand in the way of everything else. The leader's call into a treasure vault is accepted,
    // after a moment, while a portal stands near; its text is logged.
    private static void AnswerPrompts(IFramework framework)
    {
        if (!NpcInteraction.SelectYesnoOpen())
        {
            portalPromptSeenAtMs = 0;
            return;
        }

        var prompt = NpcInteraction.SelectYesnoText();
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
