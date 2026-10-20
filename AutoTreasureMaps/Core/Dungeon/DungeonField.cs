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
    // A chamber or the vault's room is well inside this, the way in from the entrance included (the cenote's first
    // coffer stands 80m from it); the next chamber is farther, 185m or more.
    public const float RoomMeters = 100f;
    // The gates of one wall stand on either side of the chamber, up to 50m apart; a gate farther than this from the
    // nearest one is another wall's.
    private const float GateWallMeters = 60f;
    private const float EnemyMeters = 45f;
    // Squared distances are compared, so a gold sack three times as far as a silver one weighs the same.
    private const float SilverSackWeight = 9f;
    private const float SackMovedMeters = 1f;
    // EObj rows of the Gambler's Lure's 大 (high) and 小 (low) choices: five pairs, 大 on the even row, one pair per
    // treasure dungeon that has the lure.
    private const uint FirstLureChoice = 2013872;
    private const uint LastLureChoice = 2013881;
    // The cards themselves are nameless event objects of this row, one either side of the coffer, a metre outside the
    // 大 and 小 choices. A card's face is one bit of a mask in the object: 1 << (value + 3) for 1 to 9. A face-down card
    // shows bit 2 and a cleared one bit 0, outside that range. Read in game: a 4 showed 0x80 and a 2 showed 0x20.
    private const uint LureCardObject = 2007457;
    private const int LureCardFaceOffset = 0x1B2;
    private const int LureCardFaceShift = 3;
    private const float LureCardMeters = 3.5f;

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

    // The game moves a sack it has handed out to a new spot rather than removing it, so one that left its spot counts as
    // picked up.
    public static bool SackMoved(IGameObject sack, Vector3 from)
        => Vector3.DistanceSquared(sack.Position, from) > SackMovedMeters * SackMovedMeters;

    // The bonus round's sack to walk to next: the nearest, with a gold sack counted as near as a silver one three times
    // closer, since it is worth three. Null when the profile knows no sacks or none is on the field. A skipped sack is
    // passed over only while it still stands where it was skipped, since the game reuses it once it moves.
    public static IGameObject? NextSack(DungeonProfile profile, IReadOnlyDictionary<ulong, Vector3>? skipped = null)
    {
        if (!profile.HasSacks)
        {
            return null;
        }

        var from = PlayerPosition;
        IGameObject? next = null;
        var best = float.MaxValue;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not { ObjectKind: ObjectKind.EventObj, IsTargetable: true } candidate
                || skipped is not null && skipped.TryGetValue(candidate.GameObjectId, out var at) && !SackMoved(candidate, at))
            {
                continue;
            }

            var weight = profile.IsGoldSack(candidate.BaseId) ? 1f : profile.IsSilverSack(candidate.BaseId) ? SilverSackWeight : 0f;
            var distance = Vector3.DistanceSquared(from, candidate.Position);
            if (weight == 0f || distance > RoomMeters * RoomMeters)
            {
                continue;
            }

            if (distance * weight < best)
            {
                best = distance * weight;
                next = candidate;
            }
        }

        return next;
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

    // The Gambler's Lure deals its cards as the 大 and 小 event objects beside the coffer; they can be targeted only
    // while a guess is waited on.
    public static bool LureGuessPending()
    {
        foreach (var candidate in Svc.Objects)
        {
            if (IsLureChoice(candidate) && candidate.IsTargetable)
            {
                return true;
            }
        }

        return false;
    }

    // The 大 or 小 choice waiting on a guess.
    public static IGameObject? LureChoice(bool high)
    {
        foreach (var candidate in Svc.Objects)
        {
            if (IsLureChoice(candidate) && candidate.IsTargetable && (candidate.BaseId % 2 == 0) == high)
            {
                return candidate;
            }
        }

        return null;
    }

    // The value of the card dealt face up, 1 to 9, while a guess is waited on; null when no card near the choices
    // shows exactly one face. raw is the part mask read, for the log.
    public static int? LureShownCard(out int raw)
    {
        raw = 0;
        if (LureChoice(true) is not { } high || LureChoice(false) is not { } low)
        {
            return null;
        }

        var middle = (high.Position + low.Position) / 2f;
        int? shown = null;
        foreach (var candidate in Svc.Objects)
        {
            if (candidate.ObjectKind != ObjectKind.EventObj || candidate.BaseId != LureCardObject || candidate.Address == 0
                || Vector3.Distance(middle, candidate.Position) > LureCardMeters)
            {
                continue;
            }

            var mask = *(ushort*)((byte*)candidate.Address + LureCardFaceOffset);
            if (FaceOf(mask) is not { } value)
            {
                continue;
            }

            // Two faces up means the guess has already been answered; neither is the one to guess on.
            if (shown is not null)
            {
                return null;
            }

            shown = value;
            raw = mask;
        }

        return shown;
    }

    private static int? FaceOf(int mask)
    {
        if (mask == 0 || (mask & (mask - 1)) != 0)
        {
            return null;
        }

        var value = BitOperations.TrailingZeroCount(mask) - LureCardFaceShift;
        return value is >= 1 and <= 9 ? value : null;
    }

    private static bool IsLureChoice(IGameObject candidate)
        => candidate.ObjectKind == ObjectKind.EventObj && candidate.BaseId is >= FirstLureChoice and <= LastLureChoice;

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
