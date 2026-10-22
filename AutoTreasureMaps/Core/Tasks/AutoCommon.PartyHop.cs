using AutoTreasureMaps.Core.Game;
using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using ECommons.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    // The party chat line goes out before the party breaks up, so the members read it while they can.
    private const int HopMessageLeadMs = 1_500;
    private const int DisbandPromptWaitMs = 3_000;
    private const int DisbandWaitMs = 10_000;
    private const string DisbandCommand = "/partycmd breakup";
    private const string LeaveCommand = "/leave";
    // The members travel while the leader buys, so they are usually there first; a member that never comes is left.
    private const int RegroupWaitMs = 5 * TimeUnits.MillisecondsPerMinute;
    private const int ReinviteEveryMs = 30_000;

    // A party on its own world cannot visit another, so before the leader buys elsewhere it tells the party where in
    // party chat and breaks it up; every member travels there too. True when the run may go: solo, or the party is gone.
    private protected async Task<bool> TakePartyAlong(uint worldId)
    {
        if (!LeadsPartyMode())
        {
            return true;
        }

        var name = Worlds.NameOf(worldId);
        PartyHop.RememberPartners();
        Diag($"Party: taking {PartyHop.Partners.Count} member(s) to {name}; telling the party and breaking it up.");
        Chat.SendMessage($"/p {PartyHop.Message(name)}");
        await DelayMs(HopMessageLeadMs);
        if (!await BreakUpParty(DisbandCommand) && !await BreakUpParty(LeaveCommand))
        {
            Warn("Party: the party could not be broken up, so the run buys on this world.");
            PartyHop.ForgetPartners();
            return false;
        }

        return !CancelToken.IsCancellationRequested;
    }

    // The command asks to confirm; that question is answered Yes and its text logged.
    private async Task<bool> BreakUpParty(string command)
    {
        Chat.SendMessage(command);
        if (await WaitUntilTimed(() => NpcInteraction.SelectYesnoOpen() || !PartyPresence.InParty(), DisbandPromptWaitMs, "party break-up prompt")
            && NpcInteraction.SelectYesnoOpen())
        {
            Diag($"Party: '{command}' asks '{NpcInteraction.SelectYesnoText()}'; answering Yes.");
            DialogDriver.Confirm();
        }

        var gone = await WaitUntilTimed(() => !PartyPresence.InParty(), DisbandWaitMs, "party broken up");
        Diag($"Party: after '{command}' the party is {(gone ? "gone" : "still there")}.");
        return gone;
    }

    // Once the leader has bought on the other world, it invites the members back there before the next map, asking
    // again every half minute; a member that never joins is left behind with a warning.
    private protected async Task RegroupParty()
    {
        if (!PartyHop.Away)
        {
            return;
        }

        var partners = PartyHop.Partners;
        Status = Loc.T(L.Status.InvitingPartyBack);
        Diag($"Party: inviting {partners.Count} member(s) back on {Worlds.NameOf(Worlds.Current)}.");
        var startedAt = Environment.TickCount64;
        var nextInviteAt = 0L;
        bool AllBack() => Svc.Party.Length > partners.Count;
        while (!CancelToken.IsCancellationRequested && !AllBack() && Environment.TickCount64 - startedAt < RegroupWaitMs)
        {
            if (Environment.TickCount64 >= nextInviteAt)
            {
                nextInviteAt = Environment.TickCount64 + ReinviteEveryMs;
                InviteBack(partners);
            }

            await NextFrame(30);
        }

        PartyHop.ForgetPartners();
        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        if (!AllBack())
        {
            Warn($"Party: the members did not join again within {RegroupWaitMs / TimeUnits.MillisecondsPerMinute} minutes; going on alone.");
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatRegroupFailed)}");
            return;
        }

        var crossWorld = InfoProxyCrossRealm.IsCrossRealmParty();
        Diag($"Party: the members joined again after {(Environment.TickCount64 - startedAt) / 1000.0:F1}s; the party is {(crossWorld ? "a cross-world party" : "on this world")}.");
        if (crossWorld)
        {
            Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatRegroupCrossWorld)}");
        }
    }

    private void InviteBack(IReadOnlyList<PartyMate> partners)
    {
        unsafe
        {
            var invite = InfoProxyPartyInvite.Instance();
            if (invite is null)
            {
                return;
            }

            foreach (var mate in partners)
            {
                if (IsInParty(mate))
                {
                    continue;
                }

                var sent = invite->InviteToParty(mate.ContentId, mate.Name, mate.HomeWorldId);
                Diag($"Party: invited {mate.Name}@{Worlds.NameOf(mate.HomeWorldId)} ({(sent ? "sent" : "not sent")}).");
            }
        }
    }

    private static bool IsInParty(PartyMate mate)
    {
        var party = Svc.Party;
        for (var index = 0; index < party.Length; index++)
        {
            if (party[index] is { } member && (ulong)member.ContentId == mate.ContentId)
            {
                return true;
            }
        }

        return false;
    }
}
