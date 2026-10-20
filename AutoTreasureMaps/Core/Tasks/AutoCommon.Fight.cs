using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Gather;
using AutoTreasureMaps.Core.Ipc;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int FightTickFrames = 15;
    private const int AggroClearMs = 45_000;
    private const string PresetMovementModule = "BossMod.Autorotation.MiscAI.NormalMovement";
    private const string PresetMovementTrack = "Destination";
    private const string PresetMovementParked = "None";

    private bool combatPresetEnsured;
    private bool combatPresetRefusalLogged;

    // Fights back with the preset until the character is out of combat or capMs runs out. Every step that needs the
    // character free of combat runs this first: a mob that has aggroed keeps attacking a character that stands still, so
    // waiting never ends it. The preset's targeting only switches while the character has no target, and only to a mob on
    // the enemy list, so a target that is dead or holds no enmity is dropped to let it take an attacker.
    private protected async Task FightOffAttackers(string scope, int capMs = AggroClearMs)
    {
        if (!Svc.Condition[ConditionFlag.InCombat] || IsKnockedOut())
        {
            return;
        }

        // A Miner or Botanist has nothing to fight with, so the daily map gather leaves instead.
        if (MapGatherTable.IsGathererClassJob(GearsetSwitcher.CurrentClassJobId()))
        {
            Diag($"{scope}: in combat on a gatherer ({ConditionTag()}); not fighting");
            return;
        }

        EnsureCombatPreset();
        Status = "Fighting off what is still attacking";
        Diag($"{scope}: still in combat ({ConditionTag()}); fighting off whatever is attacking");
        var deadline = Environment.TickCount64 + capMs;
        ReleaseCombatMovement(scope);
        try
        {
            while (Svc.Condition[ConditionFlag.InCombat] && Environment.TickCount64 < deadline)
            {
                if (CancelToken.IsCancellationRequested || IsKnockedOut())
                {
                    return;
                }

                await KeepPresetFighting(scope);
                if (Svc.Targets.Target is { } target && (target.IsDead || !EnmityList.Contains(target.EntityId)))
                {
                    Svc.Targets.Target = null;
                }

                await NextFrame(FightTickFrames);
            }
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
            HoldCombatMovement(scope);
        }

        if (Svc.Condition[ConditionFlag.InCombat])
        {
            Diag($"{scope}: still in combat after {capMs / TimeUnits.MillisecondsPerSecond}s of fighting; carrying on");
        }
    }

    // One tick's upkeep of a fight: a mounted character cannot attack, and the preset is re-applied in case it dropped.
    private async Task KeepPresetFighting(string scope)
    {
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            BossModIPC.Instance.ClearActive();
            await SafeDismount($"{scope}-aggro-dismount");
        }

        AssertCombatPresetActive();
    }

    private void EnsureCombatPreset()
    {
        if (combatPresetEnsured)
        {
            return;
        }

        var configuration = Plugin.Instance.Configuration;
        var missing = BossModIPC.Instance.GetPreset(TreasureCombatPreset.Name) is null;
        var stale = configuration.BundledCombatPresetRevision < TreasureCombatPreset.Revision;
        if (!missing && !stale)
        {
            combatPresetEnsured = true;
            return;
        }

        Diag(missing
            ? $"Fight: creating the '{TreasureCombatPreset.Name}' combat preset"
            : $"Fight: the '{TreasureCombatPreset.Name}' preset is at revision {configuration.BundledCombatPresetRevision}, the bundled one is {TreasureCombatPreset.Revision}; overwriting it");
        if (!BossModIPC.Instance.CreatePreset(TreasureCombatPreset.GetSerialized(), overwrite: true))
        {
            Warn($"Fight: the combat plugin refused the '{TreasureCombatPreset.Name}' preset; the next fight tries again");
            return;
        }

        configuration.BundledCombatPresetRevision = TreasureCombatPreset.Revision;
        configuration.Save();
        combatPresetEnsured = true;
    }

    // Re-applied every tick because the combat plugin can drop its active preset whenever combat ends.
    private void AssertCombatPresetActive()
    {
        var bossMod = BossModIPC.Instance;
        if (bossMod.GetActive() == TreasureCombatPreset.Name)
        {
            return;
        }

        if (bossMod.SetActive(TreasureCombatPreset.Name))
        {
            combatPresetRefusalLogged = false;
            return;
        }

        if (combatPresetRefusalLogged)
        {
            return;
        }

        combatPresetRefusalLogged = true;
        Warn($"Fight: the combat plugin would not activate the '{TreasureCombatPreset.Name}' preset");
    }

    private async Task BounceCombatPreset()
    {
        BossModIPC.Instance.ClearActive();
        await NextFrame(2);
        AssertCombatPresetActive();
    }

    // Hands movement to the pathfinder without dropping the preset, so the rotation keeps attacking on the way in.
    private static bool ParkCombatPresetMovement()
    {
        var bossMod = BossModIPC.Instance;
        if (bossMod.CanClearTransientStrategy
            && bossMod.AddTransientStrategy(TreasureCombatPreset.Name, PresetMovementModule, PresetMovementTrack, PresetMovementParked))
        {
            return true;
        }

        bossMod.ClearActive();
        return false;
    }

    private void ResumeCombatPresetMovement()
    {
        if (BossModIPC.Instance.ClearTransientStrategy(TreasureCombatPreset.Name, PresetMovementModule, PresetMovementTrack))
        {
            return;
        }

        Diag("Fight: could not lift the movement override; re-applying the preset instead");
        BossModIPC.Instance.ClearActive();
    }
}
