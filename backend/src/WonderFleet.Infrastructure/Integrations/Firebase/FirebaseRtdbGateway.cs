using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Features.Devices;
using WonderFleet.Domain.Entities;

namespace WonderFleet.Infrastructure.Integrations.Firebase;

/// Reads the firmware's live documents from the Realtime Database and writes cargo limits back.
/// The engineer's scratch node ("test") and other reserved nodes are never treated as devices.
internal sealed class FirebaseRtdbGateway(
    HttpClient http,
    IOptions<FirebaseOptions> options,
    ILogger<FirebaseRtdbGateway> logger) : IDeviceCloudGateway, IDisposable
{
    public void Dispose() => _credentialLock.Dispose();

    private static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/firebase.database",
        "https://www.googleapis.com/auth/userinfo.email",
    ];

    private readonly SemaphoreSlim _credentialLock = new(1, 1);
    private ServiceAccountCredential? _credential;

    public async Task<IReadOnlyList<DeviceSnapshot>> ReadAllSnapshotsAsync(CancellationToken ct)
    {
        var o = Require();
        using var response = await http.GetAsync(await UrlAsync($"{o.DevicesNode}.json", ct), ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
        if (payload is not JsonObject root) return [];

        var snapshots = new List<DeviceSnapshot>(root.Count);
        foreach (var (key, value) in root)
        {
            if (DeviceKeys.Reserved.Contains(key))
            {
                logger.LogDebug("Skipping reserved Firebase node {Key}", key);
                continue;
            }
            if (value is not JsonObject node) continue;
            var snapshot = Parse(key, node);
            if (snapshot is not null) snapshots.Add(snapshot);
        }
        return snapshots;
    }

    public async Task PushThresholdsAsync(string firebaseKey, CargoThresholds thresholds, CancellationToken ct)
    {
        var o = Require();
        if (!DeviceKeys.IsValid(firebaseKey)) throw new ArgumentException("Invalid Firebase key.", nameof(firebaseKey));

        // Field names match what the firmware already reads (see settings/TRK-0001).
        var body = new JsonObject
        {
            ["setLowTemp"] = Number(thresholds.MinTemperature),
            ["setHighTemp"] = Number(thresholds.MaxTemperature),
            ["setLowHum"] = Number(thresholds.MinHumidity),
            ["setHighHum"] = Number(thresholds.MaxHumidity),
        };

        using var request = new HttpRequestMessage(HttpMethod.Patch, await UrlAsync($"{o.SettingsNode}/{firebaseKey}.json", ct))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    // ------------------------------------------------------------------ helpers

    private FirebaseOptions Require() =>
        options.Value.IsConfigured ? options.Value : throw new InvalidOperationException("Firebase:DatabaseUrl is not configured.");

    /// Integers where possible: some firmware SDK calls (getInt) do not accept fractional JSON.
    private static JsonValue Number(decimal value) =>
        value == decimal.Truncate(value) ? JsonValue.Create((int)value) : JsonValue.Create(Math.Round((double)value, 1));

    private async Task<string> UrlAsync(string path, CancellationToken ct)
    {
        var o = options.Value;
        var url = $"{o.DatabaseUrl!.TrimEnd('/')}/{path}";
        if (o.ServiceAccountJson is not null and not "")
            return $"{url}?access_token={Uri.EscapeDataString(await AccessTokenAsync(ct))}";
        if (!string.IsNullOrWhiteSpace(o.DatabaseSecret))
            return $"{url}?auth={Uri.EscapeDataString(o.DatabaseSecret)}";
        if (o.AllowUnauthenticated) return url;
        throw new InvalidOperationException("Firebase credentials are missing. Set Firebase:ServiceAccountJson or Firebase:DatabaseSecret.");
    }

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        // ServiceAccountCredential caches and refreshes the token internally.
        if (_credential is null)
        {
            await _credentialLock.WaitAsync(ct);
            try
            {
                _credential ??= CreateCredential(options.Value.ServiceAccountJson!);
            }
            finally
            {
                _credentialLock.Release();
            }
        }
        return await _credential.GetAccessTokenForRequestAsync(cancellationToken: ct);
    }

    private static ServiceAccountCredential CreateCredential(string configured)
    {
        var json = configured.Trim();
        if (!json.StartsWith('{'))
        {
            // Environment variables often carry the JSON base64-encoded to avoid newline mangling.
            try { json = Encoding.UTF8.GetString(Convert.FromBase64String(json)); }
            catch (FormatException) { /* fall through: let the parser report it */ }
        }
        var credential = GoogleCredential.FromJson(json).CreateScoped(Scopes);
        return credential.UnderlyingCredential as ServiceAccountCredential
            ?? throw new InvalidOperationException("Firebase:ServiceAccountJson must be a service account key.");
    }

    internal static DeviceSnapshot? Parse(string key, JsonObject node)
    {
        var temperature = Decimal(node, "temp", "temperature", "t");
        var humidity = Decimal(node, "hum", "humidity", "h");
        var latitude = Double(node, "lat", "latitude");
        var longitude = Double(node, "lng", "lon", "long", "longitude");
        var active = Bool(node, "isActive", "active", "online") ?? true;
        var battery = Int(node, "battery", "bat", "batteryLevel", "batt");
        var timestamp = Timestamp(node, "ts", "timestamp", "updatedAt", "time");

        if (temperature is null && humidity is null && latitude is null && longitude is null) return null;
        return new DeviceSnapshot(key, temperature, humidity, latitude, longitude, active,
            battery is null ? null : Math.Clamp(battery.Value, 0, 100), timestamp);
    }

    private static JsonNode? Find(JsonObject node, params string[] names)
    {
        foreach (var name in names)
            foreach (var (key, value) in node)
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && value is not null)
                    return value;
        return null;
    }

    private static decimal? Decimal(JsonObject node, params string[] names) =>
        Raw(node, names) is { } s && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? Double(JsonObject node, params string[] names) =>
        Raw(node, names) is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static int? Int(JsonObject node, params string[] names) =>
        Raw(node, names) is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (int)Math.Round(v) : null;

    private static bool? Bool(JsonObject node, params string[] names)
    {
        var raw = Raw(node, names);
        if (raw is null) return null;
        if (bool.TryParse(raw, out var b)) return b;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return n != 0;
        return null;
    }

    /// Accepts epoch seconds, epoch milliseconds or an ISO-8601 string.
    private static DateTimeOffset? Timestamp(JsonObject node, params string[] names)
    {
        var raw = Raw(node, names);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            var value = epoch > 4_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                : DateTimeOffset.FromUnixTimeSeconds(epoch);
            return value.Year < 2020 ? null : value;
        }
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? Raw(JsonObject node, string[] names) => Find(node, names) switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var s) => s,
        { } other => other.ToJsonString().Trim('"'),
    };
}
