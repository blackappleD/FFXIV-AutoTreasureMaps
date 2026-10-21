using AutoTreasureMaps.Core.Game.Ops;
using ECommons.Automation;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public sealed class AutoAfterRun(AfterRunAction action, string postRunCommands) : AutoCommon
{
    private const int ConfirmationWaitMs = 6_000;
    // The game drops a chat command issued in the middle of a transition.
    private const int PreCommandSettleMs = 800;
    // The second try covers a command eaten by lag or a window in the way.
    private const int LogoutAttempts = 2;
    private const string LogoutCommand = "/logout";
    private const string CloseGameCommand = "/xlkill";

    private readonly AfterRunAction action = action;
    private readonly string postRunCommands = postRunCommands;

    protected override async Task Execute()
    {
        try
        {
            await FinishRun();
        }
        finally
        {
            ReleaseCombatMovement("after-run");
        }
    }

    private async Task FinishRun()
    {
        if (!await WaitUntilSafeToFinish("after-run"))
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"After-run chat commands and {action} skipped: no safe moment (combat, casting, or a zone change) within {AfterRunReadyWaitMs / TimeUnits.MillisecondsPerSecond}s.");
            }

            return;
        }

        await SendPostRunCommands(postRunCommands);
        switch (action)
        {
            case AfterRunAction.Logout:
                await LogOut();
                break;
            case AfterRunAction.CloseGame:
                await CloseGame();
                break;
        }
    }

    private async Task LogOut()
    {
        Status = "Logging out";
        for (var attempt = 1; attempt <= LogoutAttempts; attempt++)
        {
            await DelayMs(PreCommandSettleMs);
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            Diag($"After-run: sending {LogoutCommand} (attempt {attempt}/{LogoutAttempts}).");
            Chat.ExecuteCommand(LogoutCommand);
            if (!await WaitUntilTimed(DialogDriver.ConfirmationOpen, ConfirmationWaitMs, $"logout-confirmation#{attempt}"))
            {
                continue;
            }

            if (DialogDriver.Confirm())
            {
                Diag("After-run: logout confirmation accepted.");
                return;
            }
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn($"After-run: the logout confirmation was not accepted after {LogoutAttempts} tries; the character may still be logged in.");
        }
    }

    private async Task CloseGame()
    {
        Status = "Closing the game";
        await DelayMs(PreCommandSettleMs);
        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        Diag($"After-run: closing the game with {CloseGameCommand}.");
        Chat.ExecuteCommand(CloseGameCommand);
    }
}
