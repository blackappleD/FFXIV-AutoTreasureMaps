using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System.Globalization;
using System.Numerics;

using NativeGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace AutoTreasureMaps.Core.Dungeon;

// The bonus round's field as the sack walk sees it: the sacks on the floor and the charges that are due, read afresh on
// every tick. A charger is followed from the moment it appears, since its lane is known from where it stands and the way
// it faces well before its cast starts. What changed since the last read is kept as plain lines for the round's
// recording. Read only on the framework thread.
internal sealed unsafe class SackField(DungeonProfile profile)
{
    // Read from the logs: a charger appears, starts its 3.7 s Rush about 1.9 s later, and lands about 5.9 s after it
    // appeared.
    private const float AppearToImpactSeconds = 5.9f;
    // A charger this far from where it appeared has charged already.
    private const float ChargedMeters = 3f;
    private const float StaleImpactSeconds = -1f;

    private readonly Dictionary<ulong, Charger> chargers = [];
    private readonly Dictionary<ulong, SackSpot> sacksSeen = [];
    private readonly HashSet<ulong> stillHere = [];
    private readonly HashSet<ulong> described = [];
    private readonly List<ulong> gone = [];

    public List<SackSpot> Sacks { get; } = [];

    public List<ChargeLane> Lanes { get; } = [];

    // What appeared, moved, started casting or went since the last read.
    public List<string> Changes { get; } = [];

    // The floor the sacks have been seen on, which stands for the room's bounds.
    public Vector2 SacksMin { get; private set; } = new(float.MaxValue);

    public Vector2 SacksMax { get; private set; } = new(float.MinValue);

    public bool SackSeen => SacksMin.X <= SacksMax.X;

    // The room as far as it is known: the chargers appear on its edges, and the sacks lie inside them.
    public Vector2 RoomMin { get; private set; } = new(float.MaxValue);

    public Vector2 RoomMax { get; private set; } = new(float.MinValue);

    public bool RoomSeen => RoomMin.X < RoomMax.X && RoomMin.Y < RoomMax.Y;

    public void Read(long nowMs)
    {
        Sacks.Clear();
        Lanes.Clear();
        Changes.Clear();
        ReadSacks();
        ReadChargers(nowMs);
    }


    // A sack handed out is moved to a new spot or gone, or, if it showed when it was chosen, hidden.
    public static bool Taken(ulong sackId, Vector2 at, bool wasShown)
        => Svc.Objects.SearchById(sackId) is not { } sack || Vector2.Distance(Flat(sack.Position), at) > 1f || wasShown && !Shown(sack);

    // Whether the game draws the sack or lets it be targeted. Not known yet which of these a sack on the floor shows, so
    // every object of the sack ids is walked to, and this only tells a sack that vanished in place.
    private static bool Shown(Dalamud.Game.ClientState.Objects.Types.IGameObject sack)
        => sack.IsTargetable || sack.Address != 0 && ((NativeGameObject*)sack.Address)->RenderFlags == default;

    public static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    public bool AnySackObject => described.Count > 0;

    // Every object near the character that is no player, written down once a round when no sack showed, so the sacks'
    // real ids can be found in the recording.
    public void DescribeRoom()
    {
        var player = DungeonField.PlayerPosition;
        foreach (var candidate in Svc.Objects)
        {
            if (candidate.ObjectKind is ObjectKind.Pc or ObjectKind.Companion
                || Vector3.DistanceSquared(player, candidate.Position) > DungeonField.RoomMeters * DungeonField.RoomMeters)
            {
                continue;
            }

            var native = (NativeGameObject*)candidate.Address;
            Changes.Add($"room object {candidate.GameObjectId:X}: {candidate.ObjectKind} base {candidate.BaseId} '{candidate.Name.TextValue}', targetable {candidate.IsTargetable}, render {(native is null ? "?" : native->RenderFlags.ToString())}, at {Format(Flat(candidate.Position))}");
        }
    }

    private void ReadSacks()
    {
        stillHere.Clear();
        var player = DungeonField.PlayerPosition;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not { } candidate
                || Vector3.DistanceSquared(player, candidate.Position) > DungeonField.RoomMeters * DungeonField.RoomMeters)
            {
                continue;
            }

            var points = profile.IsGoldSack(candidate.BaseId) ? 3 : profile.IsSilverSack(candidate.BaseId) ? 1 : 0;
            if (points == 0)
            {
                continue;
            }

            // Every sack object is written down once with what the game says of it, so a round shows how sacks look.
            if (described.Add(candidate.GameObjectId))
            {
                var native = (NativeGameObject*)candidate.Address;
                Changes.Add($"sack object {candidate.GameObjectId:X}: {candidate.ObjectKind} base {candidate.BaseId}, targetable {candidate.IsTargetable}, render {(native is null ? "?" : native->RenderFlags.ToString())}, at {Format(Flat(candidate.Position))}");
            }

            var sack = new SackSpot(candidate.GameObjectId, Flat(candidate.Position), points, Shown(candidate));
            Sacks.Add(sack);
            stillHere.Add(sack.Id);
            SacksMin = Vector2.Min(SacksMin, sack.At);
            SacksMax = Vector2.Max(SacksMax, sack.At);
            RoomMin = Vector2.Min(RoomMin, sack.At);
            RoomMax = Vector2.Max(RoomMax, sack.At);
            if (!sacksSeen.TryGetValue(sack.Id, out var before))
            {
                Changes.Add($"sack {sack.Id:X} {(points == 3 ? "gold" : "silver")} at {Format(sack.At)}");
            }
            else if (Vector2.Distance(before.At, sack.At) > 1f)
            {
                Changes.Add($"sack {sack.Id:X} {(points == 3 ? "gold" : "silver")} moved to {Format(sack.At)}");
            }

            sacksSeen[sack.Id] = sack;
        }

        gone.Clear();
        foreach (var id in sacksSeen.Keys)
        {
            if (!stillHere.Contains(id))
            {
                gone.Add(id);
            }
        }

        for (var index = 0; index < gone.Count; index++)
        {
            Changes.Add($"sack {gone[index]:X} gone");
            sacksSeen.Remove(gone[index]);
        }
    }

    private void ReadChargers(long nowMs)
    {
        stillHere.Clear();
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not IBattleNpc { IsDead: false } candidate || !profile.IsSackCharger(candidate.BaseId))
            {
                continue;
            }

            var id = candidate.GameObjectId;
            stillHere.Add(id);
            var at = Flat(candidate.Position);
            if (!chargers.TryGetValue(id, out var charger))
            {
                charger = new Charger(nowMs, at);
                chargers[id] = charger;
                RoomMin = Vector2.Min(RoomMin, at);
                RoomMax = Vector2.Max(RoomMax, at);
                Changes.Add($"charger {id:X} at {Format(at)} facing {candidate.Rotation.ToString("F2", CultureInfo.InvariantCulture)}");
            }

            if (charger.Landed)
            {
                continue;
            }

            float impactIn;
            if (candidate.IsCasting)
            {
                if (!charger.CastSeen)
                {
                    charger.CastSeen = true;
                    Changes.Add($"charger {id:X} casts {candidate.CastActionId} for {candidate.TotalCastTime.ToString("F2", CultureInfo.InvariantCulture)}s");
                }

                impactIn = candidate.TotalCastTime - candidate.CurrentCastTime;
            }
            else if (charger.CastSeen || Vector2.Distance(at, charger.Appeared) > ChargedMeters)
            {
                charger.Landed = true;
                Changes.Add($"charger {id:X} landed");
                continue;
            }
            else
            {
                impactIn = AppearToImpactSeconds - (nowMs - charger.AppearedAtMs) / 1000f;
            }

            if (impactIn < StaleImpactSeconds)
            {
                continue;
            }

            var facing = new Vector2(MathF.Sin(candidate.Rotation), MathF.Cos(candidate.Rotation));
            Lanes.Add(new ChargeLane(at, facing, impactIn));
        }

        gone.Clear();
        foreach (var id in chargers.Keys)
        {
            if (!stillHere.Contains(id))
            {
                gone.Add(id);
            }
        }

        for (var index = 0; index < gone.Count; index++)
        {
            chargers.Remove(gone[index]);
        }
    }

    private static string Format(Vector2 at)
        => string.Create(CultureInfo.InvariantCulture, $"({at.X:F1}, {at.Y:F1})");

    private sealed class Charger(long appearedAtMs, Vector2 appeared)
    {
        public long AppearedAtMs { get; } = appearedAtMs;

        public Vector2 Appeared { get; } = appeared;

        public bool CastSeen { get; set; }

        public bool Landed { get; set; }
    }
}
