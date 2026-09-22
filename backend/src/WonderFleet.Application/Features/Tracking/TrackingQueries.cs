using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Features.Telemetry;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Tracking;

/// Shared read-side building blocks for admin tracking and both partner portals.
internal sealed class TrackingQueries(IApplicationDbContext db, IClock clock, TelemetrySyncState sync)
{
    public static readonly TimeSpan Wat = TimeSpan.FromHours(1);

    public IQueryable<Trip> OpenTrips(IReadOnlyCollection<Guid>? scope = null)
    {
        var q = db.Trips.AsNoTracking().Where(t => TelemetryIngestionService.OpenTripStatuses.Contains(t.Status));
        return scope is null ? q : q.Where(t => scope.Contains(t.Id));
    }

    public async Task<IReadOnlyList<MapMarkerDto>> MarkersAsync(IQueryable<Trip> trips, bool includeClimate, CancellationToken ct)
    {
        var rows = await trips
            .Where(t => t.LastLatitude != null && t.LastLongitude != null)
            .OrderByDescending(t => t.LastPositionAt)
            .Take(500)
            .Select(t => new
            {
                t.Id, t.TripCode, t.Status, t.SensorStatus, t.OriginLabel, t.DestinationLabel,
                t.LastLatitude, t.LastLongitude, t.LastSpeedKmh, t.LastPositionAt, t.LastTemperature, t.LastHumidity,
                Fleet = t.Vehicle!.FleetNumber, Code = t.Vehicle.VehicleCode, Driver = t.Driver != null ? t.Driver.FullName : null,
            })
            .ToListAsync(ct);

        return rows.Select(r => new MapMarkerDto(r.Id, r.TripCode, r.Fleet, r.Code, r.LastLatitude!.Value, r.LastLongitude!.Value,
            r.Status.ToString(), r.SensorStatus.ToString(), ShortName(r.Driver), Text.Route(r.OriginLabel, r.DestinationLabel),
            includeClimate ? r.LastTemperature : null, includeClimate ? r.LastHumidity : null,
            r.LastSpeedKmh, r.LastPositionAt)).ToList();
    }

    public async Task<StatusCountsDto> StatusCountsAsync(IQueryable<Trip> trips, CancellationToken ct)
    {
        var grouped = await trips
            .GroupBy(t => new { t.Status, Online = t.Device != null && t.Device.IsOnline })
            .Select(g => new { g.Key.Status, g.Key.Online, Count = g.Count() })
            .ToListAsync(ct);
        return new StatusCountsDto(
            Live: grouped.Where(g => g.Online && g.Status != TripStatus.Scheduled).Sum(g => g.Count),
            InTransit: grouped.Where(g => g.Status == TripStatus.InTransit).Sum(g => g.Count),
            Stopped: grouped.Where(g => g.Status == TripStatus.Stopped).Sum(g => g.Count),
            Delay: grouped.Where(g => g.Status == TripStatus.Delayed).Sum(g => g.Count));
    }

    public async Task<DeviceSummaryDto> DeviceSummaryAsync(IQueryable<Trip> trips, CancellationToken ct)
    {
        var grouped = await trips.GroupBy(t => t.SensorStatus).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int C(SensorStatus s) => grouped.FirstOrDefault(g => g.Key == s)?.Count ?? 0;
        return new DeviceSummaryDto(C(SensorStatus.Normal), C(SensorStatus.Warning), C(SensorStatus.Critical), C(SensorStatus.Offline));
    }

    public async Task<GpsMonitorDto> GpsMonitorAsync(IReadOnlyCollection<Guid>? scope, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var trips = OpenTrips(scope);
        var activeSignals = await trips.CountAsync(t => t.Device != null && t.Device.IsOnline, ct);

        var readings = db.SensorReadings.AsNoTracking().Where(r => r.TripId != null && r.Latitude != null && r.RecordedAt > now.AddHours(-6));
        if (scope is not null) readings = readings.Where(r => scope.Contains(r.TripId!.Value));

        var recent = await readings
            .OrderByDescending(r => r.RecordedAt)
            .Take(3)
            .Join(db.Trips, r => r.TripId, t => t.Id, (r, t) => new
            {
                Code = t.Vehicle!.VehicleCode, t.OriginLabel, t.DestinationLabel, r.SpeedKmh, r.RecordedAt,
            })
            .ToListAsync(ct);

        var local = now.ToOffset(Wat);
        return new GpsMonitorDto(activeSignals, now, local.ToString("HH:mm:ss"), "WAT (UTC+1)",
            recent.Select(r => new RecentPositionDto(r.Code, Text.Route(r.OriginLabel, r.DestinationLabel), r.SpeedKmh, r.RecordedAt)).ToList());
    }

    public async Task<IReadOnlyList<AlertTriggerDto>> OpenAlertsAsync(IReadOnlyCollection<Guid>? scope, int take, CancellationToken ct)
    {
        var q = db.Alerts.AsNoTracking().Where(a => a.Status != AlertStatus.Resolved);
        if (scope is not null) q = q.Where(a => a.TripId != null && scope.Contains(a.TripId.Value));
        var rows = await q.OrderByDescending(a => a.Severity).ThenByDescending(a => a.LastTriggeredAt).Take(take)
            .Select(a => new
            {
                a.Id, a.Title, a.Severity, a.LastTriggeredAt,
                Vehicle = a.Trip != null ? a.Trip.Vehicle!.VehicleCode : null,
                Serial = a.Device != null ? a.Device.Serial : null,
                Origin = a.Trip != null ? a.Trip.OriginLabel : null,
                Destination = a.Trip != null ? a.Trip.DestinationLabel : null,
            })
            .ToListAsync(ct);
        return rows.Select(a => new AlertTriggerDto(a.Id, a.Title, a.Severity.ToString(), a.Vehicle, a.Serial,
            a.Origin is null ? "—" : Text.Route(a.Origin, a.Destination!), a.LastTriggeredAt)).ToList();
    }

    public async Task<FleetClimateDto> ClimateAsync(IQueryable<Trip> trips, CancellationToken ct)
    {
        // Plain aggregates rather than GroupBy(constant).FirstOrDefault(): same result, and EF
        // no longer logs "First without OrderBy" on every dashboard load.
        var moving = trips.Where(t => t.Status != TripStatus.Scheduled);
        var temperature = await moving.AverageAsync(t => t.LastTemperature, ct);
        var humidity = await moving.AverageAsync(t => t.LastHumidity, ct);
        var hot = await moving.CountAsync(t => t.LastTemperature > t.MaxTemperature, ct);
        var humid = await moving.CountAsync(t => t.LastHumidity > t.MaxHumidity, ct);
        return new FleetClimateDto(
            temperature is null ? null : Math.Round(temperature.Value, 1),
            hot == 0 ? "Within range" : "Above limit",
            humidity is null ? null : Math.Round(humidity.Value, 0),
            humid == 0 ? "Within range" : "Above limit");
    }

    public async Task<SyncStatusDto> SyncAsync(IQueryable<Trip> trips, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var moving = trips.Where(t => t.Status != TripStatus.Scheduled && t.DeviceId != null);
        var expected = await moving.CountAsync(ct);
        var online = await moving.CountAsync(t => t.Device!.IsOnline, ct);
        var last = sync.LastReadingStoredAt ?? await db.SensorReadings.AsNoTracking().MaxAsync(r => (DateTimeOffset?)r.ReceivedAt, ct);
        var seconds = last is null ? -1 : (int)Math.Max(0, (now - last.Value).TotalSeconds);
        var all = expected == online;
        var ago = seconds < 0 ? "no data yet" : seconds < 90 ? $"{seconds} seconds ago" : $"{seconds / 60} minutes ago";
        var message = all
            ? $"All sensors transmitting - last sync {ago}"
            : $"{online} of {expected} sensors transmitting - last sync {ago}";
        return new SyncStatusDto(all, online, expected, last, seconds, message);
    }

    public async Task<DriverPoolDto> DriverPoolAsync(Guid partnerId, int take, CancellationToken ct)
    {
        var drivers = db.Drivers.AsNoTracking().Where(d => d.LogisticsPartnerId == partnerId);
        var counts = await drivers.GroupBy(d => d.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var list = await drivers.OrderBy(d => d.Status).ThenBy(d => d.FullName).Take(take)
            .Select(d => new
            {
                d.Id, d.FullName, d.Status,
                Assignment = db.Trips.Where(t => t.DriverId == d.Id && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status))
                    .Select(t => t.Vehicle!.FleetNumber + " . " + t.Vehicle.VehicleType).FirstOrDefault(),
            })
            .ToListAsync(ct);
        int C(DriverStatus s) => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0;
        return new DriverPoolDto(C(DriverStatus.OnTrip), C(DriverStatus.Available), C(DriverStatus.OffDuty),
            list.Select(d => new DriverPoolItemDto(d.Id, d.FullName, Text.Initials(d.FullName), d.Assignment ?? "Unassigned", d.Status.ToString())).ToList());
    }

    /// "John Adeyemi" → "John A." (portals never need full driver identities).
    public static string? ShortName(string? full)
    {
        if (string.IsNullOrWhiteSpace(full)) return null;
        var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0] : $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.";
    }
}
