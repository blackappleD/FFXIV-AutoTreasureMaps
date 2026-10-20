using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Market;
using AutoTreasureMaps.Core.Travel;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

// A run that bought its maps on another world travels back to the world it started on once it ends, so the character is
// not left as a visitor after a stop or a run that stays logged in.
public sealed class AutoReturnToWorld(uint worldId) : AutoCommon
{
    private readonly uint worldId = worldId;

    protected override async Task Execute()
    {
        if (Worlds.Current == worldId)
        {
            return;
        }

        var name = Worlds.NameOf(worldId);
        if (!LifestreamIPC.Instance.CanVisit(name))
        {
            Warn($"Run end: Lifestream cannot visit {name} from here; staying on {Worlds.NameOf(Worlds.Current)}.");
            return;
        }

        Diag($"Run end: travelling back to {name}, the world the run started on.");
        await HoldCombatMovementAndSettle("world-back");
        try
        {
            var gateway = MarketBoards.Resolve(Plugin.Instance.Configuration.MarketBoard).AetheryteId;
            if (!await ChangeWorld(worldId, "world-back", gateway) && !CancelToken.IsCancellationRequested)
            {
                Warn($"Run end: could not travel back to {name}; staying on {Worlds.NameOf(Worlds.Current)}.");
            }
        }
        finally
        {
            ReleaseCombatMovement("world-back");
        }
    }
}
