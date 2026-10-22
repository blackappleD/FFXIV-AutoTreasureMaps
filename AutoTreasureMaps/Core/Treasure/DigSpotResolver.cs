using AutoTreasureMaps.Core.Travel;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoTreasureMaps.Core.Treasure;

// The sheet part is kept for the last spot asked about, but flight access is read on every call: attuning the zone's
// aether currents changes it while the spot stays the same.
internal static class DigSpotResolver
{
    private static bool cacheFilled;
    private static byte cachedRank;
    private static ushort cachedSpotIndex;
    private static bool cachedResolved;
    private static DigSpot cachedSpot;

    public static bool TryResolve(byte rank, ushort spotIndex, out DigSpot spot)
    {
        if (!cacheFilled || cachedRank != rank || cachedSpotIndex != spotIndex)
        {
            cachedResolved = TryResolveFromSheets(rank, spotIndex, out cachedSpot);
            cachedRank = rank;
            cachedSpotIndex = spotIndex;
            cacheFilled = true;
        }

        if (!cachedResolved)
        {
            spot = default;
            return false;
        }

        spot = cachedSpot with { FlyingUnlocked = FlightAccess.IsAvailableIn(cachedSpot.TerritoryId) };
        return true;
    }

    // The dig spot a flag in the zone points at. A chat link carries only the ground point, so the spot's own row gives
    // the height; the nearest spot of any tier within reach wins.
    public static bool TryFindNear(uint territoryId, Vector2 groundPoint, float withinMeters, out DigSpot spot)
    {
        spot = default;
        var best = withinMeters * withinMeters;
        var found = false;
        var sheet = Svc.Data.GetSubrowExcelSheet<TreasureSpot>();
        foreach (var spots in sheet)
        {
            for (var spotIndex = 0; spotIndex < spots.Count; spotIndex++)
            {
                if (spots[spotIndex].Location.ValueNullable is not { } level || level.Territory.RowId != territoryId)
                {
                    continue;
                }

                var distance = Vector2.DistanceSquared(groundPoint, new Vector2(level.X, level.Z));
                if (distance <= best && TryResolve((byte)spots.RowId, (ushort)spotIndex, out var candidate))
                {
                    best = distance;
                    spot = candidate;
                    found = true;
                }
            }
        }

        return found;
    }

    private static bool TryResolveFromSheets(byte rank, ushort spotIndex, out DigSpot spot)
    {
        spot = default;
        if (Svc.Data.GetSubrowExcelSheet<TreasureSpot>().GetSubrowOrDefault(rank, spotIndex) is not { } treasureSpot)
        {
            return false;
        }

        if (treasureSpot.Location.RowId == 0 || treasureSpot.Location.ValueNullable is not { } level)
        {
            return false;
        }

        var territoryId = level.Territory.RowId;
        var mapId = level.Map.RowId;
        var position = new Vector3(level.X, level.Y, level.Z);
        if (territoryId == 0 || !MapCoordinates.TryToMap(mapId, position, out var mapCoordinates))
        {
            return false;
        }

        spot = new DigSpot(TerritoryNames.Of(territoryId), position, mapCoordinates, level.RowId, territoryId, mapId, spotIndex, rank, FlyingUnlocked: false);
        return true;
    }
}
