using AutoTreasureMaps.Core.Game;
using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

// A party member's run in party mode: the leader runs the maps, and this character goes to each dig spot the leader
// posts, fights beside it and rolls on the loot, until the party or its leader is gone. It leaves the moment a spot is
// posted, so it reaches the spot about when the leader does, and it takes no humanizer waits of its own.
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
        Svc.Framework.Update += DeclineTeleportOffers;
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
            Svc.Framework.Update -= DeclineTeleportOffers;
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

            if (!Plugin.Instance.Configuration.PartyMode || !PartyPresence.LocalParty() || PartyPresence.LeadsTheParty())
            {
                Diag("Follow: the party or its leader is gone; following ends.");
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatFollowEnded)}");
                session.Finish(RunEnd.PartyEnded);
                return;
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

    // The leader's teleport offers the party a ride, which this character does not take: it travels on its own, and an
    // open question would stand in the way of everything else.
    private static void DeclineTeleportOffers(IFramework framework)
    {
        if (NpcInteraction.SelectYesnoOpen() && ForeignPrompts.IsTeleportOffer(NpcInteraction.SelectYesnoText())
            && EzThrottler.Throttle(DeclineThrottleKey, DeclineThrottleMs))
        {
            RunLog.Info("Follow: declining the leader's teleport offer; travelling on our own.");
            NpcInteraction.AnswerNo();
        }
    }
}
