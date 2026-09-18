using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Infrastructure.Integrations;

namespace WonderFleet.Infrastructure.Integrations.Google;

/// Geocoding API, Places API (New) autocomplete and Routes API (computeRoutes with alternatives).
internal sealed class GoogleMapsService(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleOptions> options,
    IMemoryCache cache,
    ILogger<GoogleMapsService> logger) : IMapsService
{
    private HttpClient Http => httpClientFactory.CreateClient(HttpClients.Google);

    public async Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken ct)
    {
        var o = Require();
        var key = $"geo:{address.Trim().ToLowerInvariant()}";
        if (cache.TryGetValue<GeocodeResult?>(key, out var cached)) return cached;

        var url = "https://maps.googleapis.com/maps/api/geocode/json"
                  + $"?address={Uri.EscapeDataString(address)}&region={o.RegionCode.ToLowerInvariant()}&key={Uri.EscapeDataString(o.ApiKey!)}";
        using var response = await Http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        var status = doc.RootElement.GetProperty("status").GetString();
        if (status == "ZERO_RESULTS")
        {
            cache.Set(key, (GeocodeResult?)null, TimeSpan.FromMinutes(10));
            return null;
        }
        if (status != "OK")
        {
            logger.LogWarning("Geocoding failed with status {Status}", status);
            throw new HttpRequestException($"Geocoding API returned {status}.");
        }

        var first = doc.RootElement.GetProperty("results")[0];
        var location = first.GetProperty("geometry").GetProperty("location");
        var result = new GeocodeResult(
            first.GetProperty("formatted_address").GetString() ?? address,
            new GeoPoint(location.GetProperty("lat").GetDouble(), location.GetProperty("lng").GetDouble()),
            Component(first, "locality") ?? Component(first, "administrative_area_level_2"),
            Component(first, "administrative_area_level_1"));

        cache.Set(key, result, TimeSpan.FromHours(Math.Clamp(o.GeocodeCacheHours, 1, 168)));
        return result;
    }

    public async Task<IReadOnlyList<PlaceSuggestion>> AutocompleteAsync(string input, string? sessionToken, CancellationToken ct)
    {
        var o = Require();
        if (input.Trim().Length < 3) return [];

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:autocomplete")
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["input"] = input.Trim(),
                ["includedRegionCodes"] = RegionCodes(o.RegionCode),
                ["sessionToken"] = sessionToken,
            }),
        };
        request.Headers.Add("X-Goog-Api-Key", o.ApiKey);

        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("suggestions", out var suggestions)) return [];

        var results = new List<PlaceSuggestion>();
        foreach (var suggestion in suggestions.EnumerateArray())
        {
            if (!suggestion.TryGetProperty("placePrediction", out var prediction)) continue;
            var id = prediction.GetProperty("placeId").GetString();
            var text = prediction.GetProperty("text").GetProperty("text").GetString();
            if (id is not null && text is not null) results.Add(new PlaceSuggestion(id, text));
        }
        return results;
    }

    public async Task<IReadOnlyList<RouteOption>> ComputeRoutesAsync(GeoPoint origin, GeoPoint destination, DateTimeOffset departure, CancellationToken ct)
    {
        var o = Require();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://routes.googleapis.com/directions/v2:computeRoutes")
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["origin"] = Waypoint(origin),
                ["destination"] = Waypoint(destination),
                ["travelMode"] = "DRIVE",
                ["routingPreference"] = "TRAFFIC_AWARE",
                ["computeAlternativeRoutes"] = true,
                ["departureTime"] = departure.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["regionCode"] = o.RegionCode,
                ["units"] = "METRIC",
                ["extraComputations"] = TrafficComputation,
            }),
        };
        request.Headers.Add("X-Goog-Api-Key", o.ApiKey);
        request.Headers.Add("X-Goog-FieldMask",
            "routes.distanceMeters,routes.duration,routes.staticDuration,routes.polyline.encodedPolyline,routes.description,routes.warnings");

        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Routes API {Status}: {Error}", (int)response.StatusCode, error.Length > 300 ? error[..300] : error);
            response.EnsureSuccessStatusCode();
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("routes", out var routes)) return [];

        var options = new List<RouteOption>();
        var index = 0;
        foreach (var route in routes.EnumerateArray())
        {
            var meters = route.TryGetProperty("distanceMeters", out var d) ? d.GetInt32() : 0;
            var polyline = route.TryGetProperty("polyline", out var p) && p.TryGetProperty("encodedPolyline", out var e)
                ? e.GetString() ?? "" : "";
            var warnings = route.TryGetProperty("warnings", out var w)
                ? w.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
                : [];
            options.Add(new RouteOption(
                index,
                Math.Round(meters / 1000d, 1),
                ParseDuration(route.TryGetProperty("duration", out var dur) ? dur.GetString() : null),
                polyline,
                route.TryGetProperty("description", out var desc) ? desc.GetString() ?? $"Route {index + 1}" : $"Route {index + 1}",
                warnings,
                route.TryGetProperty("staticDuration", out var staticDuration) ? ParseDuration(staticDuration.GetString()) : null));
            index++;
        }
        return options;
    }

    // ------------------------------------------------------------------ helpers

    private static readonly string[] TrafficComputation = ["TRAFFIC_ON_POLYLINE"];

    private static string[] RegionCodes(string regionCode) => [regionCode];

    private GoogleOptions Require() =>
        options.Value.IsConfigured ? options.Value : throw new InvalidOperationException("Google:ApiKey is not configured.");

    private static Dictionary<string, object> Waypoint(GeoPoint point) => new()
    {
        ["location"] = new Dictionary<string, object>
        {
            ["latLng"] = new Dictionary<string, double> { ["latitude"] = point.Latitude, ["longitude"] = point.Longitude },
        },
    };

    /// Routes API returns protobuf durations such as "12345s".
    internal static int ParseDuration(string? duration) =>
        duration is not null && double.TryParse(duration.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? (int)Math.Round(seconds / 60)
            : 0;

    private static string? Component(JsonElement result, string type)
    {
        if (!result.TryGetProperty("address_components", out var components)) return null;
        foreach (var component in components.EnumerateArray())
        {
            foreach (var t in component.GetProperty("types").EnumerateArray())
            {
                if (t.GetString() == type) return component.GetProperty("long_name").GetString();
            }
        }
        return null;
    }
}
