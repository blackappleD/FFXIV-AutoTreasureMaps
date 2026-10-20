using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Market;
using AutoTreasureMaps.Core.Tasks;

namespace AutoTreasureMaps.Core.Supply;

// Buys the next map on the market board once the bags hold no enabled one. The map lands in the bags, so the run loops
// back and the bag step deciphers it.
internal sealed class MarketSupplyStep : IMapSupplyStep
{
    // A teleport and the walk to the board, three spaced listing requests per tier and three purchase tries, each with
    // its answer wait. A world visit, or a decipher and a second purchase for the map bought ahead, need the longer cap.
    public const int TaskTimeoutMs = 300_000;
    public const int ExtendedTaskTimeoutMs = 600_000;

    public string Name => "market";

    public TreasurePhase Phase => TreasurePhase.Buying;

    public int TimeoutMs => Plugin.Instance.Configuration is { MarketCrossWorld: false, MarketBuyAhead: false } ? TaskTimeoutMs : ExtendedTaskTimeoutMs;

    public bool SuppliesHeldMap => false;

    // A map bought without Decipher unlocked would only pile up in the bags, one more on every pass of the run.
    public MapSupplyCheck Check(Plugin plugin)
    {
        if (!DecipherOps.Unlocked())
        {
            return MapSupplyCheck.Unable(L.Decipher.NotUnlocked);
        }

        var check = MarketRules.Check(plugin, plugin.Controller.SessionSnapshot);
        if (check.Blocker == MarketBlocker.Off)
        {
            return MapSupplyCheck.NothingToGive(L.Market.ReasonOff);
        }

        return check.CanBuy ? MapSupplyCheck.Ready(check.Available) : MapSupplyCheck.Unable(MarketRules.Reason(check.Blocker));
    }

    public MapSupplyTask CreateTask() => new MarketBuyTask(Plugin.Instance.Controller.SessionSnapshot, MarketRules.AnyTier);
}
