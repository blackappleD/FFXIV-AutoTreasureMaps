using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Travel;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public sealed class AutoReturnToInn : AutoCommon
{
    private const float InnkeeperArrivalMeters = 3.5f;
    private const float InnkeeperReachMeters = 5f;
    private const float InnkeeperStepToleranceMeters = 2.5f;
    private const int EnterInnTimeoutMs = 60_000;
    private const int ApproachWatchdogMs = 25_000;
    private const int InteractWatchdogMs = 8_000;
    private const int DialogPollMs = 250;
    private const int InteractRetryMs = 500;

    protected override async Task Execute()
    {
        var inn = GrandCompanyInns.ForPlayer();
        var cityName = TerritoryNames.Of(inn.CityTerritoryId);
        Diag($"Return to inn: {cityName} ({inn.CityTerritoryId}), innkeeper {inn.InnkeeperBaseId}, inn room territory {inn.InnTerritoryId}.");
        if (Svc.ClientState.TerritoryType == inn.InnTerritoryId)
        {
            Diag("Return to inn: already inside the inn room.");
            return;
        }

        Svc.Chat.Print($"{AtmConstants.LogPrefix} Run complete. Retiring to the inn in {cityName}.");
        await HoldCombatMovementAndSettle("inn");
        try
        {
            // A visitor cannot take an inn room, and a market run may have left the character on another world. A home on
            // another data centre is out of reach for a same-DC visit, so the inn is skipped then.
            if (Worlds.AwayFromHome && !LifestreamIPC.Instance.CanVisit(Worlds.NameOf(Worlds.Home)))
            {
                Warn($"Return to inn: {Worlds.NameOf(Worlds.Home)} cannot be reached from here; staying where the run ended.");
                return;
            }

            if (Worlds.AwayFromHome && !await ChangeWorld(Worlds.Home, "inn-home", CityGateway(inn.CityTerritoryId)))
            {
                if (!CancelToken.IsCancellationRequested)
                {
                    Warn($"Return to inn: could not travel back to {Worlds.NameOf(Worlds.Home)}; staying where the run ended.");
                }

                return;
            }

            var arrived = await TravelTo(inn.CityTerritoryId, inn.InnkeeperPosition, InnkeeperArrivalMeters);
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            if (!arrived && Svc.ClientState.TerritoryType != inn.CityTerritoryId)
            {
                Warn($"Return to inn: could not reach {cityName}; staying where the run ended.");
                return;
            }

            if (Svc.Condition[ConditionFlag.Mounted])
            {
                await SafeDismount("inn-dismount");
            }

            await EnterInn(inn, cityName);
        }
        finally
        {
            ReleaseCombatMovement("inn");
        }
    }

    // The inn city's own aetheryte, so the visit home lands in the city the inn is in: Limsa 8, Gridania 2, Ul'dah 9.
    private static uint CityGateway(uint cityTerritoryId) => cityTerritoryId switch
    {
        128 or 129 => 8,
        132 or 133 => 2,
        130 or 131 => 9,
        _ => 0,
    };

    private async Task EnterInn(CityInn inn, string cityName)
    {
        Status = $"Entering the inn in {cityName}";
        var deadline = Environment.TickCount64 + EnterInnTimeoutMs;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (Svc.ClientState.TerritoryType == inn.InnTerritoryId)
            {
                Diag("Return to inn: reached the inn room.");
                return;
            }

            if (DialogDriver.AdvanceAccepting())
            {
                await DelayMs(DialogPollMs);
                continue;
            }

            if (FindByBaseId(inn.InnkeeperBaseId) is not { } innkeeper || Svc.Objects.LocalPlayer is not { } player)
            {
                await DelayMs(DialogPollMs);
                continue;
            }

            var innkeeperPosition = innkeeper.Position;
            var distance = Vector3.Distance(player.Position, innkeeperPosition);
            if (distance > InnkeeperReachMeters)
            {
                await ApproachInnkeeper(innkeeperPosition, distance);
                continue;
            }

            await TalkToInnkeeper(innkeeper);
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn("Return to inn: timed out before entering the inn room.");
        }
    }

    private async Task ApproachInnkeeper(Vector3 innkeeperPosition, float distance)
    {
        Diag($"Return to inn: walking up to the innkeeper, {distance:F1}m away.");
        var approach = new MoveOp(move => move.MoveInZone(innkeeperPosition, MovementConfig.Default.WithTolerance(InnkeeperStepToleranceMeters), null));
        await RunCancellable(approach, ApproachWatchdogMs, "inn-approach", StuckDetector.MoveStallAbort("inn-approach"));
        if (approach.Fault is { } fault)
        {
            Diag($"Return to inn: the walk to the innkeeper faulted: {fault.Message}; retrying");
        }
    }

    private async Task TalkToInnkeeper(IGameObject innkeeper)
    {
        Diag("Return to inn: talking to the innkeeper.");
        var interact = new MoveOp(move => move.Interact(innkeeper, DialogDriver.AnyOpen, UiSkipOptions.Talk));
        await RunCancellable(interact, InteractWatchdogMs, "inn-interact");
        if (interact.Fault is { } fault)
        {
            Diag($"Return to inn: talking to the innkeeper failed: {fault.Message}; retrying");
        }

        await DelayMs(InteractRetryMs);
    }

    private static IGameObject? FindByBaseId(uint baseId)
    {
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is { } candidate && candidate.BaseId == baseId)
            {
                return candidate;
            }
        }

        return null;
    }
}
