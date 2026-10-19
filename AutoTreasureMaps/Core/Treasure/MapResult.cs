using System.Collections.Immutable;

namespace AutoTreasureMaps.Core.Treasure;

internal readonly record struct ItemGain(uint ItemId, int Quantity);

// One map from its start to its end. Refusal is None unless Outcome is Refused. Gil and Items are what the bags gained
// between the coffer opening and the loot settling, so they stay empty for a map that never opened its coffer. Tier is
// the default tier, rank 0, when no map was held to begin with.
internal readonly record struct MapResult(
    TreasureTier Tier,
    MapOutcome Outcome,
    MapRefusal Refusal,
    bool PortalSeen,
    int Gil,
    ImmutableArray<ItemGain> Items,
    TimeSpan Duration)
{
    public bool HasTier => Tier.Rank != 0;

    public int ItemCount => LootKinds.ItemCount(Items);

    public int Tomestones => LootKinds.TomestoneCount(Items);

    public static MapResult Refused(in TreasureTier tier, MapRefusal refusal)
        => new(tier, MapOutcome.Refused, refusal, PortalSeen: false, Gil: 0, ImmutableArray<ItemGain>.Empty, TimeSpan.Zero);
}
