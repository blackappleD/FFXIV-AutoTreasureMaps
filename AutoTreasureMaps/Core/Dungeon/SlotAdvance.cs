using AutoTreasureMaps.Core.Treasure;
using ECommons.DalamudServices;
using System.Threading.Tasks;
using DungeonContext = AutoTreasureMaps.Core.Tasks.AutoCommon.DungeonContext;

namespace AutoTreasureMaps.Core.Dungeon;

// Draws the next round from the room's slot or roulette machine. The draw's outcome (enemies, a bonus round, or the
// way out) comes after the spin, so the transit waits for it.
internal sealed class SlotAdvance : IAdvanceStrategy
{
    private const string Scope = "dungeon-slot";
    private const int SpinAnswerMs = 10_000;

    public static readonly SlotAdvance Instance = new();

    private SlotAdvance()
    {
    }

    private static ReadOnlySpan<uint> Spun => [DungeonLogMessages.SlotSpinning, DungeonLogMessages.SlotOperated];

    public async Task<AdvanceOutcome> Run(DungeonContext context)
    {
        var machines = DungeonField.AdvanceObjects(context.Profile);
        if (machines.Count == 0)
        {
            return AdvanceOutcome.Of(AdvanceResult.NotReady);
        }

        var machine = machines[0];
        var machineId = machine.GameObjectId;
        var machineBaseId = machine.BaseId;
        var position = machine.Position;
        context.Status = "Drawing the next round";
        if (!await context.Delay(HumanAction.OpenCoffer, Scope))
        {
            return AdvanceOutcome.Of(AdvanceResult.Cancelled);
        }

        var since = TreasureMessageWatcher.Now;
        var promptBefore = context.PromptOpen();
        var interacted = await context.Interact(() => Svc.Objects.SearchById(machineId), $"machine {machineId:X}", () => context.TryGetMessage(Spun, since, out _) || context.PromptOpen(), Scope);
        context.ConfirmPrompt(Scope, interacted, promptBefore);
        await context.WaitUntil(() => context.TryGetMessage(Spun, since, out _) || context.Zoning || !context.InDungeon, SpinAnswerMs, "slot spin");
        if (context.Cancelled)
        {
            return AdvanceOutcome.Of(AdvanceResult.Cancelled);
        }

        if (context.TryGetMessage(Spun, since, out var spun))
        {
            var round = context.Dungeon.Round + 1;
            context.Diag($"{Scope}: the machine answered {DungeonLogMessages.NameOf(spun.Id)}; draw {round} of {context.Profile.Rounds}.");
            return new AdvanceOutcome(AdvanceResult.NextRound, round, machineBaseId, position, DungeonField.EventObjectIds(), since);
        }

        if (context.Zoning || !context.InDungeon)
        {
            return AdvanceOutcome.Of(AdvanceResult.Expelled);
        }

        context.Warn($"{Scope}: the machine did not start within {SpinAnswerMs / TimeUnits.MillisecondsPerSecond}s (interacted {interacted}).");
        return AdvanceOutcome.Of(AdvanceResult.Failed);
    }
}
