namespace AutoTreasureMaps.Core.Dungeon;

// The treasure dungeons the run knows, by the territory they take place in.
internal static class DungeonRegistry
{
    private static readonly TreasureDungeonHandler[] handlers =
    [
        new CenoteJaJaGuralHandler(),
        new VaultOneironHandler(),
    ];

    public static ReadOnlySpan<TreasureDungeonHandler> All => handlers;

    public static TreasureDungeonHandler? Find(uint territoryId)
    {
        for (var index = 0; index < handlers.Length; index++)
        {
            if (handlers[index].Profile.TerritoryId == territoryId)
            {
                return handlers[index];
            }
        }

        return null;
    }

    public static bool IsDungeon(uint territoryId) => Find(territoryId) is not null;
}
