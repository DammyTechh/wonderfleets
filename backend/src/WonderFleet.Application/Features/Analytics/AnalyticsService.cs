using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Features.Weather;
using WonderFleet.Domain.Enums;
using WonderFleet.Domain.Services;

namespace WonderFleet.Application.Features.Analytics;

public sealed record AnalyticsQuery(string? Period = "month", Guid? DeviceId = null);

public sealed record MetricDto(decimal? Value, decimal? Delta, string Unit);

public sealed record AnalyticsKpisDto(MetricDto SensorUptime, MetricDto TemperatureCompliance, MetricDto HumidityCompliance, MetricDto EstimatedSpoilage);

public sealed record SeriesPointDto(string Label, DateTimeOffset Bucket, decimal? Value, int Readings);

public sealed record TemperatureSeriesDto(IReadOnlyList<SeriesPointDto> Points, decimal ThresholdC);

public sealed record HumiditySeriesDto(IReadOnlyList<SeriesPointDto> Points, decimal SafeMin, decimal SafeMax);

public sealed record PartnerTripsDto(Guid PartnerId, string PartnerName, string PartnerCode, int Trips);

public sealed record DeviceHealthDto(
    Guid DeviceId, string Serial, string? Route, int? BatteryLevel, decimal? Temperature, decimal? Humidity,
    bool IsOnline, DateTimeOffset? LastSeenAt, string SensorStatus);

public sealed record DeviceOptionDto(Guid Id, string Label);

public sealed record AnalyticsOverviewDto(
    string Period, DateTimeOffset From, DateTimeOffset To,
    AnalyticsKpisDto Kpis,
    TemperatureSeriesDto AverageTemperature,
    HumiditySeriesDto AverageHumidity,
    IReadOnlyList<PartnerTripsDto> TripsByPartners,
    IReadOnlyList<DeviceHealthDto> DeviceHealth,
    WeatherIntelligenceDto WeatherIntelligence);

public interface IAnalyticsService
{
    Task<AnalyticsOverviewDto> GetOverviewAsync(AnalyticsQuery query, CancellationToken ct);
    Task<IReadOnlyList<DeviceOptionDto>> GetDeviceOptionsAsync(CancellationToken ct);
}

internal sealed class AnalyticsService(
    IApplicationDbContext db,
    IAnalyticsReadStore store,
    IWeatherQueryService weather,
    IClock clock) : IAnalyticsService
{
    internal static (DateTimeOffset From, DateTimeOffset To, string Bucket, string Period) Window(string? period, DateTimeOffset now) =>
        (period ?? "month").ToLowerInvariant() switch
        {
            "week" => (now.AddDays(-7), now, "day", "week"),
            "quarter" => (now.AddDays(-90), now, "week", "quarter"),
            "year" => (now.AddDays(-365), now, "month", "year"),
            "month" => (now.AddDays(-30), now, "day", "month"),
            _ => throw RequestValidationException.For("period", "Period must be week, month, quarter or year."),
        };

    public async Task<AnalyticsOverviewDto> GetOverviewAsync(AnalyticsQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var (from, to, bucket, period) = Window(query.Period, now);
        var span = to - from;
        var prevFrom = from - span;

        if (query.DeviceId is { } did && !await db.Devices.AnyAsync(d => d.Id == did, ct))
            throw new NotFoundException("Device", did);

        var uptime = await store.GetSensorUptimePercentAsync(from, to, query.DeviceId, ct);
        var uptimePrev = await store.GetSensorUptimePercentAsync(prevFrom, from, query.DeviceId, ct);
        var comp = await store.GetComplianceAsync(from, to, query.DeviceId, ct);
        var compPrev = await store.GetComplianceAsync(prevFrom, from, query.DeviceId, ct);

        var tempNow = Pct(comp.TemperatureInRange, comp.Total);
        var humNow = Pct(comp.HumidityInRange, comp.Total);
        var spoilNow = comp.Total == 0 ? (decimal?)null : SpoilageEstimator.EstimatePercent(comp.Total, comp.Warning, comp.Critical);
        var spoilPrev = compPrev.Total == 0 ? (decimal?)null : SpoilageEstimator.EstimatePercent(compPrev.Total, compPrev.Warning, compPrev.Critical);

        var kpis = new AnalyticsKpisDto(
            new MetricDto(Math.Round((decimal)uptime, 1), Delta((decimal)uptime, (decimal)uptimePrev), "%"),
            new MetricDto(tempNow, Delta(tempNow, Pct(compPrev.TemperatureInRange, compPrev.Total)), "%"),
            new MetricDto(humNow, Delta(humNow, Pct(compPrev.HumidityInRange, compPrev.Total)), "%"),
            new MetricDto(spoilNow, Delta(spoilNow, spoilPrev), "%"));

        var averages = await store.GetAveragesAsync(from, to, bucket, query.DeviceId, ct);
        var ruleThreshold = await db.AlertRules.Where(r => r.AlertType == AlertType.TemperatureBreach)
            .Select(r => r.ThresholdValue).FirstOrDefaultAsync(ct) ?? 8m;
        var threshold = averages.Where(a => a.AvgMaxTemperature.HasValue).Select(a => a.AvgMaxTemperature!.Value).DefaultIfEmpty(ruleThreshold).Average();

        var tripsInWindow = db.Trips.AsNoTracking().Where(t => t.StartedAt != null && t.StartedAt < to && (t.CompletedAt == null || t.CompletedAt > from));
        if (query.DeviceId is { } dev) tripsInWindow = tripsInWindow.Where(t => t.DeviceId == dev);
        var safeMin = await tripsInWindow.AverageAsync(t => (decimal?)t.MinHumidity, ct) ?? 40m;
        var safeMax = await tripsInWindow.AverageAsync(t => (decimal?)t.MaxHumidity, ct) ?? 75m;

        var partners = await store.GetTripsByPartnerAsync(from, to, 6, ct);

        var healthQuery = db.Devices.AsNoTracking().Where(d => d.Kind == DeviceKind.Master);
        if (query.DeviceId is { } hd) healthQuery = healthQuery.Where(d => d.Id == hd);
        var health = await healthQuery.OrderByDescending(d => d.IsOnline).ThenBy(d => d.Serial).Take(10)
            .Select(d => new
            {
                d.Id, d.Serial, d.BatteryLevel, d.LastTemperature, d.LastHumidity, d.IsOnline, d.LastSeenAt,
                Trip = db.Trips.Where(t => t.DeviceId == d.Id && t.CompletedAt == null && t.CancelledAt == null)
                    .Select(t => new { t.Id, t.OriginLabel, t.DestinationLabel, t.SensorStatus }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var weatherTripId = health.Select(h => h.Trip?.Id).FirstOrDefault(id => id.HasValue);
        var weatherInfo = await weather.GetTripWeatherAsync(query.DeviceId is null ? null : weatherTripId, ct);

        return new AnalyticsOverviewDto(period, from, to, kpis,
            new TemperatureSeriesDto(averages.Select(a => new SeriesPointDto(Label(a.Bucket, bucket), a.Bucket, Round(a.AvgTemperature, 1), a.Readings)).ToList(),
                Math.Round(threshold, 1)),
            new HumiditySeriesDto(averages.Select(a => new SeriesPointDto(Label(a.Bucket, bucket), a.Bucket, Round(a.AvgHumidity, 0), a.Readings)).ToList(),
                Math.Round(safeMin, 0), Math.Round(safeMax, 0)),
            partners.Select(p => new PartnerTripsDto(p.PartnerId, p.PartnerName, p.PartnerCode, p.Trips)).ToList(),
            health.Select(h => new DeviceHealthDto(h.Id, h.Serial,
                h.Trip is null ? null : Text.Route(h.Trip.OriginLabel, h.Trip.DestinationLabel),
                h.BatteryLevel, h.LastTemperature, h.LastHumidity, h.IsOnline, h.LastSeenAt,
                h.Trip?.SensorStatus.ToString() ?? (h.IsOnline ? "Normal" : "Offline"))).ToList(),
            weatherInfo);
    }

    public async Task<IReadOnlyList<DeviceOptionDto>> GetDeviceOptionsAsync(CancellationToken ct)
    {
        var devices = await db.Devices.AsNoTracking().Where(d => d.Kind == DeviceKind.Master).OrderBy(d => d.Serial)
            .Select(d => new { d.Id, d.Serial, Fleet = d.Vehicle != null ? d.Vehicle.FleetNumber : null })
            .ToListAsync(ct);
        return devices.Select(d => new DeviceOptionDto(d.Id, d.Fleet is null ? d.Serial : $"{d.Serial} · {d.Fleet}")).ToList();
    }

    private static decimal? Pct(int part, int total) => total == 0 ? null : Math.Round(part * 100m / total, 1);
    private static decimal? Delta(decimal? now, decimal? prev) => now is null || prev is null ? null : Math.Round(now.Value - prev.Value, 1);
    private static decimal? Round(decimal? v, int digits) => v is null ? null : Math.Round(v.Value, digits);

    private static string Label(DateTimeOffset bucket, string size)
    {
        var local = bucket.ToOffset(TimeSpan.FromHours(1));
        return size switch
        {
            "month" => local.ToString("MMM"),
            "week" => "Wk " + local.ToString("dd MMM"),
            _ => local.ToString("dd MMM"),
        };
    }
}
