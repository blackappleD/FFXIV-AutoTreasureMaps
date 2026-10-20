using AutoTreasureMaps.Core.Cleanup;
using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;
using ECommons.DalamudServices;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    // The coffer, its guards and a portal all appear within the dig's own 30 yalm reach of the spot, so these leave
    // room for a coffer at the edge of it and guards that chase the character a little way.
    private const float CofferSearchMeters = 60f;
    private const float GuardSearchMeters = 50f;
    private const float PortalSearchMeters = 60f;

    private TreasureProgress? mapProgress;
    private bool huntClockLogged;

    // Set when the last RunHeldMap ended on an unexpected error, for the caller to hand to the session's fault handling.
    private protected Exception? MapFault { get; private set; }

    // Runs the held map from travel to loot, or picks the session's map up where a pause or a fault left its hunt. Any
    // run task can call it once per map: the result is recorded on the session, shown through progress and announced
    // in chat. A Stop or a Pause returns Cancelled and leaves the session's map in place for the resume.
    private protected async Task<MapResult> RunHeldMap(TreasureSession session, TreasureProgress progress)
    {
        mapProgress = progress;
        huntClockLogged = false;
        MapFault = null;
        MapResult result;
        try
        {
            result = await PlayMap(session);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && !CancelToken.IsCancellationRequested)
        {
            MapFault = exception;
            var active = session.ActiveMap;
            result = Conclude(active, active?.Tier ?? default, MapOutcome.Faulted);
        }

        mapProgress = null;
        if (result.Outcome == MapOutcome.Cancelled || CancelToken.IsCancellationRequested)
        {
            Diag("Map: stopped before it finished; the session keeps it for a resume.");
            return result with { Outcome = MapOutcome.Cancelled };
        }

        // The portal to enter is the one nearest the coffer, or the spot when the dig itself turned it up.
        if ((result.PortalSeen || result.Refusal == MapRefusal.PortalDug) && session.ActiveMap is { } ended)
        {
            session.PendingPortal = new PortalLead(ended.CofferOpened ? ended.CofferPosition : ended.Spot.Position, ended.PortalsBefore);
        }

        // A faulted map is noted once it ends after the restart, and a map paused after its coffer opened not at all.
        var bagsBefore = session.ActiveMap is { CleanupBlocked: false } noted ? noted.BagsBefore : null;
        var final = MapOutcomes.Final(result.Outcome);
        if (final)
        {
            session.ActiveMap = null;
        }

        session.RecordMap(result);
        if (final)
        {
            LootCleanup.Note(bagsBefore);
        }

        progress.FinishMap(result);
        Diag($"Map: {(result.HasTier ? result.Tier.Name : "no map")} ended {result.Outcome}{(result.Refusal == MapRefusal.None ? string.Empty : $" ({result.Refusal})")}, {result.Gil} gil, {result.ItemCount} items in {result.Items.Length} stacks, portal {(result.PortalSeen ? "seen" : "not seen")}, {result.Duration.TotalSeconds:F0}s.");
        Announce(result);
        return result;
    }

    private async Task<MapResult> PlayMap(TreasureSession session)
    {
        var map = session.ActiveMap;
        if (map is { CofferOpened: true })
        {
            return await ResumeHunt(map);
        }

        var refusal = MapRunReadiness.MapBlocker(Plugin.Instance.Catalog, out var tier, out var spot);
        if (refusal != MapRefusal.None)
        {
            Diag($"Map: refused before it started: {refusal}.");
            return MapResult.Refused(tier, refusal);
        }

        if (map is null || map.Tier.Rank != tier.Rank || map.Spot.LevelId != spot.LevelId)
        {
            map = new MapInProgress(tier, spot);
            session.ActiveMap = map;
        }

        ShowMap(map);
        Diag($"Map: {tier.Name} (rank {tier.Rank}, spot {spot.SpotIndex}, Level {spot.LevelId}) in {spot.ZoneName} at {FormatPosition(spot.Position)}, flying {(spot.FlyingUnlocked ? "unlocked" : "locked")}.");
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.MapRun.ChatStarted, tier.Name, spot.ZoneName)}");
        await TopUpConsumables();

        if (!await ReachSpot(map.Spot))
        {
            return Conclude(map, tier, CancelToken.IsCancellationRequested ? MapOutcome.Cancelled : MapOutcome.Unreachable);
        }

        var dig = await DigUp(map);
        switch (dig.Answer)
        {
            case DigAnswer.Found:
                break;
            case DigAnswer.Portal:
                return Conclude(map, tier, MapOutcome.Refused, MapRefusal.PortalDug);
            case DigAnswer.Refused:
                return Conclude(map, tier, MapOutcome.Refused, dig.Refusal);
            case DigAnswer.NoMap:
                return Conclude(map, tier, MapOutcome.Refused, MapRefusal.NoMap);
            case DigAnswer.Unreachable:
                return Conclude(map, tier, MapOutcome.Unreachable);
            case DigAnswer.Cancelled:
                return Conclude(map, tier, MapOutcome.Cancelled);
            default:
                return Conclude(map, tier, MapOutcome.DigFailed);
        }

        var coffer = await OpenCoffer(map, dig.Fresh);
        return coffer switch
        {
            CofferStep.Opened => await FinishHunt(map),
            CofferStep.PromptMismatch => Conclude(map, tier, MapOutcome.Refused, MapRefusal.CofferPrompt),
            CofferStep.Cancelled => Conclude(map, tier, MapOutcome.Cancelled),
            _ => Conclude(map, tier, MapOutcome.Unreachable),
        };
    }

    // Once the coffer has opened the map is gone, so a resumed run can only go on where the hunt still runs.
    private async Task<MapResult> ResumeHunt(MapInProgress map)
    {
        if (HuntStillOn(map))
        {
            Diag($"Map: picking the {map.Tier.Name} hunt up where it stood, {(Environment.TickCount64 - map.CofferOpenedAtMs) / TimeUnits.MillisecondsPerSecond}s after its coffer opened, wave {map.Wave}.");
            ShowMap(map);
            return await FinishHunt(map);
        }

        var expired = Messages.ArrivedSince(TreasureLogMessages.HuntTimeExpired, map.CofferOpenedAtMs);
        Diag($"Map: the {map.Tier.Name} hunt ended while the run was away from it ({(expired ? "its time ran out" : "it is gone")}).");
        return Conclude(map, map.Tier, expired ? MapOutcome.TimedOut : MapOutcome.Unreachable);
    }

    private async Task<bool> ReachSpot(DigSpot spot)
    {
        EnterStep(TreasurePhase.Travelling);
        await HoldCombatMovementAndSettle("map-travel");
        if (!await TravelTo(spot.TerritoryId, spot.Position, AutoGoto.DigSpotArriveWithinMeters))
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"Map: could not reach the dig spot in {spot.ZoneName}.");
            }

            return false;
        }

        if (!await LandAndDismount(spot.Position, LandsOnTheSpot, "map-land"))
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"Map: arrived above the dig spot in {spot.ZoneName} but could not land on it.");
            }

            return false;
        }

        return !CancelToken.IsCancellationRequested;
    }

    // The game keeps the hunt in the coffer's zone only; a knockout's trip home or a teleport away ends it.
    private static bool HuntStillOn(MapInProgress map)
        => Svc.ClientState.TerritoryType == map.Spot.TerritoryId
        && (TreasureHuntDirector.IsActive() || (map.CofferId != 0 && !TreasureCoffers.Gone(map.CofferId)));

    private void EnterStep(TreasurePhase phase)
    {
        CurrentPhase = phase;
        if (mapProgress is not null && !CancelToken.IsCancellationRequested)
        {
            mapProgress.SetPhase(phase);
        }
    }

    private void ShowMap(MapInProgress map)
    {
        if (mapProgress is not null && !CancelToken.IsCancellationRequested)
        {
            mapProgress.BeginMap(map.Tier, map.Spot);
            mapProgress.SetFight(map.Wave, 0);
        }
    }

    private void ShowFight(int wave, int guardsLeft)
    {
        if (mapProgress is not null && !CancelToken.IsCancellationRequested)
        {
            mapProgress.SetFight(wave, guardsLeft);
        }
    }

    // Whether the director's end is the hunt's time limit is checked in game, so its raw values are logged once a map.
    private void ReadHuntClock()
    {
        var readable = TreasureHuntDirector.TryReadTimes(out var start, out var end);
        if (!huntClockLogged && (readable || start != 0 || end != 0 || TreasureHuntDirector.IsActive()))
        {
            huntClockLogged = true;
            Diag($"Map: hunt director {(TreasureHuntDirector.IsActive() ? "active" : "not found")}, start {start}, end {end}, server now {TreasureHuntDirector.ServerNow()}, read as a limit: {readable}.");
        }

        if (readable && mapProgress is not null && !CancelToken.IsCancellationRequested && mapProgress.HuntEndsAt != end)
        {
            mapProgress.SetHuntEnd(end);
        }
    }

    private void NotePortal(MapInProgress map)
    {
        if (map.PortalSeen || !map.CofferOpened)
        {
            return;
        }

        // Only this coffer's word, or a portal that was not standing before the dig, is ours; another player's portal nearby
        // is not.
        var announced = Messages.ArrivedSince(TreasureLogMessages.PortalAppeared, map.CofferOpenedAtMs);
        var portal = TreasurePortals.FindNew(map.CofferPosition, PortalSearchMeters, map.PortalsBefore);
        if (!announced && portal is null)
        {
            return;
        }

        map.PortalSeen = true;
        var found = portal is null ? "no portal object nearby" : $"portal object {portal.BaseId} at {FormatPosition(portal.Position)}";
        var entering = Plugin.Instance.Configuration.DungeonEnterPortal && !PartyPresence.InParty();
        Diag($"Map: a portal appeared (message {(announced ? "seen" : "not seen")}, {found}); {(entering ? "it is entered once the coffer is looted" : "it is left alone")}.");
        if (!entering)
        {
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.MapRun.ChatPortal)}");
        }
    }

    private static MapResult Conclude(MapInProgress? map, in TreasureTier tier, MapOutcome outcome, MapRefusal refusal = MapRefusal.None)
    {
        if (map is null || outcome == MapOutcome.Cancelled)
        {
            return new MapResult(tier, outcome, refusal, map?.PortalSeen ?? false, 0, ImmutableArray<ItemGain>.Empty, TimeSpan.Zero);
        }

        var (gil, items) = map.BagsBefore is { } before ? BagSnapshot.Gains(before, BagSnapshot.Capture()) : (0, ImmutableArray<ItemGain>.Empty);
        var duration = TimeSpan.FromMilliseconds(Environment.TickCount64 - map.StartedAtMs);
        return new MapResult(tier, outcome, refusal, map.PortalSeen, gil, items, duration);
    }

    private static void Announce(in MapResult result)
    {
        if (!result.HasTier)
        {
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.MapRun.ChatRefused, Loc.T(MapOutcomes.Cause(result)))}");
            return;
        }

        var name = result.Tier.Name;
        if (result.Outcome == MapOutcome.Looted)
        {
            var gil = result.Gil.ToString("N0", Loc.Culture);
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.Plural(L.MapRun.ChatLooted, result.ItemCount, name, gil)}");
            return;
        }

        var outcome = Loc.T(MapOutcomes.Label(result.Outcome));
        var line = result.Refusal == MapRefusal.None
            ? Loc.T(L.MapRun.ChatEnded, name, outcome)
            : Loc.T(L.MapRun.ChatEndedReason, name, outcome, Loc.T(MapOutcomes.Reason(result.Refusal)));
        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {line}");
    }
}
