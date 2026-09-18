using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Domain.Enums;
using WonderFleet.Domain.Services;

namespace WonderFleet.Application.Features.Analytics;

public sealed record ReportCatalogItemDto(string Type, string Title, string Description, string DefaultFormat, IReadOnlyList<string> Formats, string Period);

public sealed record ReportRequest(ReportType Type, string? Format, string? Month);

public interface IReportService
{
    IReadOnlyList<ReportCatalogItemDto> GetCatalog(string? month);
    Task<ReportFile> GenerateAsync(ReportRequest request, CancellationToken ct);
}

internal sealed class ReportService(
    IApplicationDbContext db,
    IAnalyticsReadStore store,
    IReportRenderer renderer,
    IAuditLogger audit,
    IClock clock) : IReportService
{
    private const int MaxLogRows = 50_000;
    private static readonly TimeSpan Wat = TimeSpan.FromHours(1);

    private static readonly (ReportType Type, string Title, string Description, string Format)[] Catalog =
    [
        (ReportType.Temperature, "Temperature report", "Daily average, minimum and maximum cargo temperature with breach counts.", "pdf"),
        (ReportType.Humidity, "Humidity report", "Daily cargo humidity profile with breach counts.", "pdf"),
        (ReportType.Co2Emission, "CO₂ emission report", "Estimated tank-to-wheel emissions per shipment.", "pdf"),
        (ReportType.MonthlyCompliance, "Monthly compliance", "Temperature and humidity compliance per shipment.", "pdf"),
        (ReportType.SensorTransmissionLog, "Sensor transmission log", "Every stored telemetry reading for audit.", "csv"),
        (ReportType.RouteSummary, "Route summary", "Shipments with schedule, distance and on-time performance.", "csv"),
        (ReportType.FuelUsage, "Fuel usage", "Planned versus actual fuel per shipment, with cost and variance.", "pdf"),
    ];

    public IReadOnlyList<ReportCatalogItemDto> GetCatalog(string? month)
    {
        var (from, _) = MonthWindow(month);
        var label = from.ToOffset(Wat).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        return Catalog.Select(c => new ReportCatalogItemDto(c.Type.ToString(), c.Title, c.Description, c.Format, ["pdf", "csv"], label)).ToList();
    }

    public async Task<ReportFile> GenerateAsync(ReportRequest request, CancellationToken ct)
    {
        var entry = Catalog.First(c => c.Type == request.Type);
        var format = (request.Format ?? entry.Format).ToLowerInvariant();
        if (format is not ("pdf" or "csv")) throw RequestValidationException.For("format", "Format must be pdf or csv.");
        var (from, to) = MonthWindow(request.Month);
        var monthLabel = from.ToOffset(Wat).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var subtitle = $"{monthLabel} · generated {clock.UtcNow.ToOffset(Wat):dd MMM yyyy, HH:mm} WAT · WonderFleet by OfeminiAgricTech";

        var table = request.Type switch
        {
            ReportType.Temperature => await ConditionAsync(entry.Title, subtitle, from, to, temperature: true, ct),
            ReportType.Humidity => await ConditionAsync(entry.Title, subtitle, from, to, temperature: false, ct),
            ReportType.Co2Emission => await EmissionsAsync(entry.Title, subtitle, from, to, ct),
            ReportType.MonthlyCompliance => await ComplianceAsync(entry.Title, subtitle, from, to, ct),
            ReportType.SensorTransmissionLog => await TransmissionLogAsync(entry.Title, subtitle, from, to, ct),
            ReportType.FuelUsage => await FuelAsync(entry.Title, subtitle, from, to, ct),
            _ => await RouteSummaryAsync(entry.Title, subtitle, from, to, ct),
        };

        var fileName = $"wonderfleet-{request.Type.ToString().ToLowerInvariant()}-{from.ToOffset(Wat):yyyy-MM}.{format}";
        audit.Record("report.generated", "Report", null, new { request.Type, format, month = monthLabel, rows = table.Rows.Count });
        await db.SaveChangesAsync(ct);
        return format == "pdf" ? renderer.RenderPdf(table, fileName) : renderer.RenderCsv(table, fileName);
    }

    private (DateTimeOffset From, DateTimeOffset To) MonthWindow(string? month)
    {
        var local = clock.UtcNow.ToOffset(Wat);
        int year = local.Year, m = local.Month;
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw RequestValidationException.For("month", "Month must be in yyyy-MM format.");
            (year, m) = (parsed.Year, parsed.Month);
        }
        var from = new DateTimeOffset(year, m, 1, 0, 0, 0, Wat);
        if (from > local) throw RequestValidationException.For("month", "Reports are only available for past or current months.");
        return (from.ToUniversalTime(), from.AddMonths(1).ToUniversalTime());
    }

    private async Task<ReportTable> ConditionAsync(string title, string subtitle, DateTimeOffset from, DateTimeOffset to, bool temperature, CancellationToken ct)
    {
        var days = await store.GetDailyStatsAsync(from, to, ct);
        var comp = await store.GetComplianceAsync(from, to, null, ct);
        var unit = temperature ? "°C" : "% RH";
        var rows = days.Select(d => (IReadOnlyList<string>)new List<string> {
            d.Day.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            d.Readings.ToString(CultureInfo.InvariantCulture),
            F(temperature ? d.AvgTemperature : d.AvgHumidity),
            F(temperature ? d.MinTemperature : d.MinHumidity),
            F(temperature ? d.MaxTemperature : d.MaxHumidity),
            (temperature ? d.TemperatureBreaches : d.HumidityBreaches).ToString(CultureInfo.InvariantCulture),
        }).ToList();
        var inRange = temperature ? comp.TemperatureInRange : comp.HumidityInRange;
        var all = days.Select(d => temperature ? d.AvgTemperature : d.AvgHumidity).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return new ReportTable(title, subtitle,
            ["Day", "Readings", $"Average ({unit})", $"Minimum ({unit})", $"Maximum ({unit})", "Out-of-range readings"],
            rows,
            [
                new("Readings", comp.Total.ToString("N0", CultureInfo.InvariantCulture)),
                new("Compliance", comp.Total == 0 ? "—" : $"{inRange * 100m / comp.Total:0.0}%"),
                new($"Monthly average", all.Count == 0 ? "—" : $"{all.Average():0.0} {unit}"),
                new("Estimated spoilage", $"{SpoilageEstimator.EstimatePercent(comp.Total, comp.Warning, comp.Critical):0.0}%"),
            ]);
    }

    private async Task<ReportTable> EmissionsAsync(string title, string subtitle, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var trips = await db.Trips.AsNoTracking()
            .Where(t => t.StartedAt >= from && t.StartedAt < to)
            .OrderBy(t => t.StartedAt)
            .Select(t => new
            {
                t.TripCode, t.Vehicle!.FleetNumber, t.Vehicle.CapacityTonnes, t.OriginLabel, t.DestinationLabel,
                Partner = t.LogisticsPartner!.CompanyName, t.DistanceTravelledKm, t.Co2EmissionKg, t.Status,
            })
            .Take(5000).ToListAsync(ct);
        var totalKg = trips.Sum(t => t.Co2EmissionKg);
        var totalKm = trips.Sum(t => t.DistanceTravelledKm);
        return new ReportTable(title, subtitle,
            ["Shipment", "Fleet", "Partner", "Route", "Capacity (t)", "Distance (km)", "Factor (kg/km)", "CO₂ (kg)", "Status"],
            trips.Select(t => (IReadOnlyList<string>)new List<string> {
                t.TripCode, t.FleetNumber, t.Partner, Text.Route(t.OriginLabel, t.DestinationLabel), F(t.CapacityTonnes),
                t.DistanceTravelledKm.ToString("0.0", CultureInfo.InvariantCulture), F(EmissionCalculator.FactorForCapacity(t.CapacityTonnes)),
                F(t.Co2EmissionKg), t.Status.ToString(),
            }).ToList(),
            [
                new("Shipments", trips.Count.ToString(CultureInfo.InvariantCulture)),
                new("Distance", $"{totalKm:N0} km"),
                new("Total CO₂", $"{totalKg / 1000m:0.00} t"),
                new("Intensity", totalKm > 0 ? $"{totalKg / (decimal)totalKm:0.00} kg/km" : "—"),
            ]);
    }

    private async Task<ReportTable> ComplianceAsync(string title, string subtitle, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = await store.GetTripComplianceAsync(from, to, ct);
        var comp = await store.GetComplianceAsync(from, to, null, ct);
        return new ReportTable(title, subtitle,
            ["Shipment", "Fleet", "Route", "Partner", "Processor", "Readings", "Temp compliance", "Humidity compliance", "Alerts", "Critical"],
            rows.Select(r => (IReadOnlyList<string>)new List<string> {
                r.TripCode, r.FleetNumber, r.Route, r.PartnerName, r.ProcessorName, r.Readings.ToString(CultureInfo.InvariantCulture),
                $"{r.TemperatureCompliancePct:0.0}%", $"{r.HumidityCompliancePct:0.0}%",
                r.Alerts.ToString(CultureInfo.InvariantCulture), r.CriticalAlerts.ToString(CultureInfo.InvariantCulture),
            }).ToList(),
            [
                new("Shipments", rows.Count.ToString(CultureInfo.InvariantCulture)),
                new("Temperature compliance", comp.Total == 0 ? "—" : $"{comp.TemperatureInRange * 100m / comp.Total:0.0}%"),
                new("Humidity compliance", comp.Total == 0 ? "—" : $"{comp.HumidityInRange * 100m / comp.Total:0.0}%"),
                new("Critical readings", comp.Critical.ToString("N0", CultureInfo.InvariantCulture)),
            ]);
    }

    private async Task<ReportTable> TransmissionLogAsync(string title, string subtitle, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = await db.SensorReadings.AsNoTracking()
            .Where(r => r.RecordedAt >= from && r.RecordedAt < to)
            .OrderBy(r => r.RecordedAt)
            .Take(MaxLogRows)
            .Join(db.Devices, r => r.DeviceId, d => d.Id, (r, d) => new
            {
                d.Serial, r.TripId, r.RecordedAt, r.ReceivedAt, r.Temperature, r.Humidity, r.Latitude, r.Longitude,
                r.SpeedKmh, r.BatteryLevel, r.IsActive, r.Source,
            })
            .ToListAsync(ct);
        var tripIds = rows.Where(r => r.TripId != null).Select(r => r.TripId!.Value).Distinct().ToList();
        var codes = await db.Trips.AsNoTracking().Where(t => tripIds.Contains(t.Id))
            .Select(t => new { t.Id, t.TripCode, t.Vehicle!.FleetNumber }).ToDictionaryAsync(t => t.Id, ct);

        return new ReportTable(title, subtitle,
            ["Recorded (WAT)", "Received (WAT)", "Device", "Shipment", "Fleet", "Temp (°C)", "Humidity (%)", "Latitude", "Longitude", "Speed (km/h)", "Battery (%)", "Active", "Source"],
            rows.Select(r =>
            {
                var trip = r.TripId is { } id && codes.TryGetValue(id, out var t) ? t : null;
                return (IReadOnlyList<string>)new List<string> {
                    r.RecordedAt.ToOffset(Wat).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    r.ReceivedAt.ToOffset(Wat).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    r.Serial, trip?.TripCode ?? "", trip?.FleetNumber ?? "", F(r.Temperature), F(r.Humidity),
                    r.Latitude?.ToString("0.000000", CultureInfo.InvariantCulture) ?? "",
                    r.Longitude?.ToString("0.000000", CultureInfo.InvariantCulture) ?? "",
                    r.SpeedKmh?.ToString("0.0", CultureInfo.InvariantCulture) ?? "",
                    r.BatteryLevel?.ToString(CultureInfo.InvariantCulture) ?? "",
                    r.IsActive ? "yes" : "no", r.Source.ToString(),
                };
            }).ToList(),
            [
                new("Rows", rows.Count.ToString("N0", CultureInfo.InvariantCulture) + (rows.Count == MaxLogRows ? " (truncated)" : "")),
                new("Devices", rows.Select(r => r.Serial).Distinct().Count().ToString(CultureInfo.InvariantCulture)),
            ]);
    }

    private async Task<ReportTable> FuelAsync(string title, string subtitle, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var trips = await db.Trips.AsNoTracking()
            .Where(t => t.LoadingTime >= from && t.LoadingTime < to)
            .OrderBy(t => t.LoadingTime)
            .Select(t => new
            {
                t.TripCode, t.Vehicle!.FleetNumber, t.Vehicle.FuelType, t.Vehicle.CapacityTonnes,
                Partner = t.LogisticsPartner!.CompanyName, t.OriginLabel, t.DestinationLabel,
                t.DistanceTravelledKm, t.PlannedDistanceKm, t.PlannedFuelLitres, t.PlannedFuelCost,
                t.ActualFuelLitres, t.ActualFuelCost, t.Co2EmissionKg, t.Status,
            })
            .Take(5000).ToListAsync(ct);

        var plannedLitres = trips.Sum(t => t.PlannedFuelLitres ?? 0);
        var actualLitres = trips.Sum(t => t.ActualFuelLitres ?? 0);
        var actualCost = trips.Sum(t => t.ActualFuelCost ?? 0);
        var reconciled = trips.Count(t => t.ActualFuelLitres is not null);
        var distance = trips.Sum(t => t.DistanceTravelledKm);

        return new ReportTable(title, subtitle,
            ["Shipment", "Fleet", "Partner", "Route", "Fuel", "Distance (km)", "Planned (L)", "Actual (L)",
             "Variance (L)", "Planned cost", "Actual cost", "L/100 km", "CO2 (kg)"],
            trips.Select(t =>
            {
                var variance = t.ActualFuelLitres is { } actual && t.PlannedFuelLitres is { } planned ? actual - planned : (decimal?)null;
                var perHundred = t.ActualFuelLitres is { } litres && t.DistanceTravelledKm > 0
                    ? litres / (decimal)t.DistanceTravelledKm * 100m
                    : (decimal?)null;
                return (IReadOnlyList<string>)new List<string>
                {
                    t.TripCode, t.FleetNumber, t.Partner, Text.Route(t.OriginLabel, t.DestinationLabel), t.FuelType.ToString(),
                    t.DistanceTravelledKm.ToString("0.0", CultureInfo.InvariantCulture),
                    F(t.PlannedFuelLitres), F(t.ActualFuelLitres), F(variance),
                    F(t.PlannedFuelCost), F(t.ActualFuelCost), F(perHundred), F(t.Co2EmissionKg),
                };
            }).ToList(),
            [
                new("Shipments", trips.Count.ToString(CultureInfo.InvariantCulture)),
                new("Fuel planned", $"{plannedLitres:N0} L"),
                new("Fuel recorded", $"{actualLitres:N0} L ({reconciled} of {trips.Count} shipments)"),
                new("Fuel spend", $"{actualCost:N0}"),
                new("Fleet average", actualLitres > 0 && distance > 0 ? $"{actualLitres / (decimal)distance * 100m:0.0} L/100 km" : "—"),
            ]);
    }

    private async Task<ReportTable> RouteSummaryAsync(string title, string subtitle, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var trips = await db.Trips.AsNoTracking()
            .Where(t => t.LoadingTime >= from && t.LoadingTime < to)
            .OrderBy(t => t.LoadingTime)
            .Select(t => new
            {
                t.TripCode, t.Vehicle!.FleetNumber, Partner = t.LogisticsPartner!.CompanyName, Processor = t.AgroProcessor!.Name,
                t.OriginLabel, t.DestinationLabel, t.Status, t.LoadingTime, t.ExpectedArrival, t.StartedAt, t.CompletedAt,
                t.DistanceTravelledKm, t.PlannedDistanceKm,
            })
            .Take(5000).ToListAsync(ct);

        string D(DateTimeOffset? v) => v?.ToOffset(Wat).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "";
        var completed = trips.Where(t => t.Status == TripStatus.Completed).ToList();
        var onTime = completed.Count(t => t.CompletedAt <= t.ExpectedArrival);

        return new ReportTable(title, subtitle,
            ["Shipment", "Fleet", "Partner", "Processor", "Route", "Status", "Loading", "Expected", "Started", "Completed", "Planned (km)", "Travelled (km)", "On time"],
            trips.Select(t => (IReadOnlyList<string>)new List<string> {
                t.TripCode, t.FleetNumber, t.Partner, t.Processor, Text.Route(t.OriginLabel, t.DestinationLabel), t.Status.ToString(),
                D(t.LoadingTime), D(t.ExpectedArrival), D(t.StartedAt), D(t.CompletedAt),
                t.PlannedDistanceKm?.ToString("0.0", CultureInfo.InvariantCulture) ?? "",
                t.DistanceTravelledKm.ToString("0.0", CultureInfo.InvariantCulture),
                t.Status != TripStatus.Completed ? "" : t.CompletedAt <= t.ExpectedArrival ? "yes" : "no",
            }).ToList(),
            [
                new("Shipments", trips.Count.ToString(CultureInfo.InvariantCulture)),
                new("Delivered", completed.Count.ToString(CultureInfo.InvariantCulture)),
                new("On-time rate", completed.Count == 0 ? "—" : $"{onTime * 100m / completed.Count:0.0}%"),
            ]);
    }

    private static string F(decimal? v) => v?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
}
