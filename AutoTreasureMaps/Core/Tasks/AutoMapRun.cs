using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Treasure;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

// Runs the held map once, from /atm dig or the Current map card. A fresh task is built for every resume and fault
// restart, and the session's map tells it where the hunt stood.
internal sealed class AutoMapRun(TreasureSession session, TreasureProgress progress) : AutoCommon
{
    public static void Start(Plugin plugin)
    {
        var refusal = MapRunReadiness.Blocker(plugin);
        if (refusal != MapRefusal.None)
        {
            RunLog.Info($"Map run refused: {refusal}.");
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.MapRun.ChatRefused, Loc.T(MapOutcomes.Reason(refusal)))}");
            return;
        }

        plugin.Controller.StartHeldMap();
    }

    protected override async Task Execute()
    {
        try
        {
            // A pause or a fault restart inside the dungeon the map's portal led to picks that dungeon up first.
            if (await PickUpDungeon(session, progress) is { } dungeonStep)
            {
                if (dungeonStep != PortalStep.Stop)
                {
                    session.Finish(RunEnd.SingleMap);
                }

                return;
            }

            var result = await RunHeldMap(session, progress);
            if (MapFault is { } fault)
            {
                session.RecordFault(fault, CancelToken);
                return;
            }

            if ((result.PortalSeen || result.Refusal == MapRefusal.PortalDug) && result.Outcome != MapOutcome.Cancelled
                && await EnterPortalAndClear(session, progress) == PortalStep.Stop)
            {
                return;
            }

            if (MapOutcomes.Final(result.Outcome))
            {
                session.Finish(result.Outcome == MapOutcome.Refused ? RunEnd.Refused : RunEnd.SingleMap, result.Refusal);
            }
        }
        catch (Exception exception)
        {
            session.RecordFault(exception, CancelToken);
            throw;
        }
        finally
        {
            ReleaseCombatMovement("map");
        }
    }
}
