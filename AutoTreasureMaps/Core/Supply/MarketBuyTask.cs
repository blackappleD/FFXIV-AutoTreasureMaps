using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Market;
using AutoTreasureMaps.Core.Tasks;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Core.Treasure;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Supply;

// One visit to the market board: open it, then for each tier in the order the Maps settings choose, read the listings
// and buy the cheapest one under the cap. One map is bought per visit. Session is the run's, or null for a purchase
// outside a run; onlyRank names the one tier to buy, or is MarketRules.AnyTier.
internal sealed partial class MarketBuyTask(TreasureSession? session, byte onlyRank) : MapSupplyTask
{
    private const string Scope = "market";
    private const float BoardArrivalMeters = 6f;
    // A board this close is used where it stands, whichever city it is in.
    private const float NearbyBoardMeters = 15f;
    private const int BoardInteractAttempts = 2;
    private const int BoardReopens = 2;
    private const int RequestAttempts = 3;
    private const int PurchaseAttempts = 3;
    private const int RequestAnswerTimeoutMs = 8_000;
    private const int ListingsTimeoutMs = 10_000;
    private const int PurchaseAnswerTimeoutMs = 10_000;
    private const int PurchaseLandTimeoutMs = 5_000;
    private const int UnansweredSettleMs = 3_000;
    // The board's "please wait" is waited out and the same step tried again; only a long run of them ends the visit.
    private const int ThrottleRetryMs = 2_000;
    private const int ThrottleRetries = 30;
    // With cross-world buying on, a board that keeps asking to wait is left for another world after this many.
    private const int ThrottlesBeforeWorldChange = 5;
    // A player reads the list for a moment before picking a row.
    private const int ReadPauseMinMs = 1_000;
    private const int ReadPauseMaxMs = 3_000;
    private const int CloseTimeoutMs = 3_000;
    private const int PollFrames = 2;

    private readonly MarketListing[] listings = new MarketListing[MarketBoardOps.MaxListings];
    private readonly HashSet<ulong> refusedRetainers = [];
    private readonly HashSet<ulong> passedOver = [];
    private MarketHooks? hooks;
    private SentPurchase? sent;
    private int boardReopens;
    private int purchases;
    private int throttles;
    private bool leaveOnThrottle;
    private bool throttledOut;
    private LocString rereadReason = L.Market.FailThrottled;

    private enum ListingsRead : byte
    {
        Read,
        Retry,
        Throttled,
        BoardLost,
        Interfered,
    }

    private enum Attempt : byte
    {
        Bought,
        OwnRetainer,
        ListingGone,
        UniqueHeld,
        BagFull,
        NotEnoughGil,
        Throttled,
        Stale,
        Unanswered,
        NotLanded,
        Cancelled,
    }

    private enum TierStep : byte
    {
        Reread,
        NextTier,
        Done,
    }

    // A purchase sent and not yet settled. Sequence is the purchase answer count noted just before it went out.
    private readonly record struct SentPurchase(TreasureTier Tier, MarketListing Listing, uint GilBefore, int HeldBefore, int Sequence);

    private MarketHooks Answers => hooks ?? throw new InvalidOperationException("The market board's answers are not hooked.");

    protected override async Task<MapSupplyAnswer> Supply()
    {
        var plugin = Plugin.Instance;
        var tiers = MarketRules.Candidates(plugin, session, onlyRank);
        if (tiers.Count == 0)
        {
            Diag("Market: no tier is left to buy.");
            return MapSupplyAnswer.Empty(L.Market.ReasonAllHeld);
        }

        hooks = MarketHooks.Shared();
        if (hooks is null)
        {
            return Fail(L.Market.ReasonUnsupported);
        }

        // A stop can cut the waits below short at any point, so the board's windows are closed from the cancel itself.
        using var stopRegistration = CancelToken.Register(CloseBoardNow);
        try
        {
            var answer = await VisitCheapestWorld(plugin.Configuration, tiers);
            if (answer.Outcome == MapSupplyOutcome.Supplied)
            {
                await BuyAhead(plugin);
            }

            if (!CancelToken.IsCancellationRequested)
            {
                await CloseBoard();
            }

            return answer;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CloseBoardNow();
            throw;
        }
        finally
        {
            SettleCutOff();
            hooks = null;
        }
    }

    // A world Universalis named whose board turns out to hold nothing under the cap is left for the next cheapest. Any
    // other empty answer (the budget or the gil to keep) would be the same on every world, so it ends the visit.
    private async Task<MapSupplyAnswer> VisitCheapestWorld(Configuration configuration, List<TreasureTier> tiers)
    {
        MapSupplyAnswer? previous = null;
        // The world whose board kept asking to wait, while another is looked for; and once none qualifies, the run
        // waits on that board instead of leaving it again.
        uint throttledOn = 0;
        var waitOutThrottles = false;
        for (var hop = 0; ; hop++)
        {
            var travelledTo = await MoveToCheapestWorld(configuration, tiers, throttledOn);
            if (CancelToken.IsCancellationRequested)
            {
                return MapSupplyAnswer.Cancelled;
            }

            if (throttledOn != 0 && travelledTo == 0)
            {
                Diag($"Market: no other world qualifies; waiting on the board on {Worlds.NameOf(throttledOn)} instead.");
                excludedWorlds.Remove(throttledOn);
                waitOutThrottles = true;
            }

            throttledOn = 0;

            // No other world qualifies after an empty board, so the board just read is not read again.
            if (previous is { } empty && travelledTo == 0)
            {
                return empty;
            }

            leaveOnThrottle = !waitOutThrottles && hop < MaxWorldHops && CanVisitWorlds(configuration);
            var answer = await Visit(configuration, tiers);
            leaveOnThrottle = false;
            if (throttledOut)
            {
                throttledOut = false;
                var here = Worlds.Current;
                Diag($"Market: the board on {Worlds.NameOf(here)} asked to wait {ThrottlesBeforeWorldChange} times; looking for another world.");
                throttledOn = here;
                excludedWorlds.Add(here);
                await CloseBoard();
                continue;
            }

            if (answer.Outcome != MapSupplyOutcome.Empty || answer.Reason.Key != L.Market.EmptyNothingUnderCap.Key || travelledTo == 0 || hop >= MaxWorldHops)
            {
                return answer;
            }

            previous = answer;

            Diag($"Market: {Worlds.NameOf(travelledTo)} had nothing to buy after all; trying the next cheapest world.");
            excludedWorlds.Add(travelledTo);
            await CloseBoard();
        }
    }

    private async Task<MapSupplyAnswer> Visit(Configuration configuration, List<TreasureTier> tiers)
    {
        Diag($"Market: {tiers.Count} tier(s) to try, {MarketRules.BudgetLeft(configuration, session)} gil of budget left, {MarketBoardOps.Gil()} gil held, {configuration.MarketKeepGil} to keep.");
        if (!await OpenBoard(MarketBoards.Resolve(configuration.MarketBoard)))
        {
            return CancelToken.IsCancellationRequested ? MapSupplyAnswer.Cancelled : Fail(L.Market.FailBoard);
        }

        for (var index = 0; index < tiers.Count; index++)
        {
            var answer = await BuyTier(configuration, tiers[index]);
            if (answer is { } final)
            {
                return final;
            }
        }

        return MapSupplyAnswer.Empty(L.Market.EmptyNothingUnderCap);
    }

    // Null moves on to the next tier; an answer ends the visit.
    private async Task<MapSupplyAnswer?> BuyTier(Configuration configuration, TreasureTier tier)
    {
        passedOver.Clear();
        purchases = 0;
        throttles = 0;
        for (var request = 1; request <= RequestAttempts; request++)
        {
            // Listings read once the tier's purchase tries are spent could not be bought from, so they are not asked for.
            if (purchases >= PurchaseAttempts)
            {
                return Fail(rereadReason);
            }

            if (request > 1)
            {
                // Each retry waits longer: the spacing once, then twice.
                await DelayMs(MarketRequestGate.SpacingMs * (request - 1));
            }

            if (CancelToken.IsCancellationRequested)
            {
                return MapSupplyAnswer.Cancelled;
            }

            var (read, count) = await ReadListings(tier);
            switch (read)
            {
                case ListingsRead.Retry:
                    continue;
                case ListingsRead.Throttled:
                    if (!await WaitOutThrottle())
                    {
                        return GaveUpWaiting();
                    }

                    // A refusal to wait does not use up one of the request tries.
                    request--;
                    continue;
                case ListingsRead.BoardLost:
                    return Fail(L.Market.FailBoard);
                case ListingsRead.Interfered:
                    return Fail(L.Market.FailInterfered);
            }

            var (step, answer) = await TryListings(configuration, tier, count);
            if (step == TierStep.NextTier)
            {
                return null;
            }

            if (step == TierStep.Done)
            {
                return answer;
            }
        }

        return CancelToken.IsCancellationRequested ? MapSupplyAnswer.Cancelled : Fail(rereadReason);
    }

    private async Task<(TierStep Step, MapSupplyAnswer Answer)> TryListings(Configuration configuration, TreasureTier tier, int count)
    {
        var cap = MarketRules.CapOf(configuration, tier.Rank);
        while (purchases < PurchaseAttempts)
        {
            if (!TryPickCheapest(count, cap, MarketRules.Spendable(configuration, session), out var pick, out var overBudget))
            {
                ReportNothingToBuy(tier, cap, count, overBudget);
                return (TierStep.NextTier, default);
            }

            var attempt = await Buy(configuration, tier, pick);
            if (attempt is not (Attempt.Stale or Attempt.Cancelled or Attempt.Throttled))
            {
                purchases++;
            }

            switch (attempt)
            {
                case Attempt.Bought:
                    boughtTier = tier;
                    return (TierStep.Done, MapSupplyAnswer.Supplied);
                case Attempt.OwnRetainer:
                    refusedRetainers.Add(pick.RetainerId);
                    continue;
                case Attempt.ListingGone:
                    passedOver.Add(pick.ListingId);
                    continue;
                case Attempt.UniqueHeld:
                    session?.SkipMarketTier(tier.Rank);
                    Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Market.ChatUniqueHeld, tier.Name)}");
                    return (TierStep.NextTier, default);
                case Attempt.BagFull:
                    return (TierStep.Done, Fail(L.Market.ReasonBagFull));
                case Attempt.NotEnoughGil:
                    return (TierStep.Done, OutOfGil());
                case Attempt.NotLanded:
                    return (TierStep.Done, Fail(L.Market.FailNotLanded));
                case Attempt.Cancelled:
                    return (TierStep.Done, MapSupplyAnswer.Cancelled);
                case Attempt.Throttled:
                    if (!await WaitOutThrottle())
                    {
                        return (TierStep.Done, GaveUpWaiting());
                    }

                    continue;
                case Attempt.Stale:
                    rereadReason = L.Market.FailListingsChanged;
                    return (TierStep.Reread, default);
                default:
                    return (TierStep.Done, Fail(L.Market.FailNoAnswer));
            }
        }

        return (TierStep.Done, Fail(L.Market.FailListingsChanged));
    }

    private async Task<(ListingsRead Read, int Count)> ReadListings(TreasureTier tier)
    {
        Status = $"Reading the listings of {tier.Name}";
        var waitMs = MarketRequestGate.WaitMs();
        if (waitMs > 0)
        {
            await DelayMs(waitMs);
        }

        // Checked after the spacing wait, so a board closed during it is opened again before the request goes out.
        if (!await EnsureBoardOpen())
        {
            return (CancelToken.IsCancellationRequested ? ListingsRead.Retry : ListingsRead.BoardLost, 0);
        }

        if (CancelToken.IsCancellationRequested)
        {
            return (ListingsRead.Retry, 0);
        }

        var itemId = tier.TimewornItemId;
        var sequence = Answers.Listings.Sequence;
        if (!MarketBoardOps.RequestListings(itemId))
        {
            Warn($"Market: the game did not send the listing request for {tier.Name}.");
            rereadReason = L.Market.FailNoAnswer;
            return (ListingsRead.Retry, 0);
        }

        MarketRequestGate.NoteRequest();
        if (!await WaitUntilTimed(() => Answers.Listings.Sequence != sequence, RequestAnswerTimeoutMs, "market listings answer", PollFrames))
        {
            rereadReason = L.Market.FailNoAnswer;
            return (ListingsRead.Retry, 0);
        }

        var answer = Answers.Listings;
        if (answer.Error != 0)
        {
            var limit = answer.Error == MarketReplies.RequestRateLimited ? ", the rate limit" : string.Empty;
            Diag($"Market: the listing request for {tier.Name} was refused with status 0x{answer.Error:X8}{limit}.");
            return (ListingsRead.Throttled, 0);
        }

        if (answer.Count == 0)
        {
            Diag($"Market: no {tier.Name} is listed.");
            return (ListingsRead.Read, 0);
        }

        var complete = await WaitUntilTimed(() => MarketBoardOps.ListingsComplete(itemId, answer.Count) || MarketBoardOps.SearchedItem() != itemId,
            ListingsTimeoutMs, "market listings complete", PollFrames);
        if (MarketBoardOps.SearchedItem() != itemId)
        {
            Warn($"Market: the board's search moved to item {MarketBoardOps.SearchedItem()} while {tier.Name} was read.");
            return (ListingsRead.Interfered, 0);
        }

        if (!complete)
        {
            Diag($"Market: {answer.Count} listing(s) of {tier.Name} were promised, but not all of them arrived.");
            rereadReason = L.Market.FailNoAnswer;
            return (ListingsRead.Retry, 0);
        }

        var count = MarketBoardOps.ReadListings(itemId, answer.Count, listings);
        Diag($"Market: {answer.Count} listing(s) of {tier.Name} in, {count} usable.");
        return (ListingsRead.Read, count);
    }

    // False once the tier has been told to wait too many times, or the run was stopped during the wait.
    private async Task<bool> WaitOutThrottle()
    {
        throttles++;
        if (leaveOnThrottle && throttles >= ThrottlesBeforeWorldChange)
        {
            throttledOut = true;
            return false;
        }

        if (throttles > ThrottleRetries)
        {
            Warn($"Market: the board asked to wait {ThrottleRetries} times for this tier; giving up on the visit.");
            return false;
        }

        Diag($"Market: the board asked to wait ({throttles}/{ThrottleRetries}); trying again in {ThrottleRetryMs} ms.");
        Status = "Waiting for the market board";
        await DelayMs(ThrottleRetryMs);
        return !CancelToken.IsCancellationRequested;
    }

    // A visit left for another world ends quietly; the world loop says so in the chat.
    private MapSupplyAnswer GaveUpWaiting()
        => CancelToken.IsCancellationRequested ? MapSupplyAnswer.Cancelled
            : throttledOut ? MapSupplyAnswer.Failed(L.Market.FailThrottled)
            : Fail(L.Market.FailThrottled);

    // The lowest unit price wins, then the lower tax. Maps are unique, so a row of more than one cannot be bought.
    private bool TryPickCheapest(int count, int cap, long spendable, out MarketListing pick, out bool overBudget)
    {
        pick = default;
        overBudget = false;
        var found = false;
        for (var index = 0; index < count; index++)
        {
            var listing = listings[index];
            if (listing.Quantity != 1 || listing.UnitPrice > cap || passedOver.Contains(listing.ListingId)
                || refusedRetainers.Contains(listing.RetainerId) || MarketBoardOps.IsOwnRetainer(listing.RetainerId))
            {
                continue;
            }

            if (listing.Total > spendable)
            {
                overBudget = true;
                continue;
            }

            if (found && (listing.UnitPrice > pick.UnitPrice || (listing.UnitPrice == pick.UnitPrice && listing.TotalTax >= pick.TotalTax)))
            {
                continue;
            }

            pick = listing;
            found = true;
        }

        return found;
    }

    private async Task<Attempt> Buy(Configuration configuration, TreasureTier tier, MarketListing listing)
    {
        Status = $"Buying {tier.Name}";
        await DelayMs(Random.Shared.Next(ReadPauseMinMs, ReadPauseMaxMs + 1));
        if (CancelToken.IsCancellationRequested)
        {
            return Attempt.Cancelled;
        }

        var itemId = tier.TimewornItemId;
        if (!MarketBoardOps.SearchOpen() || !MarketBoardOps.StillListed(listing, itemId))
        {
            Diag($"Market: the board closed, or listing {listing.ListingId:X} changed or left it, before the purchase; reading the listings again.");
            return Attempt.Stale;
        }

        // Gil can move during the pause, so the budget and the gil to keep are held to once more as the purchase goes out.
        var spendable = MarketRules.Spendable(configuration, session);
        if (listing.Total > spendable)
        {
            Diag($"Market: listing {listing.ListingId:X} at {listing.Total} gil with tax no longer fits the {spendable} gil that may be spent; reading the listings again.");
            return Attempt.Stale;
        }

        var catalog = Plugin.Instance.Catalog;
        var gilBefore = MarketBoardOps.Gil();
        var heldBefore = MapsHeld(catalog, tier);
        var sequence = Answers.Purchase.Sequence;
        Diag($"Market: buying {tier.Name}, listing {listing.ListingId:X} of retainer {listing.RetainerId:X} at {listing.UnitPrice} gil plus {listing.TotalTax} tax, with {gilBefore} gil and {heldBefore} held.");
        sent = new SentPurchase(tier, listing, gilBefore, heldBefore, sequence);
        if (!MarketBoardOps.Purchase(listing, itemId))
        {
            Warn("Market: the game did not take the purchase; whether it went out is left to its answer and the bags.");
            return await SettleUnanswered(catalog);
        }

        if (!await WaitUntilTimed(() => Answers.Purchase.Sequence != sequence, PurchaseAnswerTimeoutMs, "market purchase answer", PollFrames))
        {
            return await SettleUnanswered(catalog);
        }

        return await TakeAnswer(catalog);
    }

    private async Task<Attempt> TakeAnswer(TreasureCatalog catalog)
    {
        var purchase = sent.GetValueOrDefault();
        var answer = Answers.Purchase;
        var result = MarketReplies.Classify(answer.MessageId);
        Diag($"Market: the game answered the purchase with message {answer.MessageId} for item {answer.ItemId}, read as {result}.");
        if (result != PurchaseResult.Bought)
        {
            sent = null;
            return ToAttempt(result);
        }

        await WaitUntilTimed(() => MapsHeld(catalog, purchase.Tier) > purchase.HeldBefore && MarketBoardOps.Gil() < purchase.GilBefore,
            PurchaseLandTimeoutMs, "market purchase landed", PollFrames);
        // The game's word is what charges the gil, whether or not the map shows in the bags yet.
        RecordPurchase();
        if (MapsHeld(catalog, purchase.Tier) > purchase.HeldBefore)
        {
            return Attempt.Bought;
        }

        if (CancelToken.IsCancellationRequested)
        {
            return Attempt.Cancelled;
        }

        Warn($"Market: the game accepted the purchase of {purchase.Tier.Name}, but no map reached the bags.");
        return Attempt.NotLanded;
    }

    // A purchase is never sent again blind: a late answer or the bags tell whether the unanswered one went through, and
    // with neither the visit ends, so a late success cannot meet a second purchase.
    private async Task<Attempt> SettleUnanswered(TreasureCatalog catalog)
    {
        var purchase = sent.GetValueOrDefault();
        Diag("Market: the purchase got no answer yet; watching for one and checking the bags.");
        await WaitUntilTimed(() => Answers.Purchase.Sequence != purchase.Sequence || MapsHeld(catalog, purchase.Tier) > purchase.HeldBefore,
            UnansweredSettleMs, "market purchase settled", PollFrames);
        if (Answers.Purchase.Sequence != purchase.Sequence)
        {
            return await TakeAnswer(catalog);
        }

        if (MapsHeld(catalog, purchase.Tier) > purchase.HeldBefore)
        {
            RecordPurchase();
            return Attempt.Bought;
        }

        if (CancelToken.IsCancellationRequested)
        {
            return Attempt.Cancelled;
        }

        sent = null;
        Warn("Market: the purchase got no answer and no map reached the bags; this visit sends no other.");
        return Attempt.Unanswered;
    }

    private void RecordPurchase()
    {
        if (sent is not { } purchase)
        {
            return;
        }

        sent = null;
        var spent = Charge(purchase);
        Diag($"Market: bought {purchase.Tier.Name} for {spent} gil (listed at {purchase.Listing.Total} with tax).");
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Market.ChatBought, purchase.Tier.Name, spent.ToString("N0", Loc.Culture))}");
    }

    // A stop, a pause or an error can cut a purchase off between its send and the end of its checks. Unless the game is
    // known to have refused it, its gil is charged, so a resumed run cannot spend past its budget.
    private void SettleCutOff()
    {
        if (sent is not { } purchase || hooks is null)
        {
            return;
        }

        sent = null;
        var answer = hooks.Purchase;
        var answered = answer.Sequence != purchase.Sequence;
        if (answered && MarketReplies.Classify(answer.MessageId) != PurchaseResult.Bought)
        {
            return;
        }

        var spent = Charge(purchase);
        Diag($"Market: the visit was cut off with the purchase of {purchase.Tier.Name} {(answered ? "accepted" : "unanswered")}; {spent} gil charged to the run.");
    }

    private int Charge(in SentPurchase purchase)
    {
        var dropped = (long)purchase.GilBefore - MarketBoardOps.Gil();
        // Other gil can move in the same moment, so the drop is held to the listed total; no drop seen yet charges the total.
        var spent = (int)(dropped > 0 ? Math.Min(dropped, purchase.Listing.Total) : purchase.Listing.Total);
        session?.RecordPurchase(spent);
        return spent;
    }

    private MapSupplyAnswer OutOfGil()
    {
        if (session is null)
        {
            return Fail(L.Market.FailNotEnoughGil);
        }

        session.StopMarket();
        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Market.ChatStoppedGil)}");
        return MapSupplyAnswer.Empty(L.Market.ReasonStopped);
    }

    private void ReportNothingToBuy(TreasureTier tier, int cap, int count, bool overBudget)
    {
        Diag($"Market: nothing to buy for {tier.Name} from {count} usable listing(s) under a cap of {cap} gil (over the budget or the gil to keep: {overBudget}).");
        var line = overBudget
            ? Loc.T(L.Market.ChatOverBudget, tier.Name)
            : Loc.T(L.Market.ChatNoneUnderCap, tier.Name, cap.ToString("N0", Loc.Culture));
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {line}");
    }

    private async Task<bool> OpenBoard(CityBoard board)
    {
        if (MarketBoardOps.SearchOpen())
        {
            return true;
        }

        if (!BoardNearby())
        {
            var city = TerritoryNames.Of(board.TerritoryId);
            Status = $"Travelling to the market board in {city}";
            Diag($"Market: heading to the board in {city} ({board.TerritoryId}).");
            if (!await TravelTo(board.TerritoryId, board.Position, BoardArrivalMeters))
            {
                Warn($"Market: could not reach the board in {city}.");
                return false;
            }
        }

        return await InteractWithBoard();
    }

    // The game's own reaction to some refusals can close the search window, so it is opened again before a request.
    private async Task<bool> EnsureBoardOpen()
    {
        if (MarketBoardOps.SearchOpen())
        {
            return true;
        }

        if (boardReopens >= BoardReopens)
        {
            return false;
        }

        boardReopens++;
        Diag("Market: the board's window is closed; opening it again.");
        return await InteractWithBoard();
    }

    private async Task<bool> InteractWithBoard()
    {
        Status = "Opening the market board";
        for (var attempt = 1; attempt <= BoardInteractAttempts; attempt++)
        {
            var scope = $"{Scope}-board#{attempt}";
            var target = await ApproachObject(MarketBoardOps.FindNearestBoard, "the market board", scope);
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            if (target is null || !await ReadyToInteract(scope))
            {
                continue;
            }

            if (await InteractWithObject(target, MarketBoardOps.SearchOpen, scope))
            {
                Diag("Market: the board is open.");
                return true;
            }
        }

        return false;
    }

    private async Task CloseBoard()
    {
        if (!MarketBoardOps.CloseNextWindow())
        {
            return;
        }

        if (await WaitUntilTimed(static () => !MarketBoardOps.CloseNextWindow(), CloseTimeoutMs, "market windows closed", PollFrames))
        {
            return;
        }

        MarketBoardOps.CloseAll();
        Warn("Market: a board window stayed open after its own close; closed it directly.");
    }

    // For a stop and an error, which cannot wait out the close throttle. A stop runs it inside its Cancel, where a throw
    // would cut the stop short.
    private void CloseBoardNow()
    {
        try
        {
            MarketBoardOps.CloseAll();
        }
        catch (Exception exception)
        {
            Warn($"Market: closing the board's windows at once threw: {exception.Message}");
        }
    }

    private static MapSupplyAnswer Fail(LocString reason)
    {
        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Market.ChatFailed, Loc.T(reason))}");
        return MapSupplyAnswer.Failed(reason);
    }

    private static bool BoardNearby()
        => MarketBoardOps.FindNearestBoard() is { } board
        && Svc.Objects.LocalPlayer is { } player
        && Vector3.Distance(player.Position, board.Position) <= NearbyBoardMeters;

    private static int MapsHeld(TreasureCatalog catalog, in TreasureTier tier)
        => MapBag.TryFind(catalog, tier.Rank, out var map) ? map.Count : 0;

    private static Attempt ToAttempt(PurchaseResult result) => result switch
    {
        PurchaseResult.Throttled => Attempt.Throttled,
        PurchaseResult.UniqueHeld => Attempt.UniqueHeld,
        PurchaseResult.BagFull => Attempt.BagFull,
        PurchaseResult.NotEnoughGil => Attempt.NotEnoughGil,
        PurchaseResult.OwnRetainer => Attempt.OwnRetainer,
        _ => Attempt.ListingGone,
    };
}
