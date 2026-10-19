using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Globalization;

namespace AutoTreasureMaps.Core.Cleanup;

// Every item the whitelist could take, found by part of its name or by its exact id. The names are read from the item
// sheet once, on the first search, since that is tens of thousands of rows.
internal static class ItemSearch
{
    private static uint[] ids = [];
    private static string[] names = [];
    private static bool loaded;

    // Up to max matches in sheet order, and how many matched in all.
    public static int Find(string query, List<uint> destination, int max)
    {
        destination.Clear();
        query = query.Trim();
        if (query.Length == 0)
        {
            return 0;
        }

        EnsureLoaded();
        if (uint.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            if (Array.IndexOf(ids, id) >= 0)
            {
                destination.Add(id);
                return 1;
            }

            return 0;
        }

        var total = 0;
        for (var index = 0; index < names.Length; index++)
        {
            if (!names[index].Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            total++;
            if (destination.Count < max)
            {
                destination.Add(ids[index]);
            }
        }

        return total;
    }

    private static void EnsureLoaded()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        var foundIds = new List<uint>();
        var foundNames = new List<string>();
        foreach (var item in Svc.Data.GetExcelSheet<Item>())
        {
            if (item.IsIndisposable)
            {
                continue;
            }

            var name = item.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            foundIds.Add(item.RowId);
            foundNames.Add(name);
        }

        ids = [.. foundIds];
        names = [.. foundNames];
    }
}
