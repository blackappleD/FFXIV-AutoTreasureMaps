using AutoTreasureMaps.Core.Localization;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    protected const int AfterRunReadyWaitMs = 20_000;
    // Some plugin commands misbehave when several arrive in the same frame, so the player's commands are spaced out.
    private const int PostRunCommandSpacingMs = 500;

    // A mob still on the character keeps it in combat, where neither a chat command, the logout nor a clean exit goes through.
    private protected async Task<bool> WaitUntilSafeToFinish(string scope)
    {
        await FightOffAttackers(scope);
        return await WaitUntilTimed(IsSafeToFinish, AfterRunReadyWaitMs, $"{scope}-ready");
    }

    // Each command is awaited in turn so a logout or a closed game that follows cannot cut the last ones off.
    protected async Task SendPostRunCommands(string commandBlock)
    {
        if (string.IsNullOrWhiteSpace(commandBlock))
        {
            return;
        }

        Status = Loc.T(L.Status.SendingChatCommands);
        var lines = commandBlock.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            var command = lines[lineIndex].Trim();
            if (command.Length == 0)
            {
                continue;
            }

            if (!command.StartsWith('/'))
            {
                Warn($"Post-run line skipped, commands must start with '/': {command}");
                continue;
            }

            SendPostRunCommand(command);
            await DelayMs(PostRunCommandSpacingMs);
        }
    }

    private void SendPostRunCommand(string command)
    {
        try
        {
            Diag($"Post-run command: {command}");
            Chat.ExecuteCommand(command);
        }
        catch (Exception exception)
        {
            RunLog.Error($"Post-run command '{command}' failed: {exception.Message}");
        }
    }

    private static bool IsSafeToFinish()
        => Svc.Objects.LocalPlayer is not null
        && !Svc.Condition[ConditionFlag.InCombat]
        && !Svc.Condition[ConditionFlag.BetweenAreas]
        && !Svc.Condition[ConditionFlag.Casting];
}
