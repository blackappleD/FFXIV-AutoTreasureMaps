using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Travel;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    // A teleport to an aetheryte that can visit worlds, the visit's queue and its zone change.
    private const int WorldChangeTimeoutMs = 180_000;
    private const int WorldChangeNavmeshWaitMs = 30_000;

    // Visits another world in the character's data centre through Lifestream. True once the character stands on it.
    internal async Task<bool> ChangeWorld(uint worldId, string scope)
    {
        if (Worlds.Current == worldId)
        {
            return true;
        }

        var lifestream = LifestreamIPC.Instance;
        var name = Worlds.NameOf(worldId);
        if (!lifestream.IsAvailable)
        {
            Warn($"{scope}: Lifestream is not loaded, so the run cannot travel to {name}.");
            return false;
        }

        await PrepareForTeleport(scope);
        if (!await HumanDelay(HumanAction.Teleport, scope))
        {
            return false;
        }

        Status = $"Travelling to {name}";
        Diag($"{scope}: asking Lifestream to visit {name} ({worldId}) from {Worlds.NameOf(Worlds.Current)}.");
        lifestream.ChangeWorld(name);
        var arrived = await WaitUntilTimed(
            () => Worlds.Current == worldId && !lifestream.IsBusy() && !Svc.Condition[ConditionFlag.BetweenAreas] && !Svc.Condition[ConditionFlag.BetweenAreas51] && NpcInteraction.PlayerReady(),
            WorldChangeTimeoutMs,
            $"{scope}-world");
        if (!arrived)
        {
            lifestream.Abort();
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"{scope}: did not reach {name} within {WorldChangeTimeoutMs / TimeUnits.MillisecondsPerSecond}s; still on {Worlds.NameOf(Worlds.Current)}.");
            }

            return false;
        }

        Diag($"{scope}: on {name} now, in {TerritoryNames.Of(Svc.ClientState.TerritoryType)}.");
        await WaitForNavmeshReady(WorldChangeNavmeshWaitMs);
        return !CancelToken.IsCancellationRequested;
    }
}
