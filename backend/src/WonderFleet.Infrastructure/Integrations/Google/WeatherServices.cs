using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Infrastructure.Integrations;

namespace WonderFleet.Infrastructure.Integrations.Google;

/// Google Weather API with a keyless Open-Meteo fallback, so the dashboard keeps working
/// if the Weather API is not enabled on the billing account.
internal sealed class WeatherService(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleOptions> googleOptions,
    IOptions<WeatherOptions> options,
    IMemoryCache cache,
    ILogger<WeatherService> logger) : IWeatherService
{
    private HttpClient GoogleHttp => httpClientFactory.CreateClient(HttpClients.Google);
    private HttpClient OpenMeteoHttp => httpClientFactory.CreateClient(HttpClients.OpenMeteo);

    public async Task<WeatherNow?> GetCurrentAsync(GeoPoint point, CancellationToken ct)
    {
        var key = $"wx:now:{Round(point)}";
        if (cache.TryGetValue<WeatherNow?>(key, out var cached)) return cached;

        WeatherNow? result = null;
        if (UseGoogle)
        {
            try { result = await GoogleCurrentAsync(point, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { result = await FallbackCurrentAsync(point, ex, ct); }
        }
        else
        {
            result = (await OpenMeteoAsync(point, 1, ct)).Current;
        }

        cache.Set(key, result, TimeSpan.FromMinutes(Math.Clamp(options.Value.CurrentCacheMinutes, 1, 60)));
        return result;
    }

    public async Task<IReadOnlyList<WeatherHour>> GetHourlyForecastAsync(GeoPoint point, int hours, CancellationToken ct)
    {
        hours = Math.Clamp(hours, 1, 120);
        var key = $"wx:fc:{Round(point)}:{hours}";
        if (cache.TryGetValue<IReadOnlyList<WeatherHour>>(key, out var cached) && cached is not null) return cached;

        IReadOnlyList<WeatherHour> result;
        if (UseGoogle)
        {
            try
            {
                result = await GoogleForecastAsync(point, hours, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!options.Value.FallbackToOpenMeteo) throw;
                logger.LogWarning(ex, "Google Weather forecast failed; falling back to Open-Meteo");
                result = (await OpenMeteoAsync(point, hours, ct)).Hours;
            }
        }
        else
        {
            result = (await OpenMeteoAsync(point, hours, ct)).Hours;
        }

        cache.Set(key, result, TimeSpan.FromMinutes(Math.Clamp(options.Value.ForecastCacheMinutes, 5, 180)));
        return result;
    }

    private bool UseGoogle =>
        !string.Equals(options.Value.Provider, "OpenMeteo", StringComparison.OrdinalIgnoreCase) && googleOptions.Value.IsConfigured;

    private async Task<WeatherNow?> FallbackCurrentAsync(GeoPoint point, Exception ex, CancellationToken ct)
    {
        if (!options.Value.FallbackToOpenMeteo) throw ex;
        logger.LogWarning(ex, "Google Weather current conditions failed; falling back to Open-Meteo");
        return (await OpenMeteoAsync(point, 1, ct)).Current;
    }

    // ------------------------------------------------------------------ Google Weather API

    private async Task<WeatherNow?> GoogleCurrentAsync(GeoPoint point, CancellationToken ct)
    {
        var url = "https://weather.googleapis.com/v1/currentConditions:lookup"
                  + $"?key={Uri.EscapeDataString(googleOptions.Value.ApiKey!)}&location.latitude={Inv(point.Latitude)}&location.longitude={Inv(point.Longitude)}&unitsSystem=METRIC";
        using var response = await GoogleHttp.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var temperature = root.TryGetProperty("temperature", out var t) && t.TryGetProperty("degrees", out var deg) ? deg.GetDouble() : double.NaN;
        if (double.IsNaN(temperature)) return null;
        var observed = root.TryGetProperty("currentTime", out var time) && time.GetString() is { } s
                       && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed : DateTimeOffset.UtcNow;

        return new WeatherNow(
            Math.Round(temperature, 1),
            root.TryGetProperty("relativeHumidity", out var h) ? h.GetDouble() : 0,
            Condition(root),
            root.TryGetProperty("weatherCondition", out var c) && c.TryGetProperty("iconBaseUri", out var icon)
                ? icon.GetString() + ".svg" : null,
            observed);
    }

    private async Task<IReadOnlyList<WeatherHour>> GoogleForecastAsync(GeoPoint point, int hours, CancellationToken ct)
    {
        var results = new List<WeatherHour>(hours);
        string? pageToken = null;

        // The API returns at most 24 hours per page.
        for (var page = 0; page < 5 && results.Count < hours; page++)
        {
            var url = "https://weather.googleapis.com/v1/forecast/hours:lookup"
                      + $"?key={Uri.EscapeDataString(googleOptions.Value.ApiKey!)}&location.latitude={Inv(point.Latitude)}&location.longitude={Inv(point.Longitude)}"
                      + $"&hours={Math.Min(hours, 120)}&pageSize=24&unitsSystem=METRIC"
                      + (pageToken is null ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}");

            using var response = await GoogleHttp.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (!root.TryGetProperty("forecastHours", out var forecastHours)) break;

            foreach (var hour in forecastHours.EnumerateArray())
            {
                var start = hour.TryGetProperty("interval", out var interval) && interval.TryGetProperty("startTime", out var st)
                    ? st.GetString() : null;
                if (start is null || !DateTimeOffset.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var time)) continue;
                if (!hour.TryGetProperty("temperature", out var temp) || !temp.TryGetProperty("degrees", out var degrees)) continue;

                int? precipitation = hour.TryGetProperty("precipitation", out var precip)
                                     && precip.TryGetProperty("probability", out var prob)
                                     && prob.TryGetProperty("percent", out var percent)
                    ? percent.GetInt32() : null;

                results.Add(new WeatherHour(time, Math.Round(degrees.GetDouble(), 1),
                    hour.TryGetProperty("relativeHumidity", out var rh) ? rh.GetDouble() : 0,
                    Condition(hour), precipitation));
            }

            pageToken = root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (string.IsNullOrEmpty(pageToken)) break;
        }
        return results.Take(hours).ToList();
    }

    private static string Condition(JsonElement element) =>
        element.TryGetProperty("weatherCondition", out var condition) && condition.TryGetProperty("description", out var description)
        && description.TryGetProperty("text", out var text) ? text.GetString() ?? "—" : "—";

    // ------------------------------------------------------------------ Open-Meteo fallback (no API key)

    private async Task<(WeatherNow? Current, IReadOnlyList<WeatherHour> Hours)> OpenMeteoAsync(GeoPoint point, int hours, CancellationToken ct)
    {
        var url = $"{options.Value.OpenMeteoBaseUrl.TrimEnd('/')}/v1/forecast"
                  + $"?latitude={Inv(point.Latitude)}&longitude={Inv(point.Longitude)}"
                  + "&current=temperature_2m,relative_humidity_2m,weather_code"
                  + "&hourly=temperature_2m,relative_humidity_2m,precipitation_probability,weather_code"
                  + $"&forecast_hours={Math.Clamp(hours, 1, 120)}&timezone=GMT";

        using var response = await OpenMeteoHttp.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        WeatherNow? current = null;
        if (root.TryGetProperty("current", out var c) && c.TryGetProperty("temperature_2m", out var ct2))
        {
            current = new WeatherNow(
                Math.Round(ct2.GetDouble(), 1),
                c.TryGetProperty("relative_humidity_2m", out var ch) ? ch.GetDouble() : 0,
                WmoCode(c.TryGetProperty("weather_code", out var cw) ? cw.GetInt32() : -1),
                null,
                ParseUtc(c.TryGetProperty("time", out var time) ? time.GetString() : null) ?? DateTimeOffset.UtcNow);
        }

        var list = new List<WeatherHour>();
        if (root.TryGetProperty("hourly", out var hourly) && hourly.TryGetProperty("time", out var times))
        {
            var temps = hourly.GetProperty("temperature_2m");
            var humidity = hourly.TryGetProperty("relative_humidity_2m", out var hh) ? hh : default;
            var probability = hourly.TryGetProperty("precipitation_probability", out var pp) ? pp : default;
            var codes = hourly.TryGetProperty("weather_code", out var wc) ? wc : default;

            var index = 0;
            foreach (var time in times.EnumerateArray())
            {
                var parsed = ParseUtc(time.GetString());
                if (parsed is { } at && index < temps.GetArrayLength())
                {
                    list.Add(new WeatherHour(at, Math.Round(temps[index].GetDouble(), 1),
                        humidity.ValueKind == JsonValueKind.Array ? humidity[index].GetDouble() : 0,
                        WmoCode(codes.ValueKind == JsonValueKind.Array ? codes[index].GetInt32() : -1),
                        probability.ValueKind == JsonValueKind.Array && probability[index].ValueKind == JsonValueKind.Number
                            ? probability[index].GetInt32() : null));
                }
                index++;
            }
        }
        return (current, list);
    }

    private static DateTimeOffset? ParseUtc(string? value) =>
        value is not null && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc))
            : null;

    private static string Inv(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    private static string Round(GeoPoint p) =>
        FormattableString.Invariant($"{Math.Round(p.Latitude, 2)},{Math.Round(p.Longitude, 2)}");

    /// WMO weather interpretation codes used by Open-Meteo.
    internal static string WmoCode(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 or 77 => "Snow",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "—",
    };
}
