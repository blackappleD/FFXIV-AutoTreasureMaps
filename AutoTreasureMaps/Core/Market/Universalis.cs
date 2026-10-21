using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Market;

internal readonly record struct WorldListing(uint WorldId, int UnitPrice, int Quantity, int Tax);

// One item's listings across a data centre, with when each world's board was last uploaded (Unix milliseconds).
internal sealed record ItemListings(List<WorldListing> Listings, Dictionary<uint, long> WorldUploadTimes)
{
    // A world nobody has uploaded for in this long has listings too old to travel for.
    private static readonly TimeSpan MaxUploadAge = TimeSpan.FromHours(12);

    public bool Fresh(uint worldId, DateTimeOffset now)
        => WorldUploadTimes.TryGetValue(worldId, out var uploadedAtMs)
        && now - DateTimeOffset.FromUnixTimeMilliseconds(uploadedAtMs) <= MaxUploadAge;
}

// The listings Universalis holds for items across a data centre. Players upload them as they browse a board, so they
// can be hours or days old; the run reads the board again once it gets there, and a world whose last upload is too old
// is not travelled to. Every item is asked for in one request. The request runs off the framework thread, and the
// caller polls the task.
internal static class Universalis
{
    private const string Endpoint = "https://universalis.app/api/v2/{0}/{1}?listings=50&entries=0&fields={2}";
    // A request for one item answers with that item at the root; one for several answers with them under "items".
    private const string SingleFields = "listings.worldID%2Clistings.pricePerUnit%2Clistings.quantity%2Clistings.tax%2CworldUploadTimes";
    private const string ManyFields = "items.listings.worldID%2Citems.listings.pricePerUnit%2Citems.listings.quantity%2Citems.listings.tax%2Citems.worldUploadTimes";
    private const int RequestTimeoutSeconds = 10;

    private static readonly HttpClient client = CreateClient();

    // One client for every Universalis request the plugin makes.
    internal static HttpClient Client => client;

    // Keyed by item id; an item Universalis does not answer for is left out.
    public static Task<Dictionary<uint, ItemListings>> Listings(string dataCenter, IReadOnlyList<uint> itemIds, CancellationToken cancel)
        => Task.Run(() => Fetch(dataCenter, itemIds, cancel), cancel);

    private static async Task<Dictionary<uint, ItemListings>> Fetch(string dataCenter, IReadOnlyList<uint> itemIds, CancellationToken cancel)
    {
        var found = new Dictionary<uint, ItemListings>();
        if (itemIds.Count == 0)
        {
            return found;
        }

        var ids = string.Join("%2C", itemIds.Select(static id => id.ToString(CultureInfo.InvariantCulture)));
        var url = string.Format(CultureInfo.InvariantCulture, Endpoint, Uri.EscapeDataString(dataCenter), ids, itemIds.Count == 1 ? SingleFields : ManyFields);
        using var response = await client.GetAsync(url, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancel).ConfigureAwait(false);
        var root = document.RootElement;
        if (itemIds.Count == 1)
        {
            found[itemIds[0]] = ReadItem(root);
            return found;
        }

        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
        {
            return found;
        }

        foreach (var item in items.EnumerateObject())
        {
            if (uint.TryParse(item.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
            {
                found[itemId] = ReadItem(item.Value);
            }
        }

        return found;
    }

    private static ItemListings ReadItem(JsonElement item)
    {
        var listings = new List<WorldListing>();
        if (item.TryGetProperty("listings", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var listing in rows.EnumerateArray())
            {
                listings.Add(new WorldListing(
                    ReadUInt(listing, "worldID"),
                    (int)ReadUInt(listing, "pricePerUnit"),
                    (int)ReadUInt(listing, "quantity"),
                    (int)ReadUInt(listing, "tax")));
            }
        }

        var uploads = new Dictionary<uint, long>();
        if (item.TryGetProperty("worldUploadTimes", out var times) && times.ValueKind == JsonValueKind.Object)
        {
            foreach (var time in times.EnumerateObject())
            {
                if (uint.TryParse(time.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var worldId) && time.Value.TryGetInt64(out var uploadedAtMs))
                {
                    uploads[worldId] = uploadedAtMs;
                }
            }
        }

        return new ItemListings(listings, uploads);
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
