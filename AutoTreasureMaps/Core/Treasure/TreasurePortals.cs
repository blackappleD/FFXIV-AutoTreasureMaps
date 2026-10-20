using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoTreasureMaps.Core.Treasure;

// A coffer of a tier with a dungeon can leave a teleportation portal behind. The game data names four event objects
// "teleportation portal" and the overworld hunt spawns one of them. Another player's portal can stand close by, so the
// portals already standing before the dig are noted, and only one that appeared since, nearest our coffer, is ours.
internal static class TreasurePortals
{
    public const int MaxRemembered = 8;

    private static readonly uint[] PortalBaseIds = [2007181, 2009036, 2009039, 2013488];

    // The portals standing near a point, for the ones that were there before the dig.
    public static int Collect(Vector3 around, float withinMeters, Span<ulong> into)
    {
        var objects = Svc.Objects;
        var limit = withinMeters * withinMeters;
        var count = 0;
        for (var index = 0; index < objects.Length && count < into.Length; index++)
        {
            var candidate = objects[index];
            if (candidate is not null && IsPortal(candidate) && Vector3.DistanceSquared(around, candidate.Position) <= limit)
            {
                into[count++] = candidate.GameObjectId;
            }
        }

        return count;
    }

    // The portal nearest a point that is not one of those standing before.
    public static IGameObject? FindNew(Vector3 around, float withinMeters, ReadOnlySpan<ulong> before)
    {
        var objects = Svc.Objects;
        var nearestDistance = withinMeters * withinMeters;
        IGameObject? nearest = null;
        for (var index = 0; index < objects.Length; index++)
        {
            var candidate = objects[index];
            if (candidate is null || !IsPortal(candidate) || before.Contains(candidate.GameObjectId))
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(around, candidate.Position);
            if (distance <= nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }

    private static bool IsPortal(IGameObject candidate)
        => candidate.ObjectKind == ObjectKind.EventObj && Array.IndexOf(PortalBaseIds, candidate.BaseId) >= 0;
}
