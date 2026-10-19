using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using System.Numerics;
using NativeTreasure = FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure;

namespace AutoTreasureMaps.Core.Dungeon;

// What the dungeon's field shows right now. Every query walks the object table afresh, so nothing it returns is held
// past its frame. Read only on the framework thread.
internal static unsafe class DungeonField
{
    // A chamber or the vault's room is well inside this; a farther object belongs to another chamber.
    public const float RoomMeters = 60f;
    // Gates of one wall stand a few metres apart; a gate farther than this from the nearest one is another wall's.
    private const float GateWallMeters = 30f;
    private const float EnemyMeters = 45f;

    public static Vector3 PlayerPosition => Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;

    // The nearest coffer the run can still act on: a listed event object, or a coffer object of any kind, that is
    // targetable, not fading and not yet asked more than maxInteractions times.
    public static IGameObject? NearestCoffer(DungeonInProgress dungeon, int maxInteractions)
    {
        var from = PlayerPosition;
        IGameObject? nearest = null;
        var best = RoomMeters * RoomMeters;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            var candidate = objects[index];
            if (candidate is null || !candidate.IsTargetable || !IsCoffer(dungeon.Profile, candidate) || dungeon.CofferInteractions(candidate.GameObjectId) >= maxInteractions)
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(from, candidate.Position);
            if (distance < best)
            {
                best = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }

    public static bool IsCoffer(DungeonProfile profile, IGameObject candidate)
        => candidate.ObjectKind switch
        {
            ObjectKind.EventObj => profile.IsCoffer(candidate.BaseId),
            ObjectKind.Treasure => !Faded(candidate),
            _ => false,
        };

    // The advance objects of the nearest wall (the gates) or the machine, in no particular order.
    public static List<IGameObject> AdvanceObjects(DungeonProfile profile)
    {
        var from = PlayerPosition;
        var found = new List<IGameObject>(4);
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            var candidate = objects[index];
            if (candidate is { IsTargetable: true, ObjectKind: ObjectKind.EventObj } && profile.IsAdvance(candidate.BaseId)
                && Vector3.DistanceSquared(from, candidate.Position) <= RoomMeters * RoomMeters)
            {
                found.Add(candidate);
            }
        }

        if (found.Count <= 1)
        {
            return found;
        }

        found.Sort((left, right) => Vector3.DistanceSquared(from, left.Position).CompareTo(Vector3.DistanceSquared(from, right.Position)));
        var anchor = found[0].Position;
        found.RemoveAll(candidate => Vector3.Distance(anchor, candidate.Position) > GateWallMeters);
        return found;
    }

    // Left to right as seen from where the character stands, facing the middle of the gates.
    public static List<IGameObject> LeftToRight(List<IGameObject> gates)
    {
        if (gates.Count <= 1)
        {
            return gates;
        }

        var centre = Vector3.Zero;
        for (var index = 0; index < gates.Count; index++)
        {
            centre += gates[index].Position;
        }

        centre /= gates.Count;
        var facing = centre - PlayerPosition;
        facing.Y = 0f;
        if (facing.LengthSquared() < 0.01f)
        {
            facing = new Vector3(0f, 0f, -1f);
        }

        facing = Vector3.Normalize(facing);
        // X runs east and Z south, so facing north (-Z) puts +X on the right: right = (-facing.Z, 0, facing.X).
        var right = new Vector3(-facing.Z, 0f, facing.X);
        var ordered = new List<IGameObject>(gates);
        ordered.Sort((a, b) => Vector3.Dot(a.Position - centre, right).CompareTo(Vector3.Dot(b.Position - centre, right)));
        return ordered;
    }

    public static IGameObject? NearestTransit(DungeonProfile profile, Vector3 around, float withinMeters, IReadOnlySet<ulong> before)
    {
        IGameObject? nearest = null;
        var best = withinMeters * withinMeters;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            var candidate = objects[index];
            if (candidate is not { ObjectKind: ObjectKind.EventObj })
            {
                continue;
            }

            // A listed circle counts whenever it shows; while none is listed, an event object that was not there before
            // the gate opened is taken for it, and the recording shows which one it was.
            var listed = profile.IsTransit(candidate.BaseId);
            if (!listed && (profile.TransitBaseIds.Length > 0 || before.Contains(candidate.GameObjectId) || profile.IsAdvance(candidate.BaseId) || profile.IsCoffer(candidate.BaseId)))
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(around, candidate.Position);
            if (distance < best)
            {
                best = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }

    public static HashSet<ulong> EventObjectIds()
    {
        var ids = new HashSet<ulong>();
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is { ObjectKind: ObjectKind.EventObj } candidate)
            {
                ids.Add(candidate.GameObjectId);
            }
        }

        return ids;
    }

    public static IGameObject? NearestExit(DungeonProfile profile)
    {
        var from = PlayerPosition;
        IGameObject? nearest = null;
        var best = float.MaxValue;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            var candidate = objects[index];
            if (candidate is { IsTargetable: true, ObjectKind: ObjectKind.EventObj } && profile.IsExit(candidate.BaseId))
            {
                var distance = Vector3.DistanceSquared(from, candidate.Position);
                if (distance < best)
                {
                    best = distance;
                    nearest = candidate;
                }
            }
        }

        return nearest;
    }

    // Live, targetable, hostile combatants close enough to be this room's.
    public static IBattleNpc? NearestEnemy(out int count)
    {
        count = 0;
        var from = PlayerPosition;
        IBattleNpc? nearest = null;
        var best = EnemyMeters * EnemyMeters;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant, IsTargetable: true, IsDead: false } npc || npc.CurrentHp == 0 || !npc.IsHostile())
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(from, npc.Position);
            if (distance > EnemyMeters * EnemyMeters)
            {
                continue;
            }

            count++;
            if (distance < best)
            {
                best = distance;
                nearest = npc;
            }
        }

        return nearest;
    }

    // The enemy the settings want killed first: the nearest rare enemy, then the mandragora whose marker comes next. Null
    // when neither is asked for or none is near. Only live, targetable, hostile combatants within the room count.
    public static IBattleNpc? PriorityEnemy(DungeonProfile profile, bool bonusFirst, bool mandragorasInOrder)
    {
        if (!bonusFirst && !mandragorasInOrder)
        {
            return null;
        }

        var from = PlayerPosition;
        IBattleNpc? bonus = null;
        var bonusDistance = EnemyMeters * EnemyMeters;
        IBattleNpc? mandragora = null;
        var mandragoraOrder = int.MaxValue;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant, IsTargetable: true, IsDead: false } npc || npc.CurrentHp == 0 || !npc.IsHostile())
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(from, npc.Position);
            if (distance > EnemyMeters * EnemyMeters)
            {
                continue;
            }

            if (bonusFirst && profile.IsBonus(npc.NameId) && distance < bonusDistance)
            {
                bonusDistance = distance;
                bonus = npc;
                continue;
            }

            var order = mandragorasInOrder ? profile.MandragoraOrder(npc.NameId) : -1;
            if (order >= 0 && order < mandragoraOrder)
            {
                mandragoraOrder = order;
                mandragora = npc;
            }
        }

        return bonus ?? mandragora;
    }

    private static bool Faded(IGameObject coffer)
    {
        if (coffer.Address == 0)
        {
            return true;
        }

        var treasure = (NativeTreasure*)coffer.Address;
        return treasure->State is NativeTreasure.TreasureState.FadingOut or NativeTreasure.TreasureState.FadedOut
            || (treasure->Flags & NativeTreasure.TreasureFlags.FadedOut) != 0;
    }
}
