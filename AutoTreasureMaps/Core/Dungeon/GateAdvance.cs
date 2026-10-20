using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System.Threading.Tasks;
using DungeonContext = AutoTreasureMaps.Core.Tasks.AutoCommon.DungeonContext;

namespace AutoTreasureMaps.Core.Dungeon;

// Picks one of the chamber's gates by the dungeon's gate policy and opens it. The game answers with the gate to the
// next chamber opening or with a trap that sends the character out; every gate dungeon shares this.
internal sealed class GateAdvance : IAdvanceStrategy
{
    private const string Scope = "dungeon-gate";
    // A gate can take 25 seconds to open after the key is used, so the answer is waited on well past that.
    private const int GateAnswerMs = 45_000;
    private const int AnswerPollMs = 200;
    private const int LeftHint = 1;
    private const int RightHint = 2;
    private const int CentreHint = 3;

    public static readonly GateAdvance Instance = new();

    private GateAdvance()
    {
    }

    private static ReadOnlySpan<uint> Answers =>
    [
        DungeonLogMessages.GateOpenedOfSeven,
        DungeonLogMessages.GateOpenedOfFive,
        DungeonLogMessages.GateTrapExpelled,
        DungeonLogMessages.GateLocked,
        DungeonLogMessages.GateShut,
    ];

    public async Task<AdvanceOutcome> Run(DungeonContext context)
    {
        var gates = DungeonField.LeftToRight(DungeonField.AdvanceObjects(context.Profile));
        if (gates.Count == 0)
        {
            return AdvanceOutcome.Of(AdvanceResult.NotReady);
        }

        var index = Pick(context, gates, out var reason);
        var gate = gates[index];
        var gateId = gate.GameObjectId;
        var gateBaseId = gate.BaseId;
        var gatePosition = gate.Position;
        context.Diag($"{Scope}: {gates.Count} gate(s) left to right {Describe(gates)}; taking #{index + 1} ({reason}).");
        context.Status = "Opening a gate";

        var before = DungeonField.EventObjectIds();
        if (!await context.Delay(HumanAction.OpenCoffer, Scope))
        {
            return AdvanceOutcome.Of(AdvanceResult.Cancelled);
        }

        // Putting a hand on the gate is a cast; the gate answers once it ends, and any question it asks may come after.
        var since = TreasureMessageWatcher.Now;
        var promptBefore = context.PromptOpen();
        bool Taken() => Answered(context, since) || context.PromptOpen() || Svc.Condition[ConditionFlag.Casting] || context.MessageSince(DungeonLogMessages.GateHandPlaced, since);
        var interacted = await context.Interact(() => Svc.Objects.SearchById(gateId), $"gate {gateId:X}", Taken, Scope, raised: true);
        if (!interacted && !Taken())
        {
            context.Warn($"{Scope}: could not use gate {gateBaseId}.");
            return AdvanceOutcome.Of(AdvanceResult.Failed);
        }

        var deadline = Environment.TickCount64 + GateAnswerMs;
        while (!context.Cancelled && Environment.TickCount64 < deadline && !Answered(context, since) && !context.Zoning && context.InDungeon)
        {
            context.ConfirmPrompt(Scope, interacted: true, promptBefore);
            await context.DelayMs(AnswerPollMs);
        }

        if (context.Cancelled)
        {
            return AdvanceOutcome.Of(AdvanceResult.Cancelled);
        }

        if (context.TryGetMessage(DungeonLogMessages.GateOpened, since, out var opened))
        {
            var round = opened.TryGetParameter(0, out var chamber) && chamber > 0 ? chamber + 1 : context.Dungeon.Round + 1;
            context.Diag($"{Scope}: the gate opened ({DungeonLogMessages.NameOf(opened.Id)}), on to chamber {round}.");
            return new AdvanceOutcome(AdvanceResult.NextRound, round, gateBaseId, gatePosition, before, since);
        }

        if (context.MessageSince(DungeonLogMessages.GateTrapExpelled, since) || context.Zoning || !context.InDungeon)
        {
            context.Diag($"{Scope}: the gate was a trap; the game sends the character out.");
            return AdvanceOutcome.Of(AdvanceResult.Expelled);
        }

        if (context.MessageSince(DungeonLogMessages.GateLocked, since) || context.MessageSince(DungeonLogMessages.GateShut, since))
        {
            context.Diag($"{Scope}: the gate is not ready yet.");
            return AdvanceOutcome.Of(AdvanceResult.NotReady);
        }

        context.Warn($"{Scope}: no answer from the gate within {GateAnswerMs / TimeUnits.MillisecondsPerSecond}s (interacted {interacted}).");
        return AdvanceOutcome.Of(AdvanceResult.Failed);
    }

    private static bool Answered(DungeonContext context, long since) => context.TryGetMessage(Answers, since, out _);

    private static int Pick(DungeonContext context, List<IGameObject> gates, out string reason)
    {
        var configuration = context.Config;
        if (configuration.DungeonFollowGateHint
            && context.TryGetMessage(DungeonLogMessages.GateGlowHints, context.Dungeon.RoundStartedAtMs, out var hint)
            && hint.TryGetParameter(0, out var side)
            && HintedIndex(side, gates.Count) is { } hinted)
        {
            reason = $"the game lit gate {side}";
            return hinted;
        }

        var policy = configuration.GatePolicyFor(context.Profile.Key);
        reason = $"policy {policy}";
        return policy switch
        {
            GatePolicy.Left => 0,
            GatePolicy.Right => gates.Count - 1,
            _ => Random.Shared.Next(gates.Count),
        };
    }

    private static int? HintedIndex(int side, int count) => (side, count) switch
    {
        (LeftHint, _) => 0,
        (RightHint, _) => count - 1,
        (CentreHint, 3) => 1,
        _ => null,
    };

    private static string Describe(List<IGameObject> gates)
    {
        var parts = new string[gates.Count];
        for (var index = 0; index < gates.Count; index++)
        {
            var position = gates[index].Position;
            parts[index] = $"{gates[index].BaseId}@({position.X:F1}, {position.Z:F1})";
        }

        return string.Join(", ", parts);
    }
}
