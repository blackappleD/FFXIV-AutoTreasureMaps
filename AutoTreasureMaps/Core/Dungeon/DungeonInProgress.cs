using AutoTreasureMaps.Core.Game.Watchers;
using System.Collections.Immutable;

namespace AutoTreasureMaps.Core.Dungeon;

// The dungeon under way. It lives on the session, so a pause or a fault restart builds a fresh run task that picks
// the dungeon up from here; whatever the field shows is read again either way.
internal sealed class DungeonInProgress(DungeonProfile profile)
{
    // Times a coffer was interacted with, by game object id, so one that never answers is given up on.
    private readonly Dictionary<ulong, int> cofferInteractions = [];

    // Coffers that already gave their loot. One stays targetable for a few seconds while it fades, and is not asked again.
    private readonly HashSet<ulong> lootedCoffers = [];

    // The special rounds met, by when each began; one walked again keeps its last count.
    private readonly SortedDictionary<long, SpecialRound> specialRounds = new();

    public DungeonProfile Profile { get; } = profile;

    public long StartedAtMs { get; } = Environment.TickCount64;

    // The zone entry this dungeon was cleared in; a later instance of the same dungeon starts afresh.
    public int ZoneEntry { get; } = DutyWatcher.ZoneEntry;

    public Dictionary<uint, int>? BagsBefore { get; set; }

    // Set when the run was paused inside: what the player did meanwhile would read as loot, so the cleanup leaves this
    // dungeon's loot alone.
    public bool CleanupBlocked { get; set; }

    // A gate dungeon counts chambers from 1, read from the game's own gate message; a slot dungeon counts the draws
    // made so far, from 0.
    public int Round { get; private set; } = profile.Kind == DungeonKind.Gate ? 1 : 0;

    // The last round the run had something to do in: a fight, a coffer, the loot, a bonus round or a lure. A draw that
    // only showed the way out moved Round on but reached nothing, so the record goes by this instead.
    public int RoundReached { get; private set; }

    // When the current chamber or draw began, for the messages that belong to it.
    public long RoundStartedAtMs { get; private set; } = Environment.TickCount64;

    public int CofferInteractionsThisRound { get; private set; }

    // The last time the run had something to do in this dungeon: a fight, a coffer, the loot, an advance.
    public long LastBusyAtMs { get; set; } = Environment.TickCount64;

    // A gate that answered it is locked is not tried again before this.
    public long AdvanceNotBeforeMs { get; set; }

    // The last chamber reached, or the last draw made; nothing is left to advance to.
    public bool FinalRound => Round >= Profile.Rounds;

    // Set once the run asked to leave, so the zone change that follows reads as a clear and not as an expulsion.
    public bool LeaveRequested { get; set; }

    public bool LureNoticeShown { get; set; }

    public bool BonusNoticeShown { get; set; }

    // Rolls the game did not take are not sent again before this, so a refused roll does not hold the run.
    public long LootNotBeforeMs { get; set; }

    // The way on to the current round, from the moment the advance opened it until the character has been carried there.
    public AdvanceOutcome? PendingTransit { get; set; }

    public int CofferInteractions(ulong cofferId) => cofferInteractions.GetValueOrDefault(cofferId);

    public void NoteCofferInteraction(ulong cofferId)
    {
        cofferInteractions[cofferId] = CofferInteractions(cofferId) + 1;
        CofferInteractionsThisRound++;
    }

    public bool CofferLooted(ulong cofferId) => lootedCoffers.Contains(cofferId);

    public void NoteCofferLooted(ulong cofferId) => lootedCoffers.Add(cofferId);

    public ImmutableArray<SpecialRound> SpecialRounds => [.. specialRounds.Values];

    public void NoteSpecialRound(long startedAtMs, SpecialRound round) => specialRounds[startedAtMs] = round;

    public void NoteRoundReached() => RoundReached = Math.Max(RoundReached, Round);

    // The machine drew its final notion, so no draw is left whatever the count says.
    public void MarkFinalRound() => Round = Math.Max(Round, Profile.Rounds);

    public void BeginRound(int round)
    {
        Round = round;
        RoundStartedAtMs = Environment.TickCount64;
        CofferInteractionsThisRound = 0;
        AdvanceNotBeforeMs = 0;
        LureNoticeShown = false;
    }
}
