using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Application.Features.Weather;

public sealed record PointWeatherDto(string Label, double Latitude, double Longitude, double TemperatureC, double RelativeHumidity, string Condition, string? IconUri);

public sealed record WeatherIntelligenceDto(Guid? TripId, string? FleetNumber, PointWeatherDto? Origin, PointWeatherDto? Destination, PointWeatherDto? Current);

public sealed record ForecastDto(double Latitude, double Longitude, IReadOnlyList<WeatherHour> Hours);

public interface IWeatherQueryService
{
    Task<PointWeatherDto> GetCurrentAsync(double lat, double lng, CancellationToken ct);
    Task<ForecastDto> GetForecastAsync(double lat, double lng, int hours, CancellationToken ct);
    Task<WeatherIntelligenceDto> GetTripWeatherAsync(Guid? tripId, CancellationToken ct);
    /// Fail-soft batch lookup: a weather outage must never break tracking pages.
    Task<IReadOnlyList<PointWeatherDto>> TryGetManyAsync(IEnumerable<(string Label, double Lat, double Lng)> points, CancellationToken ct);
}

internal sealed class WeatherQueryService(IApplicationDbContext db, IWeatherService weather, ILogger<WeatherQueryService> logger) : IWeatherQueryService
{
    public async Task<PointWeatherDto> GetCurrentAsync(double lat, double lng, CancellationToken ct)
    {
        var now = await weather.GetCurrentAsync(new GeoPoint(lat, lng), ct)
            ?? throw new ExternalServiceException("Weather", "No weather data for this location.");
        return new PointWeatherDto("Selected location", lat, lng, now.TemperatureC, now.RelativeHumidity, now.Condition, now.IconUri);
    }

    public async Task<ForecastDto> GetForecastAsync(double lat, double lng, int hours, CancellationToken ct) =>
        new(lat, lng, await weather.GetHourlyForecastAsync(new GeoPoint(lat, lng), Math.Clamp(hours, 1, 48), ct));

    public async Task<WeatherIntelligenceDto> GetTripWeatherAsync(Guid? tripId, CancellationToken ct)
    {
        var q = db.Trips.AsNoTracking().Include(t => t.Vehicle).AsQueryable();
        var trip = tripId is { } id
            ? await q.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Trip", id)
            : await q.Where(t => t.StartedAt != null && t.CompletedAt == null && t.CancelledAt == null)
                .OrderByDescending(t => t.LastPositionAt).FirstOrDefaultAsync(ct);
        if (trip is null) return new WeatherIntelligenceDto(null, null, null, null, null);

        var points = new List<(string, double, double)>();
        if (trip.PickupLatitude is { } plat && trip.PickupLongitude is { } plng) points.Add((trip.OriginLabel, plat, plng));
        if (trip.DestinationLatitude is { } dlat && trip.DestinationLongitude is { } dlng) points.Add((trip.DestinationLabel, dlat, dlng));
        if (trip.LastLatitude is { } clat && trip.LastLongitude is { } clng) points.Add(("Current position", clat, clng));
        var results = await TryGetManyAsync(points, ct);

        return new WeatherIntelligenceDto(trip.Id, trip.Vehicle?.FleetNumber,
            results.FirstOrDefault(r => r.Label == trip.OriginLabel),
            results.FirstOrDefault(r => r.Label == trip.DestinationLabel),
            results.FirstOrDefault(r => r.Label == "Current position"));
    }

    public async Task<IReadOnlyList<PointWeatherDto>> TryGetManyAsync(IEnumerable<(string Label, double Lat, double Lng)> points, CancellationToken ct)
    {
        var tasks = points.Take(6).Select(async p =>
        {
            try
            {
                var w = await weather.GetCurrentAsync(new GeoPoint(p.Lat, p.Lng), ct);
                return w is null ? null : new PointWeatherDto(p.Label, p.Lat, p.Lng, w.TemperatureC, w.RelativeHumidity, w.Condition, w.IconUri);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Weather lookup failed for {Label}", p.Label);
                return null;
            }
        });
        return (await Task.WhenAll(tasks)).OfType<PointWeatherDto>().ToList();
    }
}
