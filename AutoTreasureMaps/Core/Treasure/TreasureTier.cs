using AutoTreasureMaps.Core.Travel;

namespace AutoTreasureMaps.Core.Treasure;

// PortalKeyItemId is the thief's map a tier hands out when its portal could not be entered, 0 for a tier without one.
internal readonly record struct TreasureTier(
    string Name,
    uint TimewornItemId,
    uint KeyItemId,
    uint PortalKeyItemId,
    byte Rank,
    byte MaxPartySize,
    byte SpotCount,
    ExpansionKind Expansion,
    bool Undersea,
    bool Tradable)
{
    private const byte FullPartySize = 8;

    public bool HasPortal => PortalKeyItemId != 0;

    public GamePatch Patch => MapPatches.Of(TimewornItemId);

    public bool TunedForFullParty => MaxPartySize >= FullPartySize;
}
