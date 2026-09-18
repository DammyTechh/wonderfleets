using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Options;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;
using WonderFleet.Domain.Services;

namespace WonderFleet.Application.Features.Fuel;

public interface IFuelService
{
    Task<FuelEstimateDto> EstimateAsync(FuelEstimateRequest request, CancellationToken ct);
    Task<FuelEstimateDto> EstimateForTripAsync(Guid tripId, bool persist, CancellationToken ct);
    Task<TripFuelSummaryDto> GetTripFuelAsync(Guid tripId, CancellationToken ct);
    Task<TripFuelSummaryDto> RecordActualAsync(Guid tripId, RecordFuelRequest request, CancellationToken ct);
    Task<IReadOnlyList<FuelPriceDto>> GetPricesAsync(CancellationToken ct);
    Task<FuelPriceDto> UpdatePriceAsync(FuelType fuelType, UpdateFuelPriceRequest request, CancellationToken ct);
}

/// Turns a journey into litres and naira.
///
/// The physics lives in <see cref="FuelEstimator"/>; this service is the data-gathering half:
/// it resolves the route (distance, driving time and the free-flow time that reveals traffic),
/// samples the forecast along the way for the heat and rain allowances, decides whether the load
/// needs active cooling, then prices the result with the current pump price.
///
/// Every external call is fail-soft: a missing route or forecast lowers the confidence and is
/// stated in the assumptions, but dispatch still gets a number.
internal sealed class FuelService(
    IApplicationDbContext db,
    IMapsService maps,
    IWeatherService weather,
    IClock clock,
    ICurrentActor actor,
    IAuditLogger audit,
    IOptions<FuelOptions> options,
    ILogger<FuelService> logger) : IFuelService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<FuelEstimateDto> EstimateAsync(FuelEstimateRequest request, CancellationToken ct)
    {
        Vehicle? vehicle = null;
        if (request.VehicleId is { } vehicleId)
        {
            vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vehicleId && v.DeletedAt == null, ct)
                ?? throw RequestValidationException.For(nameof(request.VehicleId), "Vehicle not found.");
        }

        var capacity = request.CapacityTonnes ?? vehicle?.CapacityTonnes
            ?? throw RequestValidationException.For(nameof(request.CapacityTonnes), "Vehicle capacity is required.");

        var context = new EstimateContext(
            FuelType: request.FuelType ?? vehicle?.FuelType ?? FuelType.Diesel,
            CapacityTonnes: capacity,
            PayloadTonnes: request.PayloadTonnes ?? capacity * 0.8m,
            OriginText: request.Origin,
            DestinationText: request.Destination,
            Origin: null,
            Destination: null,
            KnownDistanceKm: request.DistanceKm,
            Departure: request.Departure?.ToUniversalTime() ?? clock.UtcNow.AddHours(1),
            VehicleType: vehicle?.VehicleType,
            CargoMaxTemperature: request.CargoMaxTemperature,
            RefrigeratedOverride: request.Refrigerated,
            BaselineLitresPer100Km: request.BaselineLitresPer100Km ?? vehicle?.BaselineConsumptionLPer100Km,
            TankCapacityLitres: vehicle?.TankCapacityLitres,
            RoughRoadShare: request.RoughRoadShare,
            ExtraIdleHours: request.IdleHours);

        var (plan, inputs, route, climate) = await BuildAsync(context, ct);
        var price = await PriceAsync(context.FuelType, ct);
        return Map(null, null, context, plan, inputs, route, climate, price, clock.UtcNow);
    }

    public async Task<FuelEstimateDto> EstimateForTripAsync(Guid tripId, bool persist, CancellationToken ct)
    {
        var trip = await db.Trips.Include(t => t.Vehicle).FirstOrDefaultAsync(t => t.Id == tripId, ct)
            ?? throw new NotFoundException("Trip", tripId);
        var vehicle = trip.Vehicle ?? throw new NotFoundException("Vehicle", trip.VehicleId);

        // A moving truck is re-planned from where it is now; a scheduled one from its pickup point.
        var origin = trip.IsMoving && trip.LastLatitude is { } lat && trip.LastLongitude is { } lng
            ? new GeoPoint(lat, lng)
            : trip.PickupLatitude is { } plat && trip.PickupLongitude is { } plng ? new GeoPoint(plat, plng) : null;
        var destination = trip.DestinationLatitude is { } dlat && trip.DestinationLongitude is { } dlng
            ? new GeoPoint(dlat, dlng)
            : null;

        var context = new EstimateContext(
            FuelType: vehicle.FuelType,
            CapacityTonnes: vehicle.CapacityTonnes,
            PayloadTonnes: trip.EstimatedWeightTonnes,
            OriginText: trip.PickupAddress,
            DestinationText: trip.DestinationAddress,
            Origin: origin,
            Destination: destination,
            KnownDistanceKm: trip.PlannedDistanceKm,
            Departure: trip.StartedAt ?? trip.LoadingTime,
            VehicleType: vehicle.VehicleType,
            CargoMaxTemperature: trip.MaxTemperature,
            RefrigeratedOverride: null,
            BaselineLitresPer100Km: vehicle.BaselineConsumptionLPer100Km,
            TankCapacityLitres: vehicle.TankCapacityLitres,
            RoughRoadShare: null,
            ExtraIdleHours: null);

        var (plan, inputs, route, climate) = await BuildAsync(context, ct);
        var price = await PriceAsync(context.FuelType, ct);
        var now = clock.UtcNow;

        FuelEstimate? stored = null;
        if (persist)
        {
            stored = new FuelEstimate
            {
                TripId = trip.Id,
                VehicleId = vehicle.Id,
                FuelType = context.FuelType,
                DistanceKm = inputs.DistanceKm,
                DurationMinutes = inputs.DurationMinutes,
                FreeFlowMinutes = inputs.FreeFlowDurationMinutes,
                TrafficRatio = plan.TrafficRatio,
                AvgAmbientC = inputs.AverageAmbientC,
                PeakAmbientC = inputs.PeakAmbientC,
                Refrigerated = inputs.Refrigerated,
                BaseLitres = plan.BaseLitres,
                DriveLitres = plan.DriveLitres,
                ReeferLitres = plan.ReeferLitres,
                IdleLitres = plan.IdleLitres,
                TotalLitres = plan.TotalLitres,
                RecommendedLitres = plan.RecommendedLitres,
                LitresPer100Km = plan.EffectiveLitresPer100Km,
                Co2Kg = plan.Co2Kg,
                PricePerLitre = price.PricePerLitre,
                EstimatedCost = FuelEstimator.Cost(plan.TotalLitres, price.PricePerLitre),
                Currency = price.Currency,
                Confidence = plan.Confidence,
                BreakdownJson = JsonSerializer.Serialize(plan.Components, Json),
                AssumptionsJson = JsonSerializer.Serialize(plan.Assumptions, Json),
                CreatedByAdminId = actor.AdminId,
                CreatedAt = now,
            };
            db.FuelEstimates.Add(stored);

            trip.PlannedFuelLitres = plan.RecommendedLitres;
            trip.PlannedFuelCost = FuelEstimator.Cost(plan.RecommendedLitres, price.PricePerLitre);
            // Before any fuel log exists, the modelled burn is the best emission figure available.
            if (trip.ActualFuelLitres is null) trip.Co2EmissionKg = plan.Co2Kg;

            audit.Record("fuel.estimated", nameof(Trip), trip.Id,
                new { plan.TotalLitres, plan.RecommendedLitres, price.PricePerLitre, plan.Confidence });
            await db.SaveChangesAsync(ct);
        }

        return Map(stored?.Id, trip.Id, context, plan, inputs, route, climate, price, now);
    }

    public async Task<TripFuelSummaryDto> GetTripFuelAsync(Guid tripId, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().Include(t => t.Vehicle).FirstOrDefaultAsync(t => t.Id == tripId, ct)
            ?? throw new NotFoundException("Trip", tripId);

        var latest = await db.FuelEstimates.AsNoTracking()
            .Where(e => e.TripId == tripId)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // No plan yet: compute one on the fly so the page is never empty (not persisted).
        var estimate = latest is not null ? Map(latest, trip.Vehicle?.TankCapacityLitres) : await SafeEstimateAsync(tripId, ct);
        return Summary(trip, estimate);
    }

    public async Task<TripFuelSummaryDto> RecordActualAsync(Guid tripId, RecordFuelRequest request, CancellationToken ct)
    {
        var trip = await db.Trips.Include(t => t.Vehicle).FirstOrDefaultAsync(t => t.Id == tripId, ct)
            ?? throw new NotFoundException("Trip", tripId);
        var fuelType = trip.Vehicle?.FuelType ?? FuelType.Diesel;

        var cost = request.Cost;
        if (cost is null)
        {
            var price = await PriceAsync(fuelType, ct);
            cost = FuelEstimator.Cost(request.Litres, price.PricePerLitre);
        }

        trip.RecordFuelActuals(request.Litres, cost, fuelType, clock.UtcNow);
        audit.Record("fuel.recorded", nameof(Trip), tripId, new { request.Litres, cost, request.Note });
        await db.SaveChangesAsync(ct);

        return await GetTripFuelAsync(tripId, ct);
    }

    public async Task<IReadOnlyList<FuelPriceDto>> GetPricesAsync(CancellationToken ct)
    {
        var stored = await db.FuelPrices.AsNoTracking().ToListAsync(ct);
        var result = new List<FuelPriceDto>();
        foreach (var fuelType in Enum.GetValues<FuelType>())
        {
            var price = stored.FirstOrDefault(p => p.FuelType == fuelType);
            result.Add(price is not null
                ? new FuelPriceDto(fuelType.ToString(), price.PricePerLitre, price.Currency, price.Source, price.UpdatedAt)
                : new FuelPriceDto(fuelType.ToString(), Fallback(fuelType), options.Value.Currency, "Configured fallback", clock.UtcNow));
        }
        return result;
    }

    public async Task<FuelPriceDto> UpdatePriceAsync(FuelType fuelType, UpdateFuelPriceRequest request, CancellationToken ct)
    {
        var price = await db.FuelPrices.FirstOrDefaultAsync(p => p.FuelType == fuelType, ct);
        if (price is null)
        {
            price = new FuelPrice { FuelType = fuelType, Currency = options.Value.Currency };
            db.FuelPrices.Add(price);
        }

        price.PricePerLitre = request.PricePerLitre;
        price.Source = FuelText.Trimmed(request.Source);
        price.UpdatedByAdminId = actor.AdminId;
        price.UpdatedAt = clock.UtcNow;

        audit.Record("fuel_price.updated", nameof(FuelPrice), fuelType.ToString(), new { request.PricePerLitre });
        await db.SaveChangesAsync(ct);
        return new FuelPriceDto(fuelType.ToString(), price.PricePerLitre, price.Currency, price.Source, price.UpdatedAt);
    }

    // ------------------------------------------------------------------ internals

    private sealed record EstimateContext(
        FuelType FuelType, decimal CapacityTonnes, decimal PayloadTonnes,
        string? OriginText, string? DestinationText, GeoPoint? Origin, GeoPoint? Destination,
        double? KnownDistanceKm, DateTimeOffset Departure, string? VehicleType,
        decimal? CargoMaxTemperature, bool? RefrigeratedOverride, decimal? BaselineLitresPer100Km,
        decimal? TankCapacityLitres, double? RoughRoadShare, double? ExtraIdleHours);

    private sealed record RouteFacts(double DistanceKm, int DurationMinutes, int? FreeFlowMinutes);

    private sealed record ClimateFacts(double? AverageC, double? PeakC, bool RainExpected);

    private async Task<(FuelPlan Plan, FuelInputs Inputs, RouteFacts Route, ClimateFacts Climate)> BuildAsync(
        EstimateContext context, CancellationToken ct)
    {
        var origin = context.Origin ?? await LocateAsync(context.OriginText, ct);
        var destination = context.Destination ?? await LocateAsync(context.DestinationText, ct);

        var route = await RouteAsync(origin, destination, context, ct);
        var climate = await ClimateAsync(origin, destination, context.Departure, ct);

        // Reefer load: an explicitly refrigerated body, or a set point the outside air will beat.
        var refrigerated = context.RefrigeratedOverride
            ?? (IsRefrigeratedBody(context.VehicleType)
                || (context.CargoMaxTemperature is { } max && (decimal)(climate.PeakC ?? 32) - max > 5m));

        var drivingHours = route.DurationMinutes / 60d;
        var idleHours = context.ExtraIdleHours
            ?? options.Value.DefaultIdleHours + options.Value.IdleHoursPerDrivingDay * (drivingHours / 8d);

        var inputs = new FuelInputs(
            context.FuelType,
            context.CapacityTonnes,
            Math.Min(context.PayloadTonnes, context.CapacityTonnes),
            route.DistanceKm,
            route.DurationMinutes,
            route.FreeFlowMinutes,
            climate.AverageC,
            climate.PeakC,
            context.CargoMaxTemperature,
            refrigerated,
            idleHours,
            context.RoughRoadShare ?? options.Value.DefaultRoughRoadShare,
            context.BaselineLitresPer100Km,
            climate.RainExpected);

        return (FuelEstimator.Estimate(inputs), inputs, route, climate);
    }

    private async Task<GeoPoint?> LocateAsync(string? address, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        try
        {
            var result = await maps.GeocodeAsync(address.Trim(), ct);
            return result?.Location;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Geocoding failed during fuel planning");
            return null;
        }
    }

    private async Task<RouteFacts> RouteAsync(GeoPoint? origin, GeoPoint? destination, EstimateContext context, CancellationToken ct)
    {
        if (origin is not null && destination is not null)
        {
            try
            {
                var departure = context.Departure > clock.UtcNow ? context.Departure : clock.UtcNow.AddMinutes(2);
                var routes = await maps.ComputeRoutesAsync(origin, destination, departure, ct);
                if (routes.Count > 0)
                {
                    var best = routes[0];
                    return new RouteFacts(best.DistanceKm, best.DurationMinutes, best.StaticDurationMinutes);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Routes lookup failed during fuel planning; falling back to straight-line distance");
            }
        }

        // Fall back to the known distance, or a road-factored great-circle distance.
        var distance = context.KnownDistanceKm
                       ?? (origin is not null && destination is not null
                           ? GeoMath.HaversineMeters(origin.Latitude, origin.Longitude, destination.Latitude, destination.Longitude) / 1000 * 1.25
                           : 0);
        var minutes = distance > 0 ? (int)Math.Round(distance / options.Value.FallbackAverageSpeedKmh * 60) : 0;
        return new RouteFacts(Math.Round(distance, 1), minutes, null);
    }

    /// Samples the forecast at both ends and the midpoint, at the hour the truck will be there.
    private async Task<ClimateFacts> ClimateAsync(GeoPoint? origin, GeoPoint? destination, DateTimeOffset departure, CancellationToken ct)
    {
        var points = new List<GeoPoint>();
        if (origin is not null) points.Add(origin);
        if (origin is not null && destination is not null)
            points.Add(new GeoPoint((origin.Latitude + destination.Latitude) / 2, (origin.Longitude + destination.Longitude) / 2));
        if (destination is not null) points.Add(destination);
        if (points.Count == 0) return new ClimateFacts(null, null, false);

        var temperatures = new List<double>();
        var rain = false;
        foreach (var point in points)
        {
            try
            {
                var hours = await weather.GetHourlyForecastAsync(point, 48, ct);
                var sample = hours.OrderBy(hour => Math.Abs((hour.Time - departure).TotalMinutes)).FirstOrDefault();
                if (sample is null) continue;
                temperatures.Add(sample.TemperatureC);
                if (sample.PrecipitationProbability is >= 50) rain = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Forecast sample failed during fuel planning");
            }
        }

        return temperatures.Count == 0
            ? new ClimateFacts(null, null, false)
            : new ClimateFacts(Math.Round(temperatures.Average(), 1), Math.Round(temperatures.Max(), 1), rain);
    }

    private static bool IsRefrigeratedBody(string? vehicleType) =>
        vehicleType is not null
        && (vehicleType.Contains("refriger", StringComparison.OrdinalIgnoreCase)
            || vehicleType.Contains("reefer", StringComparison.OrdinalIgnoreCase)
            || vehicleType.Contains("chill", StringComparison.OrdinalIgnoreCase));

    private async Task<(decimal PricePerLitre, string Currency)> PriceAsync(FuelType fuelType, CancellationToken ct)
    {
        var price = await db.FuelPrices.AsNoTracking().FirstOrDefaultAsync(p => p.FuelType == fuelType, ct);
        return price is not null
            ? (price.PricePerLitre, price.Currency)
            : (Fallback(fuelType), options.Value.Currency);
    }

    private decimal Fallback(FuelType fuelType) =>
        fuelType == FuelType.Petrol ? options.Value.FallbackPetrolPrice : options.Value.FallbackDieselPrice;

    private async Task<FuelEstimateDto?> SafeEstimateAsync(Guid tripId, CancellationToken ct)
    {
        try
        {
            return await EstimateForTripAsync(tripId, persist: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not NotFoundException)
        {
            logger.LogWarning(ex, "Could not compute a fuel estimate for trip {TripId}", tripId);
            return null;
        }
    }

    private FuelEstimateDto Map(
        Guid? id, Guid? tripId, EstimateContext context, FuelPlan plan, FuelInputs inputs,
        RouteFacts route, ClimateFacts climate, (decimal PricePerLitre, string Currency) price, DateTimeOffset now)
    {
        var components = plan.Components
            .Select(component => new FuelComponentDto(
                component.Key, component.Label, component.Litres,
                plan.TotalLitres > 0 ? Math.Round(component.Litres / plan.TotalLitres * 100m, 1) : 0,
                component.Detail))
            .ToList();

        return new FuelEstimateDto(
            id, tripId, context.FuelType.ToString(), route.DistanceKm, route.DurationMinutes, route.FreeFlowMinutes,
            plan.TrafficRatio, FuelText.TrafficLabel(plan.TrafficRatio), climate.AverageC, climate.PeakC, inputs.Refrigerated,
            plan.BaseLitres, plan.DriveLitres, plan.ReeferLitres, plan.IdleLitres, plan.TotalLitres, plan.RecommendedLitres,
            plan.EffectiveLitresPer100Km, plan.Co2Kg, price.PricePerLitre,
            FuelEstimator.Cost(plan.TotalLitres, price.PricePerLitre),
            FuelEstimator.Cost(plan.RecommendedLitres, price.PricePerLitre),
            price.Currency, plan.Confidence,
            context.TankCapacityLitres is > 0 ? Math.Round(plan.RecommendedLitres / context.TankCapacityLitres.Value, 2) : null,
            components, plan.Assumptions, now);
    }

    private static FuelEstimateDto Map(FuelEstimate stored, decimal? tankCapacityLitres)
    {
        var components = JsonSerializer.Deserialize<List<FuelComponent>>(stored.BreakdownJson, Json) ?? [];
        var assumptions = JsonSerializer.Deserialize<List<string>>(stored.AssumptionsJson, Json) ?? [];
        return new FuelEstimateDto(
            stored.Id, stored.TripId, stored.FuelType.ToString(), stored.DistanceKm, stored.DurationMinutes, stored.FreeFlowMinutes,
            stored.TrafficRatio, FuelText.TrafficLabel(stored.TrafficRatio), stored.AvgAmbientC, stored.PeakAmbientC, stored.Refrigerated,
            stored.BaseLitres, stored.DriveLitres, stored.ReeferLitres, stored.IdleLitres, stored.TotalLitres, stored.RecommendedLitres,
            stored.LitresPer100Km, stored.Co2Kg, stored.PricePerLitre, stored.EstimatedCost,
            FuelEstimator.Cost(stored.RecommendedLitres, stored.PricePerLitre), stored.Currency, stored.Confidence,
            tankCapacityLitres is > 0 ? Math.Round(stored.RecommendedLitres / tankCapacityLitres.Value, 2) : null,
            components.Select(component => new FuelComponentDto(
                component.Key, component.Label, component.Litres,
                stored.TotalLitres > 0 ? Math.Round(component.Litres / stored.TotalLitres * 100m, 1) : 0,
                component.Detail)).ToList(),
            assumptions, stored.CreatedAt);
    }

    private static TripFuelSummaryDto Summary(Trip trip, FuelEstimateDto? estimate)
    {
        var planned = trip.PlannedFuelLitres ?? estimate?.RecommendedLitres;
        var variance = trip.ActualFuelLitres is { } actual && planned is { } plan ? actual - plan : (decimal?)null;
        return new TripFuelSummaryDto(
            trip.Id, trip.TripCode, (trip.Vehicle?.FuelType ?? FuelType.Diesel).ToString(),
            planned, trip.PlannedFuelCost ?? estimate?.RecommendedCost,
            trip.ActualFuelLitres, trip.ActualFuelCost,
            variance is null ? null : Math.Round(variance.Value, 2),
            variance is not null && planned is > 0 ? Math.Round(variance.Value / planned.Value * 100m, 1) : null,
            trip.Co2EmissionKg, estimate?.Currency ?? "NGN", trip.FuelRecordedAt, estimate);
    }
}
