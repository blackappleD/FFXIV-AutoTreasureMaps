using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Market;

internal readonly record struct WorldListing(uint WorldId, int UnitPrice, int Quantity, int Tax);

// The listings Universalis holds for an item across a data centre. Players upload them as they browse a board, so they
// can be a few hours old; the run reads the board again once it gets there. The request runs off the framework thread,
// and the caller polls the task.
internal static class Universalis
{
    private const string Endpoint = "https://universalis.app/api/v2/{0}/{1}?listings=50&entries=0&fields=listings.worldID%2Clistings.pricePerUnit%2Clistings.quantity%2Clistings.tax";
    private const int RequestTimeoutSeconds = 10;

    private static readonly HttpClient client = CreateClient();

    // One client for every Universalis request the plugin makes.
    internal static HttpClient Client => client;

    public static Task<List<WorldListing>> Listings(string dataCenter, uint itemId, CancellationToken cancel)
        => Task.Run(() => Fetch(dataCenter, itemId, cancel), cancel);

    private static async Task<List<WorldListing>> Fetch(string dataCenter, uint itemId, CancellationToken cancel)
    {
        var url = string.Format(CultureInfo.InvariantCulture, Endpoint, Uri.EscapeDataString(dataCenter), itemId);
        using var response = await client.GetAsync(url, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancel).ConfigureAwait(false);
        var found = new List<WorldListing>();
        if (!document.RootElement.TryGetProperty("listings", out var listings) || listings.ValueKind != JsonValueKind.Array)
        {
            return found;
        }

        foreach (var listing in listings.EnumerateArray())
        {
            found.Add(new WorldListing(
                ReadUInt(listing, "worldID"),
                (int)ReadUInt(listing, "pricePerUnit"),
                (int)ReadUInt(listing, "quantity"),
                (int)ReadUInt(listing, "tax")));
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
