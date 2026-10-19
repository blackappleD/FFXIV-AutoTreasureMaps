using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Travel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Market;

// What the high-value loot sells for: the lowest listing on the home world's data centre, from Universalis. One batched
// request prices every listed item, refreshed every half hour and retried a minute after a failure. Tick runs on the
// framework thread; the request itself runs off it, and the windows only read the last prices it brought back.
internal static class LootPrices
{
    private const string Endpoint = "https://universalis.app/api/v2/{0}/{1}?listings=0&entries=0&fields=items.itemID%2Citems.minPriceNQ%2Citems.minPrice";
    private const int RefreshMs = 30 * 60 * 1000;
    private const int RetryMs = 60 * 1000;
    private const int RequestTimeoutSeconds = 10;

    private static readonly HttpClient client = CreateClient();
    private static Dictionary<uint, int> prices = [];
    private static Task<Dictionary<uint, int>>? pending;
    private static long nextRequestAtMs;
    private static uint pricedHomeWorld;

    // The data centre the prices are from, empty until a request has answered.
    public static string DataCenter { get; private set; } = string.Empty;

    // Bumped whenever new prices arrive, so captions built from them know when to rebuild.
    public static int Revision { get; private set; }

    public static bool Loaded => prices.Count > 0;

    public static void Tick()
    {
        if (pending is { } request)
        {
            if (request.IsCompleted)
            {
                Collect(request);
            }

            return;
        }

        // A character from another home world is priced at once rather than after the half hour.
        var home = Worlds.Home;
        if (home == 0 || (Environment.TickCount64 < nextRequestAtMs && home == pricedHomeWorld))
        {
            return;
        }

        var dataCenter = Worlds.DataCenterOf(home);
        if (dataCenter.Length == 0)
        {
            return;
        }

        pricedHomeWorld = home;
        DataCenter = dataCenter;
        nextRequestAtMs = Environment.TickCount64 + RetryMs;
        var ids = string.Join(',', HighValueLoot.AllItems.ToArray());
        pending = Task.Run(() => Fetch(dataCenter, ids));
    }

    // 0 for an item nobody lists or before the prices have come in.
    public static int Of(uint itemId) => prices.GetValueOrDefault(itemId);

    public static long ValueOf(Dictionary<uint, int>? items)
    {
        if (items is null)
        {
            return 0;
        }

        long total = 0;
        foreach (var (itemId, quantity) in items)
        {
            total += (long)Of(itemId) * quantity;
        }

        return total;
    }

    public static long ValueOf(Dictionary<uint, MapTally>? tallies)
    {
        if (tallies is null)
        {
            return 0;
        }

        long total = 0;
        foreach (var tally in tallies.Values)
        {
            total += ValueOf(tally.Items);
        }

        return total;
    }

    private static void Collect(Task<Dictionary<uint, int>> request)
    {
        pending = null;
        if (!request.IsCompletedSuccessfully)
        {
            RunLog.Warning($"Loot prices: Universalis request for {DataCenter} failed: {request.Exception?.GetBaseException().Message}");
            return;
        }

        prices = request.Result;
        nextRequestAtMs = Environment.TickCount64 + RefreshMs;
        Revision++;
        RunLog.Debug($"Loot prices: {prices.Count} item(s) priced on {DataCenter}.");
    }

    private static async Task<Dictionary<uint, int>> Fetch(string dataCenter, string ids)
    {
        var url = string.Format(CultureInfo.InvariantCulture, Endpoint, Uri.EscapeDataString(dataCenter), ids);
        using var response = await client.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body).ConfigureAwait(false);
        var found = new Dictionary<uint, int>();
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
        {
            return found;
        }

        foreach (var entry in items.EnumerateObject())
        {
            var item = entry.Value;
            var itemId = ReadUInt(item, "itemID");
            // Loot drops NQ; an item only ever listed HQ falls back to its lowest listing of any quality.
            var price = ReadUInt(item, "minPriceNQ");
            if (price == 0)
            {
                price = ReadUInt(item, "minPrice");
            }

            if (itemId != 0 && price != 0)
            {
                found[itemId] = (int)Math.Min(price, int.MaxValue);
            }
        }

        return found;
    }

    private static uint ReadUInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetUInt32(out var number) ? number : 0;

    private static HttpClient CreateClient()
    {
        var created = new HttpClient { Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds) };
        created.DefaultRequestHeaders.UserAgent.ParseAdd("TreasureHelper (Dalamud plugin)");
        return created;
    }
}
