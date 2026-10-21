using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System.Globalization;
using System.Numerics;

namespace AutoTreasureMaps.Core.Dungeon;

// The bonus round's field as the sack walk sees it: the sacks on the floor and the charges that are due, read afresh on
// every tick. A charger is followed from the moment it appears, since its lane is known from where it stands and the way
// it faces well before its cast starts. What changed since the last read is kept as plain lines for the round's
// recording. Read only on the framework thread.
internal sealed class SackField(DungeonProfile profile)
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
    private readonly List<ulong> gone = [];

    public List<SackSpot> Sacks { get; } = [];

    public List<ChargeLane> Lanes { get; } = [];

    // What appeared, moved, started casting or went since the last read.
    public List<string> Changes { get; } = [];

    // The floor the sacks have been seen on, which stands for the room's bounds.
    public Vector2 SacksMin { get; private set; } = new(float.MaxValue);

    public Vector2 SacksMax { get; private set; } = new(float.MinValue);

    public bool SackSeen => SacksMin.X <= SacksMax.X;

    public void Read(long nowMs)
    {
        Sacks.Clear();
        Lanes.Clear();
        Changes.Clear();
        ReadSacks();
        ReadChargers(nowMs);
    }


    // A sack handed out is moved to a new spot, or gone or untargetable on the way there.
    public static bool Taken(ulong sackId, Vector2 at)
        => Svc.Objects.SearchById(sackId) is not { IsTargetable: true } sack || Vector2.Distance(Flat(sack.Position), at) > 1f;

    public static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    private void ReadSacks()
    {
        stillHere.Clear();
        var player = DungeonField.PlayerPosition;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not { ObjectKind: ObjectKind.EventObj, IsTargetable: true } candidate
                || Vector3.DistanceSquared(player, candidate.Position) > DungeonField.RoomMeters * DungeonField.RoomMeters)
            {
                continue;
            }

            var points = profile.IsGoldSack(candidate.BaseId) ? 3 : profile.IsSilverSack(candidate.BaseId) ? 1 : 0;
            if (points == 0)
            {
                continue;
            }

            var sack = new SackSpot(candidate.GameObjectId, Flat(candidate.Position), points);
            Sacks.Add(sack);
            stillHere.Add(sack.Id);
            SacksMin = Vector2.Min(SacksMin, sack.At);
            SacksMax = Vector2.Max(SacksMax, sack.At);
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
