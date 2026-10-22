using AutoTreasureMaps.Core.Tasks;
using Dalamud.Game.ClientState.Objects.Types;
using System.Numerics;
using System.Threading.Tasks;
using DungeonContext = AutoTreasureMaps.Core.Tasks.AutoCommon.DungeonContext;

namespace AutoTreasureMaps.Core.Dungeon;

internal enum AdvanceResult : byte
{
    // The way on opened (a gate) or the next draw began (a slot); NextRound is the round it leads to.
    NextRound,
    // The game sent the character out instead.
    Expelled,
    // Nothing to advance with yet, or the game said it is not time (a locked gate); the run looks again.
    NotReady,
    Failed,
    Cancelled,
}

// What an advance left behind for the transit: the object it used and where that stood, the event objects that stood
// before it, and when.
internal readonly record struct AdvanceOutcome(AdvanceResult Result, int NextRound, uint BaseId, Vector3 At, IReadOnlySet<ulong> ObjectsBefore, long SinceMs)
{
    private static readonly IReadOnlySet<ulong> None = new HashSet<ulong>();

    public static AdvanceOutcome Of(AdvanceResult result) => new(result, 0, 0, Vector3.Zero, None, 0);
}

// Moves the run from one round to the next: picks a gate, or draws from the machine.
internal interface IAdvanceStrategy
{
    Task<AdvanceOutcome> Run(DungeonContext context);
}

// Gets the character into the next round once the advance succeeded: into the circle, or simply waits in place.
// False when it could not; the run looks at the field again either way.
internal interface ITransitStrategy
{
    Task<bool> Run(DungeonContext context, AdvanceOutcome advance);
}

// One treasure dungeon: its data and the two strategies it advances with. The run calls every dungeon the same way, so
// a dungeon that differs somewhere overrides that step and keeps the shared one everywhere else.
internal abstract class TreasureDungeonHandler
{
    private const int CofferGraceMs = 10_000;

    public abstract DungeonProfile Profile { get; }

    protected abstract IAdvanceStrategy Advance { get; }

    protected abstract ITransitStrategy Transit { get; }

    public virtual Task OpenCoffer(DungeonContext context, IGameObject coffer) => context.OpenCoffer(coffer);

    public virtual Task Fight(DungeonContext context) => context.FightField();

    public virtual Task AnswerLure(DungeonContext context) => context.AnswerLure();

    public virtual Task SettleLoot(DungeonContext context) => context.SettleLoot();

    public virtual Task<bool> Leave(DungeonContext context) => context.LeaveDuty();

    public Task<AdvanceOutcome> AdvanceRound(DungeonContext context) => Advance.Run(context);

    public Task<bool> TransitRound(DungeonContext context, AdvanceOutcome advance) => Transit.Run(context, advance);

    // The round one line of the duty list names, for a member that has only the list to go by. Each dungeon words it its
    // own way, so a dungeon whose wording is known reads it; any other has nothing to read and its round is counted.
    public virtual bool TryReadListedRound(string line, out int round)
    {
        round = 0;
        return false;
    }

    // The advance is tried only while rounds remain; once the last is reached the run leaves when the field is quiet.
    public virtual bool MoreRounds(DungeonInProgress dungeon) => !dungeon.FinalRound;

    // Asked only once the field is quiet: nothing attacks, nothing waits on a roll and no coffer is left to open. A
    // round whose coffer has not been opened yet waits a while for it to show, so a late coffer is not skipped; the slot
    // room before its first draw has none to wait for.
    public virtual bool ReadyToAdvance(DungeonInProgress dungeon)
    {
        var now = Environment.TickCount64;
        if (now < dungeon.AdvanceNotBeforeMs)
        {
            return false;
        }

        var nothingToWaitFor = dungeon.CofferInteractionsThisRound > 0 || (Profile.Kind == DungeonKind.Slot && dungeon.Round == 0);
        return nothingToWaitFor || now - dungeon.LastBusyAtMs >= CofferGraceMs;
    }
}

internal sealed class CenoteJaJaGuralHandler : TreasureDungeonHandler
{
    public override DungeonProfile Profile => DungeonProfiles.CenoteJaJaGural;

    protected override IAdvanceStrategy Advance => GateAdvance.Instance;

    protected override ITransitStrategy Transit => WalkIntoCircleTransit.Instance;

    // The list names the chamber to break through: "突破第一区" to "突破第四区", then "突破最终区" for the fifth. Read
    // from the Chinese client; other clients' wording is not known yet, so their round is counted.
    public override bool TryReadListedRound(string line, out int round)
    {
        round = DutyListRound.ChineseChamber(line, Profile.Rounds);
        return round > 0;
    }
}

internal sealed class VaultOneironHandler : TreasureDungeonHandler
{
    public override DungeonProfile Profile => DungeonProfiles.VaultOneiron;

    protected override IAdvanceStrategy Advance => SlotAdvance.Instance;

    protected override ITransitStrategy Transit => StayInPlaceTransit.Instance;

    // The list counts the draws as "N/5" ("第2/5次挑战"); the bonus round's "N/100" and the clock do not match.
    public override bool TryReadListedRound(string line, out int round)
    {
        round = DutyListRound.CountOutOf(line, Profile.Rounds);
        return round > 0;
    }
}
