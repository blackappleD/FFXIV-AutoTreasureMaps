using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Game;
using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Game.Watchers;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Core.Treasure;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    // "Enter the treasure vault?", the question the portal asks before it carries the party in.
    private const uint PortalPromptAddonRow = 8116;
    private const int PortalAppearWaitMs = 8_000;
    private const int PortalEnterAttempts = 3;
    private const int PortalAnswerMs = 5_000;
    private const int PortalZoneWaitMs = 30_000;
    private const int DungeonSettleNavmeshMs = 30_000;
    // A dungeon the run does not know, or hands back, is the player's; the run waits this long for them to leave it.
    private const int HandedBackWaitMs = 1_800_000;
    private const int HandedBackPollMs = 1_000;
    // The portal does not wait for ever, so the coffer's loot gets only this long before the walk in; the rest is
    // discarded after the dungeon.
    private const int PortalCleanupWaitMs = 30_000;

    private static string[]? portalPromptFragments;

    private protected enum PortalStep : byte { Cleared, Skipped, Stop }

    private static string[] PortalPromptFragments
        => portalPromptFragments ??= Svc.Data.GetExcelSheet<Addon>().GetRowOrDefault(PortalPromptAddonRow) is { } row ? GameText.FixedFragments(row.Text) : [];

    // Enters the portal the last map left behind and clears the dungeon behind it. Stop when the run must end here: a
    // stop, a pause, or a fault already handed to the session.
    private protected async Task<PortalStep> EnterPortalAndClear(TreasureSession session, TreasureProgress progress)
    {
        if (!Plugin.Instance.Configuration.DungeonEnterPortal)
        {
            return PortalStep.Skipped;
        }

        if (Svc.Party.Length > 0)
        {
            Diag("Portal: in a party; the dungeon is left to the party.");
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatPortalInParty)}");
            return PortalStep.Skipped;
        }

        // The dungeon gives a lot more loot, so the coffer's is cleared out of the bags first.
        await DiscardNotedLoot(PortalCleanupWaitMs, "Portal");
        if (CancelToken.IsCancellationRequested)
        {
            return PortalStep.Stop;
        }

        if (!await EnterPortal())
        {
            return CancelToken.IsCancellationRequested ? PortalStep.Stop : PortalStep.Skipped;
        }

        return await ClearDungeonHere(session, progress);
    }

    // Clears the treasure dungeon the character stands in: a known one through its handler, an unknown one left to the
    // player until they come out.
    private protected async Task<PortalStep> ClearDungeonHere(TreasureSession session, TreasureProgress progress)
    {
        await WaitUntilTimed(() => !Zoning(), PortalZoneWaitMs, "dungeon settled");
        await WaitForNavmeshReady(DungeonSettleNavmeshMs);
        if (DungeonRegistry.Find(Svc.ClientState.TerritoryType) is not { } handler)
        {
            Warn($"Portal: territory {Svc.ClientState.TerritoryType} ({TerritoryNames.Of(Svc.ClientState.TerritoryType)}) is no treasure dungeon the run knows.");
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatUnknownDungeon)}");
            return await WaitForPlayerToLeave();
        }

        progress.SetPhase(TreasurePhase.Fighting);
        var result = await RunDungeon(session, progress, handler);
        if (DungeonFault is { } fault)
        {
            session.RecordFault(fault, CancelToken);
            return PortalStep.Stop;
        }

        if (result.Outcome == DungeonOutcome.Cancelled || CancelToken.IsCancellationRequested)
        {
            return PortalStep.Stop;
        }

        return DutyWatcher.InContent() ? await WaitForPlayerToLeave() : PortalStep.Cleared;
    }

    private async Task<bool> EnterPortal()
    {
        const string scope = "portal";
        var startTerritory = Svc.ClientState.TerritoryType;
        Status = "Entering the portal";
        await WaitUntilTimed(() => TreasurePortals.FindNear(PlayerPosition(), PortalSearchMeters) is not null, PortalAppearWaitMs, "portal object");
        DutyWatcher.HoldForDungeonEntry();
        for (var attempt = 1; attempt <= PortalEnterAttempts && !CancelToken.IsCancellationRequested; attempt++)
        {
            await FightOffAttackers(scope);
            var portal = await ApproachObject(() => TreasurePortals.FindNear(PlayerPosition(), PortalSearchMeters), "the portal", scope);
            if (portal is null)
            {
                Warn($"{scope}: no portal within {PortalSearchMeters:F0}m to enter.");
                return false;
            }

            if (!await ReadyToInteract(scope) || !await HumanDelay(HumanAction.OpenCoffer, scope))
            {
                continue;
            }

            if (attempt == 1)
            {
                Diag($"{scope}: entering portal {portal.BaseId} at {FormatPosition(portal.Position)}.");
                Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Dungeon.ChatEnteringPortal)}");
            }

            await InteractWithObject(portal, () => NpcInteraction.SelectYesnoOpen() || Zoning() || Svc.ClientState.TerritoryType != startTerritory, scope);
            if (NpcInteraction.SelectYesnoOpen())
            {
                var prompt = NpcInteraction.SelectYesnoText();
                if (!PortalPrompt(prompt))
                {
                    Warn($"{scope}: the portal asks '{prompt}', which is not its known question; leaving it to you.");
                    return false;
                }

                Diag($"{scope}: the portal asks '{prompt}'; answering Yes.");
                DialogDriver.Confirm();
            }

            await WaitUntilTimed(() => Zoning() || Svc.ClientState.TerritoryType != startTerritory, PortalAnswerMs, "portal answer");
            if (await WaitUntilTimed(() => !Zoning() && Svc.ClientState.TerritoryType != startTerritory, PortalZoneWaitMs, "into the dungeon"))
            {
                Diag($"{scope}: in {TerritoryNames.Of(Svc.ClientState.TerritoryType)} ({Svc.ClientState.TerritoryType}).");
                return true;
            }
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn($"{scope}: did not get into the dungeon after {PortalEnterAttempts} attempts.");
        }

        return false;
    }

    private async Task<PortalStep> WaitForPlayerToLeave()
    {
        Status = "The dungeon is up to you; the run waits outside";
        var deadline = Environment.TickCount64 + HandedBackWaitMs;
        while (DutyWatcher.InContent() && Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return PortalStep.Stop;
            }

            await DelayMs(HandedBackPollMs);
        }

        if (DutyWatcher.InContent())
        {
            Warn("Portal: still in the dungeon after the wait; the run ends here.");
            return PortalStep.Stop;
        }

        await WaitUntilTimed(() => !Zoning() && NpcInteraction.PlayerReady(), PortalZoneWaitMs, "back outside");
        return PortalStep.Cleared;
    }

    private static bool PortalPrompt(string prompt)
    {
        var fragments = PortalPromptFragments;
        if (fragments.Length == 0)
        {
            return false;
        }

        var folded = GameText.Fold(prompt);
        for (var index = 0; index < fragments.Length; index++)
        {
            if (!GameText.Contains(folded, fragments[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Zoning() => Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];

    private static System.Numerics.Vector3 PlayerPosition() => Svc.Objects.LocalPlayer?.Position ?? default;
}
