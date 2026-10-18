using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Localization;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

// Clears the treasure dungeon the character stands in, from /atm dungeon. A fresh task is built for every resume and
// fault restart, and the session's dungeon tells it where the run stood.
internal sealed class AutoDungeonRun(TreasureSession session, TreasureProgress progress) : AutoCommon
{
    // False, with the reason already in chat, when the run cannot start here.
    public static bool CanStart()
    {
        if (DungeonRegistry.Find(Svc.ClientState.TerritoryType) is null)
        {
            RunLog.Info($"Dungeon run refused: territory {Svc.ClientState.TerritoryType} is not a known treasure dungeon.");
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatNotInDungeon)}");
            return false;
        }

        if (Svc.Party.Length > 0)
        {
            RunLog.Info("Dungeon run refused: the character is in a party.");
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatInParty)}");
            return false;
        }

        return true;
    }

    protected override async Task Execute()
    {
        try
        {
            if (DungeonRegistry.Find(Svc.ClientState.TerritoryType) is not { } handler)
            {
                Diag("Dungeon run: no longer in a known treasure dungeon; nothing to resume.");
                session.ActiveDungeon = null;
                session.Finish(RunEnd.Dungeon);
                return;
            }

            var result = await RunDungeon(session, progress, handler);
            if (DungeonFault is { } fault)
            {
                session.RecordFault(fault, CancelToken);
                return;
            }

            if (DungeonOutcomes.Final(result.Outcome))
            {
                session.Finish(RunEnd.Dungeon);
            }
        }
        catch (Exception exception)
        {
            session.RecordFault(exception, CancelToken);
            throw;
        }
        finally
        {
            ReleaseCombatMovement("dungeon");
        }
    }
}
