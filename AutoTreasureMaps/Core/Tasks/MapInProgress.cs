using AutoTreasureMaps.Core.Treasure;
using System.Numerics;

namespace AutoTreasureMaps.Core.Tasks;

// The map a run is on, kept by the session so a resume after a pause or a fault picks its hunt up where it stood: once
// the coffer has opened the map is gone, and only this still knows which map it was and what the bags held before.
// Times are Environment.TickCount64, the clock the message watcher stamps with.
internal sealed class MapInProgress(TreasureTier tier, DigSpot spot)
{
    public TreasureTier Tier { get; } = tier;

    public DigSpot Spot { get; set; } = spot;

    public long StartedAtMs { get; } = Environment.TickCount64;

    public ulong CofferId { get; set; }

    public uint CofferEntityId { get; set; }

    public Vector3 CofferPosition { get; set; }

    public long CofferOpenedAtMs { get; set; }

    public Dictionary<uint, int>? BagsBefore { get; set; }

    public bool PortalSeen { get; set; }

    // Set when the run was paused after the coffer opened: what the player did meanwhile would read as loot, so the
    // cleanup leaves this map's loot alone.
    public bool CleanupBlocked { get; set; }

    // The portals that stood near the spot before the dig, which belong to someone else.
    public ulong[] PortalsBefore { get; set; } = [];

    public int Wave { get; set; }

    // The reinforcement message the wave count last took, so a later fight round or a resume does not count it again.
    public long WaveSeenAtMs { get; set; }

    public bool CofferOpened => CofferOpenedAtMs != 0;
}

// Where the portal the last map left should stand, and the portals that were there before it, which are someone else's.
internal readonly record struct PortalLead(Vector3 Near, ulong[] Before);
