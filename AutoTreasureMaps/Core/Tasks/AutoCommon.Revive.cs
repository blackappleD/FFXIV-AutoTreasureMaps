using AutoTreasureMaps.Core.Ipc;
using AutoTreasureMaps.Core.Localization;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int ReviveCommand = (int)clib.Enums.CommandFlag.Revive;
    private const int ReviveReturn = (int)clib.Enums.AgentReviveOp.Return;
    private const int ReviveAccept = (int)clib.Enums.AgentReviveOp.AcceptRevive;
    private const int RaiseWaitMs = 30_000;
    private const int RaisePollMs = 1_000;
    private const int ReviveTransitionMs = 60_000;
    // The revive window ignores input for a moment after the knockout, so Return is re-sent until the trip home starts.
    private const int ReturnReissueMs = 1_500;
    private const int RevivePollMs = 250;
    private const int WeaknessSettleMs = 1_000;

    // Solo, it answers the return prompt straight away; in a party it first gives a raise a while to arrive. True once the
    // character stands again, wherever that is; the run travels back from there.
    private protected async Task<bool> RecoverFromKnockout()
    {
        BossModIPC.Instance.ClearActive();
        NavmeshIPC.Instance.Stop();
        if (!IsKnockedOut())
        {
            return true;
        }

        CurrentPhase = TreasurePhase.Travelling;
        var inParty = Svc.Party.Length > 0;
        if (inParty && await WaitForRaise())
        {
            Diag("Revive: raised where the character fell");
            await DelayMs(WeaknessSettleMs);
            return true;
        }

        if (CancelToken.IsCancellationRequested)
        {
            return false;
        }

        Status = Loc.T(L.Status.KnockedOutHome);
        Diag($"Revive: knocked out {(inParty ? "with no raise coming" : "while solo")}; answering the return prompt");
        SendReviveCommand(ReviveReturn);
        var deadline = Environment.TickCount64 + ReviveTransitionMs;
        var nextReturnAt = Environment.TickCount64 + ReturnReissueMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            var knockedOut = IsKnockedOut();
            var zoning = Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];
            if (!knockedOut && !zoning)
            {
                await DelayMs(WeaknessSettleMs);
                Diag($"Revive: back on its feet in territory {Svc.ClientState.TerritoryType}");
                return true;
            }

            if (knockedOut && !zoning && Environment.TickCount64 >= nextReturnAt)
            {
                SendReviveCommand(ReviveReturn);
                nextReturnAt = Environment.TickCount64 + ReturnReissueMs;
            }

            await DelayMs(RevivePollMs);
        }

        Warn($"Revive: the return to the home point did not complete within {ReviveTransitionMs / TimeUnits.MillisecondsPerSecond}s");
        return !IsKnockedOut();
    }

    // A knockout the revive cannot clear would fail every later map the same way, so the run stops instead.
    private protected async Task<bool> EnsureStanding()
    {
        if (!IsKnockedOut())
        {
            return true;
        }

        Diag("Run: the character is knocked out; bringing it back before going on");
        var standing = await RecoverFromKnockout();
        if (CancelToken.IsCancellationRequested)
        {
            return false;
        }

        if (standing)
        {
            return true;
        }

        Warn("Run: the character is still knocked out after the revive; stopping the run");
        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} The character could not get back on its feet, so the run stops.");
        return false;
    }

    private protected static bool IsKnockedOut() => Svc.Condition[ConditionFlag.Unconscious];

    private async Task<bool> WaitForRaise()
    {
        Status = Loc.T(L.Status.KnockedOutRaise);
        Diag($"Revive: knocked out in a party; waiting up to {RaiseWaitMs / TimeUnits.MillisecondsPerSecond}s for a raise");
        var deadline = Environment.TickCount64 + RaiseWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            if (!IsKnockedOut())
            {
                return true;
            }

            // Accepting does nothing unless a raise is on offer, so repeating it is harmless.
            SendReviveCommand(ReviveAccept);
            await DelayMs(RaisePollMs);
        }

        return !IsKnockedOut();
    }

    private void SendReviveCommand(int operation)
    {
        try
        {
            if (!GameMain.ExecuteCommand(ReviveCommand, operation))
            {
                Diag($"Revive: the revive command ({operation}) was not accepted");
            }
        }
        catch (Exception exception)
        {
            Warn($"Revive: the revive command ({operation}) failed: {exception.Message}");
        }
    }
}
