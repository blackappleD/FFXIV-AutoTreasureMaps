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
    // The left card stands about 2m from the midpoint of the choices on the 大 side, the right card as far on the 小
    // side, and the coffer between them; a card counts as a side's past this.
    private const float LureCardSideMeters = 1.5f;
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
            if (candidate is null || !candidate.IsTargetable || !IsCoffer(dungeon.Profile, candidate) || dungeon.CofferLooted(candidate.GameObjectId)
                || dungeon.CofferInteractions(candidate.GameObjectId) >= maxInteractions)
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
    // Whether any gate or machine is up within the room, without building the list Decide would throw away every tick.
    public static bool AnyAdvanceObject(DungeonProfile profile)
    {
        var from = PlayerPosition;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            var candidate = objects[index];
            if (candidate is { IsTargetable: true, ObjectKind: ObjectKind.EventObj } && profile.IsAdvance(candidate.BaseId)
                && Vector3.DistanceSquared(from, candidate.Position) <= RoomMeters * RoomMeters)
            {
                return true;
            }
        }

        return false;
    }

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
        ordered.Sort((first, second) => Vector3.Dot(first.Position - centre, right).CompareTo(Vector3.Dot(second.Position - centre, right)));
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
    public static IGameObject? LureChoice(bool high) => FindLureChoice(high, targetableOnly: true);

    // The value of the left card, dealt face up, while a guess is waited on; null when it does not show one face.
    // raw is the mask read, for the log.
    public static int? LureShownCard(out int raw)
    {
        raw = 0;
        return LureGuessPending() ? LureCardFace(left: true, out raw) : null;
    }

    // The value a card shows, 1 to 9, at any point of the lure: the right card turned over by the last guess becomes
    // the next challenge's left card, so it is read before deciding to challenge again. Null when the card is face
    // down or not found.
    public static int? LureCardFace(bool left, out int raw)
    {
        raw = 0;
        if (FindLureChoice(high: true, targetableOnly: false) is not { } high || FindLureChoice(high: false, targetableOnly: false) is not { } low)
        {
            return null;
        }

        var middle = (high.Position + low.Position) / 2f;
        var towardRight = low.Position - high.Position;
        if (towardRight.LengthSquared() < float.Epsilon)
        {
            return null;
        }

        towardRight = Vector3.Normalize(towardRight);
        foreach (var candidate in Svc.Objects)
        {
            if (candidate.ObjectKind != ObjectKind.EventObj || candidate.BaseId != LureCardObject || candidate.Address == 0
                || Vector3.Distance(middle, candidate.Position) > LureCardMeters)
            {
                continue;
            }

            var along = Vector3.Dot(candidate.Position - middle, towardRight);
            if (left ? along > -LureCardSideMeters : along < LureCardSideMeters)
            {
                continue;
            }

            raw = *(ushort*)((byte*)candidate.Address + LureCardFaceOffset);
            return FaceOf(raw);
        }

        return null;
    }

    private static IGameObject? FindLureChoice(bool high, bool targetableOnly)
    {
        foreach (var candidate in Svc.Objects)
        {
            if (IsLureChoice(candidate) && (!targetableOnly || candidate.IsTargetable) && (candidate.BaseId % 2 == 0) == high)
            {
                return candidate;
            }
        }

        return null;
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

    // Live, targetable, hostile combatants close enough to be this room's. A mandragora that is not fighting yet is left
    // out unless the settings want the party killed: it leaves on its own before long, so pulling it unasked only loses
    // the run time. One that fights the character counts like any other enemy.
    public static IBattleNpc? NearestEnemy(DungeonProfile profile, bool mandragoras, out int count)
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

            if (!mandragoras && Unpulled(profile, npc))
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

    // A mandragora of the party that is not fighting anyone yet.
    public static bool Unpulled(DungeonProfile profile, IBattleNpc npc)
        => profile.MandragoraOrder(npc.NameId) >= 0 && (npc.StatusFlags & StatusFlags.InCombat) == 0;

    // Whether a mandragora of the party is anywhere in the room and not fighting anyone yet.
    public static bool UnpulledMandragora(DungeonProfile profile)
    {
        if (profile.MandragoraNameIds.Length == 0)
        {
            return false;
        }

        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant, IsTargetable: true, IsDead: false } npc && npc.CurrentHp != 0 && Unpulled(profile, npc))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a mandragora of the party is alive in the room, fighting or not.
    public static bool AnyMandragora(DungeonProfile profile)
    {
        if (profile.MandragoraNameIds.Length == 0)
        {
            return false;
        }

        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant, IsTargetable: true, IsDead: false } npc && npc.CurrentHp != 0 && profile.MandragoraOrder(npc.NameId) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // The room's nearest live, targetable, hostile combatant that is not of the mandragora party, or null.
    public static IBattleNpc? NearestNonMandragora(DungeonProfile profile)
    {
        var from = PlayerPosition;
        IBattleNpc? nearest = null;
        var best = EnemyMeters * EnemyMeters;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant, IsTargetable: true, IsDead: false } npc || npc.CurrentHp == 0 || !npc.IsHostile() || profile.MandragoraOrder(npc.NameId) >= 0)
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(from, npc.Position);
            if (distance < best)
            {
                best = distance;
                nearest = npc;
            }
        }

        return nearest;
    }

    // The enemy to kill first: the nearest rare enemy (one that runs off before one that stays) and then the mandragora
    // whose marker comes next, where the settings ask for them, then the nearest add the profile puts before the boss it comes with. Null when none is near. Only
    // live, targetable, hostile combatants within the room count.
    public static IBattleNpc? PriorityEnemy(DungeonProfile profile, bool bonusFirst, bool mandragorasInOrder)
    {
        if (!bonusFirst && !mandragorasInOrder && profile.FirstNameIds.Length == 0)
        {
            return null;
        }

        var from = PlayerPosition;
        IBattleNpc? bonus = null;
        var bonusFleeing = false;
        var bonusDistance = EnemyMeters * EnemyMeters;
        IBattleNpc? mandragora = null;
        var mandragoraOrder = int.MaxValue;
        IBattleNpc? first = null;
        var firstDistance = EnemyMeters * EnemyMeters;
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

            if (bonusFirst && profile.IsBonus(npc.NameId))
            {
                var fleeing = profile.IsFleeing(npc.NameId);
                if (bonus is null || (fleeing && !bonusFleeing) || (fleeing == bonusFleeing && distance < bonusDistance))
                {
                    bonusDistance = distance;
                    bonusFleeing = fleeing;
                    bonus = npc;
                }

                continue;
            }

            var order = mandragorasInOrder ? profile.MandragoraOrder(npc.NameId) : -1;
            if (order >= 0 && order < mandragoraOrder)
            {
                mandragoraOrder = order;
                mandragora = npc;
                continue;
            }

            if (profile.IsFirst(npc.NameId) && distance < firstDistance)
            {
                firstDistance = distance;
                first = npc;
            }
        }

        return bonus ?? mandragora ?? first;
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
