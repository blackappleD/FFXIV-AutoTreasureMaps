using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Game.Player;
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

        if (PartyPresence.InParty() && !(PartyPresence.LocalParty() && PartyPresence.LeadsTheParty()))
        {
            RunLog.Info("Dungeon run refused: the character is in a party it does not lead.");
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
                session.ClearOwnDungeon();
                session.Finish(RunEnd.DungeonHandedBack);
                return;
            }

            // The player asked for this dungeon to be cleared, so it is the run's own work from here.
            session.MarkOwnDungeon();

            var result = await RunDungeon(session, progress, handler);
            if (DungeonFault is { } fault)
            {
                session.RecordFault(fault, CancelToken);
                return;
            }

            // Only a dungeon the run saw through ends as done; one handed back leaves the player inside, where an after-run
            // logout or trip to the inn must not follow.
            if (DungeonOutcomes.Final(result.Outcome))
            {
                session.ClearOwnDungeon();
                session.Finish(result.Outcome is DungeonOutcome.Cleared or DungeonOutcome.Expelled or DungeonOutcome.Left ? RunEnd.Dungeon : RunEnd.DungeonHandedBack);
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
