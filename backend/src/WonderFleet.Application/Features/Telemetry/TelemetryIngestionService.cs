using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Options;
using WonderFleet.Application.Features.Alerts;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;
using WonderFleet.Domain.Services;

namespace WonderFleet.Application.Features.Telemetry;

public enum IngestionOutcome { Stored, Unchanged, UnknownDevice }

public interface ITelemetryIngestionService
{
    Task<IngestionOutcome> IngestAsync(DeviceSnapshot snapshot, TelemetrySource source, CancellationToken ct);
}

/// Process-wide sync status for the dashboard banner ("All sensors transmitting – last sync 12 seconds ago").
public sealed class TelemetrySyncState
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _unknownKeys = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastStored = new();
    public DateTimeOffset? LastPollAt { get; private set; }
    public DateTimeOffset? LastSuccessfulPollAt { get; private set; }
    public DateTimeOffset? LastReadingStoredAt { get; private set; }
    public string? LastError { get; private set; }
    public int LastPollNodeCount { get; private set; }

    public IReadOnlyDictionary<string, DateTimeOffset> UnknownDeviceKeys => _unknownKeys;

    public void PollSucceeded(DateTimeOffset at, int nodes) { LastPollAt = at; LastSuccessfulPollAt = at; LastPollNodeCount = nodes; LastError = null; }
    public void PollFailed(DateTimeOffset at, string error) { LastPollAt = at; LastError = error; }
    public void ReadingStored(DateTimeOffset at) => LastReadingStoredAt = at;
    public void UnknownDevice(string key, DateTimeOffset at) => _unknownKeys[key] = at;
    public void KnownDevice(string key) => _unknownKeys.TryRemove(key, out _);

    /// True when an unchanged payload should still be stored as a heartbeat row (keeps uptime and charts continuous).
    public bool HeartbeatDue(Guid deviceId, DateTimeOffset now, TimeSpan interval) =>
        !_lastStored.TryGetValue(deviceId, out var last) || now - last >= interval;

    public void DeviceReadingStored(Guid deviceId, DateTimeOffset at)
    {
        _lastStored[deviceId] = at;
        LastReadingStoredAt = at;
    }
}

internal sealed class TelemetryIngestionService(
    IApplicationDbContext db,
    IAlertEngine alerts,
    IRealtimePublisher realtime,
    IClock clock,
    TelemetrySyncState syncState,
    IOptions<TelemetryOptions> options,
    ILogger<TelemetryIngestionService> logger) : ITelemetryIngestionService
{
    internal static readonly TripStatus[] OpenTripStatuses =
        [TripStatus.Scheduled, TripStatus.InTransit, TripStatus.Delayed, TripStatus.Stopped];

    public async Task<IngestionOutcome> IngestAsync(DeviceSnapshot snapshot, TelemetrySource source, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var device = await db.Devices.FirstOrDefaultAsync(d => d.FirebaseKey == snapshot.FirebaseKey, ct);
        if (device is null)
        {
            // Never auto-register: an unknown node could be a test node or a rogue writer.
            syncState.UnknownDevice(snapshot.FirebaseKey, now);
            return IngestionOutcome.UnknownDevice;
        }
        syncState.KnownDevice(snapshot.FirebaseKey);

        var hash = Fingerprint(snapshot);
        var unchanged = hash == device.LastPayloadHash;
        var heartbeat = TimeSpan.FromSeconds(Math.Max(30, options.Value.HeartbeatSeconds));
        if (unchanged && (!device.IsOnline || !syncState.HeartbeatDue(device.Id, now, heartbeat)))
        {
            // Unchanged payload: firmware has not written. Touch LastSeenAt at most once a minute.
            if (device.LastSeenAt is null || now - device.LastSeenAt > TimeSpan.FromMinutes(1))
            {
                device.LastSeenAt = now;
                await db.SaveChangesAsync(ct);
            }
            return IngestionOutcome.Unchanged;
        }

        var temperature = Sane(snapshot.Temperature, -40, 85, "temperature", device.Serial);
        var humidity = Sane(snapshot.Humidity, 0, 100, "humidity", device.Serial);
        var hasFix = GeoMath.IsValidFix(snapshot.Latitude, snapshot.Longitude);
        var lat = hasFix ? snapshot.Latitude : null;
        var lng = hasFix ? snapshot.Longitude : null;
        var recordedAt = snapshot.DeviceTimestamp is { } ts && ts <= now.AddMinutes(2) && ts >= now.AddDays(-2) ? ts : now;

        var wasOnline = device.IsOnline;
        device.LastPayloadHash = hash;
        device.LastSeenAt = now;
        if (!unchanged)
        {
            // Only a real write proves the unit is alive; heartbeats never flip online state (prevents alert flapping).
            device.LastChangedAt = now;
            device.IsOnline = snapshot.IsActive;
        }
        device.LastTemperature = temperature ?? device.LastTemperature;
        device.LastHumidity = humidity ?? device.LastHumidity;
        if (hasFix) { device.LastLatitude = lat; device.LastLongitude = lng; }
        if (snapshot.BatteryLevel is { } battery) device.BatteryLevel = Math.Clamp(battery, 0, 100);

        var trip = await db.Trips
            .Include(t => t.Vehicle)
            .FirstOrDefaultAsync(t => t.DeviceId == device.Id && OpenTripStatuses.Contains(t.Status), ct);

        if (trip is not null)
        {
            var movedKm = trip.ApplyTelemetry(temperature, humidity, lat, lng, recordedAt);
            if (movedKm > 0 && trip.Vehicle is not null)
            {
                trip.Co2EmissionKg = EmissionCalculator.EstimateKg(trip.DistanceTravelledKm, trip.Vehicle.CapacityTonnes);
                await alerts.ResolveOpenAsync(null, trip.Id, AlertType.Stoppage, $"{trip.Vehicle.FleetNumber} is moving again.", ct);
            }
        }

        var reading = new SensorReading
        {
            DeviceId = device.Id,
            TripId = trip?.Id,
            VehicleId = trip?.VehicleId ?? device.VehicleId,
            Temperature = temperature,
            Humidity = humidity,
            Latitude = lat,
            Longitude = lng,
            SpeedKmh = trip?.LastSpeedKmh,
            BatteryLevel = device.BatteryLevel,
            IsActive = snapshot.IsActive,
            Source = source,
            RecordedAt = recordedAt,
            ReceivedAt = now,
        };
        db.SensorReadings.Add(reading);

        if (!snapshot.IsActive)
        {
            if (trip is not null && !unchanged)
            {
                trip.SensorStatus = SensorStatus.Offline;
                if (wasOnline)
                {
                    var cfg = await alerts.GetConfigAsync(ct);
                    if (cfg.IsEnabled(AlertType.DeviceOffline))
                        await alerts.RaiseAsync(new AlertSpec(AlertType.DeviceOffline, AlertSeverity.Warning, "Device offline",
                            $"Device {device.Serial} on {trip.Vehicle?.FleetNumber} reported itself inactive. Cargo conditions are not being monitored."),
                            device, trip, ct);
                }
            }
        }
        else
        {
            if (!wasOnline)
                await alerts.ResolveOpenAsync(device.Id, null, AlertType.DeviceOffline, $"Device {device.Serial} is transmitting again.", ct);
            await alerts.EvaluateReadingAsync(device, trip, reading, ct);
        }

        await db.SaveChangesAsync(ct);
        syncState.DeviceReadingStored(device.Id, now);

        await realtime.PublishTelemetryAsync(new TelemetryEvent(
            trip?.Id, device.Id, trip?.Vehicle?.FleetNumber, lat ?? device.LastLatitude, lng ?? device.LastLongitude,
            trip?.LastSpeedKmh, temperature, humidity,
            (trip?.SensorStatus ?? (snapshot.IsActive ? SensorStatus.Normal : SensorStatus.Offline)).ToString(),
            trip?.Status.ToString(), recordedAt), ct);
        foreach (var evt in alerts.DrainEvents()) await realtime.PublishAlertAsync(evt, ct);

        return IngestionOutcome.Stored;
    }

    private decimal? Sane(decimal? value, decimal min, decimal max, string field, string serial)
    {
        if (value is null) return null;
        if (value < min || value > max)
        {
            logger.LogWarning("Discarding out-of-range {Field}={Value} from device {Serial}", field, value, serial);
            return null;
        }
        return Math.Round(value.Value, 2);
    }

    private static string Fingerprint(DeviceSnapshot s)
    {
        var raw = string.Join('|',
            s.Temperature?.ToString(CultureInfo.InvariantCulture), s.Humidity?.ToString(CultureInfo.InvariantCulture),
            s.Latitude?.ToString("R", CultureInfo.InvariantCulture), s.Longitude?.ToString("R", CultureInfo.InvariantCulture),
            s.IsActive, s.BatteryLevel, s.DeviceTimestamp?.ToUnixTimeMilliseconds());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
