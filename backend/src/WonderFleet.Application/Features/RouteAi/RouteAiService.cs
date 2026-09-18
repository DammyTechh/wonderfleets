using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Models;
using WonderFleet.Application.Features.Fleet;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.RouteAi;

public sealed record OptimizeRouteRequest(
    Guid? TripId, string? Origin, string? Destination, DateTimeOffset? Departure, IReadOnlyList<Guid>? ProduceTypeIds);

public sealed record RouteCandidateDto(
    int Index, double DistanceKm, int DurationMinutes, string EncodedPolyline, string Description, IReadOnlyList<string> Warnings,
    double? MaxForecastTempC, double? AvgForecastTempC, double HeatExposure, bool Selected);

public sealed record RouteRecommendationDto(
    Guid Id, Guid? TripId, string Origin, string Destination, DateTimeOffset RequestedDeparture, DateTimeOffset? RecommendedDeparture,
    int SelectedRouteIndex, string Summary, string HeatRiskLevel, decimal EstimatedSpoilageReductionPct,
    IReadOnlyList<string> DriverTips, string Model, IReadOnlyList<RouteCandidateDto> Routes, DateTimeOffset CreatedAt);

public sealed record RouteRecommendationListItemDto(
    Guid Id, Guid? TripId, string Route, string HeatRiskLevel, decimal EstimatedSpoilageReductionPct,
    double DistanceKm, int DurationMinutes, string Model, DateTimeOffset CreatedAt);

public sealed record RouteAiSummaryDto(int OptimizationsThisWeek, decimal AvgSpoilageReductionPct, int CriticalAlerts);

public sealed class OptimizeRouteRequestValidator : AbstractValidator<OptimizeRouteRequest>
{
    public OptimizeRouteRequestValidator()
    {
        RuleFor(x => x).Must(x => x.TripId.HasValue || (!string.IsNullOrWhiteSpace(x.Origin) && !string.IsNullOrWhiteSpace(x.Destination)))
            .WithName("TripId").WithMessage("Choose a trip or enter both origin and destination.");
        RuleFor(x => x.Origin).MaximumLength(300);
        RuleFor(x => x.Destination).MaximumLength(300);
        RuleFor(x => x.ProduceTypeIds).Must(p => p is null || p.Count <= 10);
        RuleFor(x => x.Departure).LessThan(_ => DateTimeOffset.UtcNow.AddDays(7)).When(x => x.Departure.HasValue)
            .WithMessage("Departure must be within the next 7 days (forecast horizon).");
    }
}

public interface IRouteAiService
{
    Task<RouteRecommendationDto> OptimizeAsync(OptimizeRouteRequest request, CancellationToken ct);
    Task<RouteRecommendationDto> GetAsync(Guid id, CancellationToken ct);
    Task<PagedResult<RouteRecommendationListItemDto>> ListAsync(PageQuery query, Guid? tripId, CancellationToken ct);
    Task<RouteAiSummaryDto> GetSummaryAsync(CancellationToken ct);
}

internal sealed class RouteAiService(
    IApplicationDbContext db,
    IMapsService maps,
    IWeatherService weather,
    IRouteAdvisor advisor,
    IClock clock,
    IAuditLogger audit,
    ILogger<RouteAiService> logger) : IRouteAiService
{
    private const int SamplesPerRoute = 4;
    private const string HeuristicModel = "wonderfleet-heuristic-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record StoredRoute(RouteCandidateDto Route, IReadOnlyList<WeatherHour> Samples);
    private sealed record StoredAdvice(IReadOnlyList<string> DriverTips);

    public async Task<RouteRecommendationDto> OptimizeAsync(OptimizeRouteRequest request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        Trip? trip = null;
        string originText, destinationText;
        GeoPoint origin, destination;
        List<ProduceType> produce;
        decimal? maxSafe;

        if (request.TripId is { } tripId)
        {
            trip = await db.Trips.Include(t => t.Produce).ThenInclude(p => p.ProduceType)
                .FirstOrDefaultAsync(t => t.Id == tripId, ct) ?? throw new NotFoundException("Trip", tripId);
            if (trip.IsEnded) throw new BusinessRuleException("trip.ended", "Route optimisation is only available for upcoming or active trips.");
            originText = trip.IsMoving && trip.LastLatitude is not null ? $"{trip.TripCode} current position" : trip.PickupAddress;
            destinationText = trip.DestinationAddress;
            origin = trip.IsMoving && trip.LastLatitude is { } la && trip.LastLongitude is { } lo
                ? new GeoPoint(la, lo)
                : trip.PickupLatitude is { } pa && trip.PickupLongitude is { } po ? new GeoPoint(pa, po) : await GeocodeAsync(trip.PickupAddress, ct);
            destination = trip.DestinationLatitude is { } da && trip.DestinationLongitude is { } dLo
                ? new GeoPoint(da, dLo) : await GeocodeAsync(trip.DestinationAddress, ct);
            produce = trip.Produce.Select(p => p.ProduceType).OfType<ProduceType>().ToList();
            maxSafe = trip.MaxTemperature;
        }
        else
        {
            originText = request.Origin!.Trim();
            destinationText = request.Destination!.Trim();
            origin = await GeocodeAsync(originText, ct);
            destination = await GeocodeAsync(destinationText, ct);
            var ids = request.ProduceTypeIds ?? [];
            produce = await db.ProduceTypes.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
            maxSafe = FleetService.Suggest(produce).Thresholds?.MaxTemperature
                ?? await db.AlertRules.Where(r => r.AlertType == AlertType.TemperatureBreach).Select(r => r.ThresholdValue).FirstOrDefaultAsync(ct);
        }

        var requested = request.Departure?.ToUniversalTime() ?? (trip is { IsMoving: false } && trip.LoadingTime > now ? trip.LoadingTime : now);
        var departure = requested < now.AddMinutes(2) ? now.AddMinutes(2) : requested;

        IReadOnlyList<RouteOption> options;
        try
        {
            options = await maps.ComputeRoutesAsync(origin, destination, departure, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AppException)
        {
            logger.LogWarning(ex, "Routes API failed");
            throw new ExternalServiceException("Google Routes", "Route alternatives are unavailable right now.");
        }
        if (options.Count == 0)
            throw new BusinessRuleException("route.none", "No drivable route was found between these locations.");

        var contexts = new List<RouteWeatherContext>();
        foreach (var option in options.Take(3))
            contexts.Add(new RouteWeatherContext(option, await SampleWeatherAsync(option, departure, ct)));

        var safe = maxSafe ?? 30m;
        var exposures = contexts.ToDictionary(c => c.Route.Index, c => Exposure(c.WeatherSamples, safe));
        var heuristic = Heuristic(contexts, exposures, departure, await OriginForecastAsync(origin, ct), safe);

        RouteAdvice? ai = null;
        try
        {
            ai = await advisor.AdviseAsync(new RouteAdviceRequest(originText, destinationText, departure,
                produce.Select(p => p.Name).ToList(), maxSafe, contexts), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Route advisor failed; using heuristic");
        }
        var advice = Sanitize(ai, contexts, departure, now) ?? heuristic;

        var chosen = contexts.First(c => c.Route.Index == advice.SelectedRouteIndex);
        var candidates = contexts.Select(c => ToCandidate(c, exposures[c.Route.Index], c.Route.Index == advice.SelectedRouteIndex)).ToList();

        var rec = new RouteRecommendation
        {
            TripId = trip?.Id,
            Origin = Cap(originText, 300),
            Destination = Cap(destinationText, 300),
            RequestedDeparture = departure,
            RecommendedDeparture = advice.RecommendedDeparture,
            SelectedRouteIndex = advice.SelectedRouteIndex,
            Summary = Cap(advice.Summary, 2000),
            HeatRiskLevel = advice.HeatRiskLevel,
            DistanceKm = Math.Round(chosen.Route.DistanceKm, 1),
            DurationMinutes = chosen.Route.DurationMinutes,
            EncodedPolyline = chosen.Route.EncodedPolyline,
            EstimatedSpoilageReductionPct = advice.EstimatedSpoilageReductionPct,
            RoutesJson = JsonSerializer.Serialize(contexts.Select((c, i) => new StoredRoute(candidates[i], c.WeatherSamples)).ToList(), Json),
            AdviceJson = JsonSerializer.Serialize(new StoredAdvice(advice.DriverTips), Json),
            Model = Cap(advice.Model, 60),
            CreatedAt = now,
        };
        db.RouteRecommendations.Add(rec);
        if (trip is not null && trip.PlannedDistanceKm is null) trip.PlannedDistanceKm = rec.DistanceKm;
        audit.Record("route_ai.optimized", nameof(RouteRecommendation), rec.Id, new { rec.TripId, rec.Model, rec.SelectedRouteIndex });
        await db.SaveChangesAsync(ct);

        return new RouteRecommendationDto(rec.Id, rec.TripId, rec.Origin, rec.Destination, rec.RequestedDeparture, rec.RecommendedDeparture,
            rec.SelectedRouteIndex, rec.Summary, rec.HeatRiskLevel, rec.EstimatedSpoilageReductionPct, advice.DriverTips, rec.Model,
            candidates, rec.CreatedAt);
    }

    public async Task<RouteRecommendationDto> GetAsync(Guid id, CancellationToken ct)
    {
        var r = await db.RouteRecommendations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Route recommendation", id);
        var routes = JsonSerializer.Deserialize<List<StoredRoute>>(r.RoutesJson, Json) ?? [];
        var advice = JsonSerializer.Deserialize<StoredAdvice>(r.AdviceJson, Json);
        return new RouteRecommendationDto(r.Id, r.TripId, r.Origin, r.Destination, r.RequestedDeparture, r.RecommendedDeparture,
            r.SelectedRouteIndex, r.Summary, r.HeatRiskLevel, r.EstimatedSpoilageReductionPct, advice?.DriverTips ?? [], r.Model,
            routes.Select(x => x.Route).ToList(), r.CreatedAt);
    }

    public async Task<PagedResult<RouteRecommendationListItemDto>> ListAsync(PageQuery query, Guid? tripId, CancellationToken ct)
    {
        var q = db.RouteRecommendations.AsNoTracking();
        if (tripId is { } id) q = q.Where(r => r.TripId == id);
        if (query.NormalizedSearch is { } term) q = q.Where(r => r.Origin.ToLower().Contains(term) || r.Destination.ToLower().Contains(term));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.CreatedAt).Skip(query.Skip).Take(query.PageSize)
            .Select(r => new RouteRecommendationListItemDto(r.Id, r.TripId, r.Origin + " → " + r.Destination, r.HeatRiskLevel,
                r.EstimatedSpoilageReductionPct, r.DistanceKm, r.DurationMinutes, r.Model, r.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<RouteRecommendationListItemDto>(rows, query.Page, query.PageSize, total);
    }

    public async Task<RouteAiSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var weekAgo = now.AddDays(-7);
        var monthAgo = now.AddDays(-30);
        var count = await db.RouteRecommendations.CountAsync(r => r.CreatedAt >= weekAgo, ct);
        var avg = await db.RouteRecommendations.Where(r => r.CreatedAt >= monthAgo)
            .AverageAsync(r => (decimal?)r.EstimatedSpoilageReductionPct, ct) ?? 0m;
        var critical = await db.Alerts.CountAsync(a => a.Status != AlertStatus.Resolved && a.Severity == AlertSeverity.Critical, ct);
        return new RouteAiSummaryDto(count, Math.Round(avg, 1), critical);
    }

    // ------------------------------------------------------------------ internals

    private async Task<GeoPoint> GeocodeAsync(string address, CancellationToken ct)
    {
        var result = await maps.GeocodeAsync(address, ct)
            ?? throw new BusinessRuleException("geo.not_found", $"We could not locate \"{Cap(address, 60)}\".");
        return result.Location;
    }

    private async Task<IReadOnlyList<WeatherHour>> SampleWeatherAsync(RouteOption route, DateTimeOffset departure, CancellationToken ct)
    {
        var points = Polyline.Sample(Polyline.Decode(route.EncodedPolyline), SamplesPerRoute);
        var samples = new List<WeatherHour>();
        for (var i = 0; i < points.Count; i++)
        {
            var fraction = points.Count == 1 ? 0 : i / (double)(points.Count - 1);
            var eta = departure.AddMinutes(route.DurationMinutes * fraction);
            try
            {
                var hours = await weather.GetHourlyForecastAsync(points[i], 48, ct);
                var match = hours.OrderBy(h => Math.Abs((h.Time - eta).TotalMinutes)).FirstOrDefault();
                if (match is not null) samples.Add(match with { Time = eta });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Forecast sample failed");
            }
        }
        return samples;
    }

    private async Task<IReadOnlyList<WeatherHour>> OriginForecastAsync(GeoPoint origin, CancellationToken ct)
    {
        try { return await weather.GetHourlyForecastAsync(origin, 24, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogDebug(ex, "Origin forecast failed"); return []; }
    }

    private static double Exposure(IReadOnlyList<WeatherHour> samples, decimal safeMax) =>
        Math.Round(samples.Sum(s => Math.Max(0, s.TemperatureC - (double)safeMax)), 1);

    /// Transparent fallback: fastest route after penalising heat exposure; suggests a cooler departure window when it matters.
    private static RouteAdvice Heuristic(IReadOnlyList<RouteWeatherContext> contexts, IReadOnlyDictionary<int, double> exposure,
        DateTimeOffset departure, IReadOnlyList<WeatherHour> originForecast, decimal safeMax)
    {
        var best = contexts.OrderBy(c => c.Route.DurationMinutes + exposure[c.Route.Index] * 15).First();
        var peak = best.WeatherSamples.Count == 0 ? (double?)null : best.WeatherSamples.Max(s => s.TemperatureC);
        var risk = RiskLevel(peak);

        DateTimeOffset? recommended = null;
        var shiftGain = 0d;
        if (peak >= 33 && originForecast.Count > 0)
        {
            var hours = Math.Max(1, (int)Math.Ceiling(best.Route.DurationMinutes / 60d));
            double WindowAvg(DateTimeOffset start) => originForecast
                .Where(h => h.Time >= start && h.Time < start.AddHours(hours)).Select(h => h.TemperatureC).DefaultIfEmpty(double.NaN).Average();
            var current = WindowAvg(departure);
            var candidates = originForecast.Where(h => h.Time > departure && h.Time <= departure.AddHours(12))
                .Select(h => (h.Time, Avg: WindowAvg(h.Time))).Where(x => !double.IsNaN(x.Avg)).ToList();
            if (!double.IsNaN(current) && candidates.Count > 0)
            {
                var coolest = candidates.MinBy(x => x.Avg);
                if (current - coolest.Avg >= 2)
                {
                    recommended = coolest.Time;
                    shiftGain = current - coolest.Avg;
                }
            }
        }

        var worst = exposure.Values.DefaultIfEmpty(0).Max();
        var chosenExposure = exposure[best.Route.Index];
        var reduction = (worst - chosenExposure) / Math.Max(worst, 1) * 25 + (recommended is null ? 0 : Math.Min(10, shiftGain * 2));
        var pct = Math.Round((decimal)Math.Clamp(reduction, 0, 40), 1);

        var tips = new List<string> { $"Keep cargo between the trip limits; the upper limit is {safeMax:0.#}°C." };
        if (risk != "Low") tips.Add("Plan shaded stops and avoid idling in direct sun between 12:00 and 16:00.");
        if (risk == "High") tips.Add("Open vents or use evaporative covers on long stationary periods.");
        tips.Add("Check the WonderFleet alerts after every stop.");

        var summary = $"Route {best.Route.Index + 1} ({best.Route.Description}) is recommended: {best.Route.DistanceKm:0} km, about {best.Route.DurationMinutes / 60}h {best.Route.DurationMinutes % 60}m"
            + (peak is null ? "." : $", with forecast temperatures peaking near {peak:0}°C along the way.")
            + (recommended is { } r ? $" Departing at {r.ToOffset(TimeSpan.FromHours(1)):HH:mm} WAT avoids roughly {shiftGain:0.#}°C of heat." : "");

        return new RouteAdvice(best.Route.Index, recommended, risk, pct, summary, tips, HeuristicModel);
    }

    private static RouteAdvice? Sanitize(RouteAdvice? ai, IReadOnlyList<RouteWeatherContext> contexts, DateTimeOffset departure, DateTimeOffset now)
    {
        if (ai is null || contexts.All(c => c.Route.Index != ai.SelectedRouteIndex) || string.IsNullOrWhiteSpace(ai.Summary)) return null;
        var rec = ai.RecommendedDeparture is { } r && r >= now && r <= departure.AddHours(24) ? r : (DateTimeOffset?)null;
        var level = ai.HeatRiskLevel is "Low" or "Moderate" or "High" ? ai.HeatRiskLevel : "Moderate";
        var tips = ai.DriverTips.Where(t => !string.IsNullOrWhiteSpace(t)).Take(6).Select(t => Cap(t.Trim(), 200)).ToList();
        return ai with
        {
            RecommendedDeparture = rec,
            HeatRiskLevel = level,
            EstimatedSpoilageReductionPct = Math.Round(Math.Clamp(ai.EstimatedSpoilageReductionPct, 0, 60), 1),
            DriverTips = tips,
        };
    }

    private static string RiskLevel(double? peak) => peak switch
    {
        null => "Moderate",
        >= 35 => "High",
        >= 30 => "Moderate",
        _ => "Low",
    };

    private static RouteCandidateDto ToCandidate(RouteWeatherContext c, double exposure, bool selected) => new(
        c.Route.Index, Math.Round(c.Route.DistanceKm, 1), c.Route.DurationMinutes, c.Route.EncodedPolyline, c.Route.Description, c.Route.Warnings,
        c.WeatherSamples.Count == 0 ? null : Math.Round(c.WeatherSamples.Max(s => s.TemperatureC), 1),
        c.WeatherSamples.Count == 0 ? null : Math.Round(c.WeatherSamples.Average(s => s.TemperatureC), 1),
        exposure, selected);

    private static string Cap(string s, int max) => s.Length > max ? s[..max] : s;
}
