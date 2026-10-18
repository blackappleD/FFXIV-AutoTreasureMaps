using AutoTreasureMaps.Core.Localization;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoTreasureMaps.Core.Travel;

// The character's worlds and their data centre, from the game's World sheet. Read only on the framework thread.
internal static class Worlds
{
    public static uint Current => Svc.Objects.LocalPlayer?.CurrentWorld.RowId ?? 0;

    public static uint Home => Svc.Objects.LocalPlayer?.HomeWorld.RowId ?? 0;

    public static bool AwayFromHome => Current != 0 && Home != 0 && Current != Home;

    public static string NameOf(uint worldId)
    {
        var name = Svc.Data.GetExcelSheet<World>().GetRowOrDefault(worldId)?.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? $"world {worldId}" : GameGlyphs.Cover(name);
    }

    // The data centre's name as the game spells it, which is also the name Universalis files the CN worlds under.
    public static string DataCenterOf(uint worldId)
        => Svc.Data.GetExcelSheet<World>().GetRowOrDefault(worldId)?.DataCenter.ValueNullable?.Name.ExtractText() ?? string.Empty;
}
