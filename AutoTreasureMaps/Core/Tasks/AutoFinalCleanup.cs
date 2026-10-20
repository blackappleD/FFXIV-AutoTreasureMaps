using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

// The pass that discards the last maps' loot once a run meets its stop condition, run as a task of its own so the
// after-run action starts only once it is over.
public sealed class AutoFinalCleanup : AutoCommon
{
    private const int FinalCleanupWaitMs = 120_000;

    protected override Task Execute() => DiscardNotedLoot(FinalCleanupWaitMs, "Run end");
}
