using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.External;
using AutoTreasureMaps.Core.Game.Ops;
using AutoTreasureMaps.Core.Game.Player;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Supply;
using ECommons.DalamudServices;

namespace AutoTreasureMaps.Core.Treasure;

internal enum RunBlocker : byte
{
    None,
    NotLoggedIn,
    NeedsPlugins,
    TaskRunning,
    NotCombatClass,
    HeldMap,
    NoMaps,
}

// Available counts the held map and every map the supply chain could put in hand after it. Reason is set whenever
// Blocker is. Follows is set when the run would follow the party leader, which needs no map of its own.
internal readonly record struct RunCheck(RunBlocker Blocker, LocString Reason, int Available, bool Follows = false)
{
    public bool CanStart => Blocker == RunBlocker.None;
}

// What stops a run from starting. Cheap enough for every frame: the held map and the bags are read once a frame, and
// the rest are flag and unlock reads. The caller checks first that no run is going.
internal static class RunReadiness
{
    private const int HeldMapCount = 1;

    public static RunCheck Check(Plugin plugin)
    {
        if (!Svc.ClientState.IsLoggedIn || Svc.Objects.LocalPlayer is null)
        {
            return new RunCheck(RunBlocker.NotLoggedIn, L.Decipher.NotLoggedIn, 0);
        }

        if (!ExternalPlugins.AllRequiredInstalled())
        {
            return new RunCheck(RunBlocker.NeedsPlugins, L.Treasure.DetailSetupNeeded, 0);
        }

        if (clib.Services.Svc.Automation.CurrentTask is not null)
        {
            return new RunCheck(RunBlocker.TaskRunning, L.Decipher.TaskRunning, 0);
        }

        if (!GearsetSwitcher.OnCombatClass())
        {
            return new RunCheck(RunBlocker.NotCombatClass, L.MapRun.RefusedNotCombatClass, 0);
        }

        // A party member follows its leader, which runs the maps, so it may start, or start again after a stop, anywhere
        // and with no map of its own.
        if (FollowsLeader())
        {
            return new RunCheck(RunBlocker.None, default, 0, Follows: true);
        }

        var supply = MapSupplyChain.Check(plugin);

        // A run started inside a treasure dungeon clears it first, so it needs no map to start; the maps only count
        // for what comes after, and a run with none left ends once the dungeon is done.
        if (DungeonRegistry.IsDungeon(Svc.ClientState.TerritoryType))
        {
            var held = HeldMapReader.TryRead(plugin.Catalog, out _) ? HeldMapCount : 0;
            return new RunCheck(RunBlocker.None, default, held + (supply.CanSupply ? supply.Available : 0));
        }

        if (!HeldMapReader.TryRead(plugin.Catalog, out _))
        {
            return supply.CanSupply ? new RunCheck(RunBlocker.None, default, supply.Available) : new RunCheck(RunBlocker.NoMaps, supply.Reason, 0);
        }

        var refusal = MapRunReadiness.MapBlocker(plugin.Catalog, out _, out _);
        return refusal == MapRefusal.None
            ? new RunCheck(RunBlocker.None, default, HeldMapCount + supply.Available)
            : new RunCheck(RunBlocker.HeldMap, MapOutcomes.Reason(refusal), 0);
    }

    public static bool FollowsLeader() => PartyPresence.InParty() && !PartyPresence.LeadsTheParty();
}
