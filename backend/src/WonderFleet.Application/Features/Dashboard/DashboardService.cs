using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Features.RouteAi;
using WonderFleet.Application.Features.Tracking;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Dashboard;

public sealed record ActiveTripsKpiDto(int Count, int NewThisWeek);
public sealed record AlertsKpiDto(int Open, int Critical, int Warning);
public sealed record TemperatureKpiDto(decimal? Average, decimal? DeltaFromLastHour);
public sealed record HumidityKpiDto(decimal? Average, decimal PercentAboveThreshold);
public sealed record EmissionKpiDto(decimal Tonnes, decimal? ChangePercentThisWeek, int WindowDays);

public sealed record ActiveFleetDto(Guid TripId, string FleetNumber, string VehicleCode, string Route, string CargoSummary,
    decimal? Temperature, decimal? Humidity, string SensorStatus, string TripStatus);

public sealed record DashboardDto(
    ActiveTripsKpiDto ActiveTrips,
    AlertsKpiDto Alerts,
    TemperatureKpiDto AvgTemperature,
    HumidityKpiDto Humidity,
    EmissionKpiDto Co2Emission,
    RouteAiSummaryDto RouteAi,
    IReadOnlyList<MapMarkerDto> Markers,
    StatusCountsDto StatusCounts,
    IReadOnlyList<ActiveFleetDto> ActiveFleets,
    SyncStatusDto Sync,
    int UnreadNotifications,
    DateTimeOffset GeneratedAt);

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken ct);
}

internal sealed class DashboardService(
    IApplicationDbContext db,
    TrackingQueries tracking,
    IRouteAiService routeAi,
    IClock clock) : IDashboardService
{
    private const int EmissionWindowDays = 30;

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var open = tracking.OpenTrips();
        var weekAgo = now.AddDays(-7);

        var activeCount = await open.CountAsync(ct);
        var newThisWeek = await open.CountAsync(t => t.CreatedAt >= weekAgo, ct);

        var alertGroups = await db.Alerts.AsNoTracking().Where(a => a.Status != AlertStatus.Resolved)
            .GroupBy(a => a.Severity).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int A(AlertSeverity s) => alertGroups.FirstOrDefault(x => x.Key == s)?.Count ?? 0;

        // Temperature: live average across moving trips, delta against the previous hour of readings.
        var climate = await tracking.ClimateAsync(open, ct);
        var lastHour = await AvgTempAsync(now.AddHours(-1), now, ct);
        var prevHour = await AvgTempAsync(now.AddHours(-2), now.AddHours(-1), ct);
        decimal? delta = lastHour is { } l && prevHour is { } p ? Math.Round(l - p, 1) : null;

        var moving = open.Where(t => t.Status != TripStatus.Scheduled && t.LastHumidity != null);
        var movingCount = await moving.CountAsync(ct);
        var humid = await moving.CountAsync(t => t.LastHumidity > t.MaxHumidity, ct);
        var pctAbove = movingCount == 0 ? 0 : Math.Round(humid * 100m / movingCount, 0);

        var windowStart = now.AddDays(-EmissionWindowDays);
        var co2Kg = await db.Trips.Where(t => t.StartedAt >= windowStart).SumAsync(t => (decimal?)t.Co2EmissionKg, ct) ?? 0;
        var thisWeek = await db.Trips.Where(t => t.StartedAt >= weekAgo).SumAsync(t => (decimal?)t.Co2EmissionKg, ct) ?? 0;
        var twoWeeksAgo = now.AddDays(-14);
        var lastWeek = await db.Trips.Where(t => t.StartedAt >= twoWeeksAgo && t.StartedAt < weekAgo).SumAsync(t => (decimal?)t.Co2EmissionKg, ct) ?? 0;
        decimal? change = lastWeek > 0 ? Math.Round((thisWeek - lastWeek) / lastWeek * 100, 0) : null;

        var fleets = await open.OrderByDescending(t => t.SensorStatus).ThenByDescending(t => t.LastPositionAt).Take(8)
            .Select(t => new
            {
                t.Id, t.Vehicle!.FleetNumber, t.Vehicle.VehicleCode, t.OriginLabel, t.DestinationLabel, t.EstimatedWeightTonnes,
                t.LastTemperature, t.LastHumidity, t.SensorStatus, t.Status,
                Produce = t.Produce.Select(p => p.ProduceType!.Name).ToList(),
            })
            .ToListAsync(ct);

        var unread = await db.Notifications.CountAsync(n => !n.IsRead && n.DismissedAt == null, ct);

        return new DashboardDto(
            new ActiveTripsKpiDto(activeCount, newThisWeek),
            new AlertsKpiDto(alertGroups.Sum(g => g.Count), A(AlertSeverity.Critical), A(AlertSeverity.Warning)),
            new TemperatureKpiDto(climate.AvgTemperature, delta),
            new HumidityKpiDto(climate.AvgHumidity, pctAbove),
            new EmissionKpiDto(Math.Round(co2Kg / 1000m, 2), change, EmissionWindowDays),
            await routeAi.GetSummaryAsync(ct),
            await tracking.MarkersAsync(open, includeClimate: true, ct),
            await tracking.StatusCountsAsync(open, ct),
            fleets.Select(f => new ActiveFleetDto(f.Id, f.FleetNumber, f.VehicleCode, Text.Route(f.OriginLabel, f.DestinationLabel),
                $"{(f.Produce.Count == 0 ? "Produce" : string.Join(", ", f.Produce))} • {f.EstimatedWeightTonnes:0.#} tons",
                f.LastTemperature, f.LastHumidity, f.SensorStatus.ToString(), f.Status.ToString())).ToList(),
            await tracking.SyncAsync(open, ct),
            unread,
            now);
    }

    private async Task<decimal?> AvgTempAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var avg = await db.SensorReadings.AsNoTracking()
            .Where(r => r.TripId != null && r.Temperature != null && r.RecordedAt >= from && r.RecordedAt < to)
            .AverageAsync(r => r.Temperature, ct);
        return avg is null ? null : Math.Round(avg.Value, 1);
    }
}
