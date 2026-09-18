using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Application.Features.Tracking;

public sealed record VehiclePositionDto(Guid TripId, string FleetNumber, string VehicleCode, IReadOnlyList<TrackPointDto> Track);

public sealed record TrackPointDto(double Latitude, double Longitude, decimal? Temperature, decimal? Humidity, double? SpeedKmh, DateTimeOffset RecordedAt);

public interface ITrackingService
{
    Task<LiveTrackingDto> GetLiveAsync(CancellationToken ct);
    Task<VehiclePositionDto> GetTripTrackAsync(Guid tripId, int hours, CancellationToken ct);
}

internal sealed class TrackingService(IApplicationDbContext db, TrackingQueries queries, IClock clock) : ITrackingService
{
    public async Task<LiveTrackingDto> GetLiveAsync(CancellationToken ct)
    {
        var open = queries.OpenTrips();
        return new LiveTrackingDto(
            await queries.MarkersAsync(open, includeClimate: true, ct),
            await queries.StatusCountsAsync(open, ct),
            await queries.DeviceSummaryAsync(open, ct),
            await queries.ClimateAsync(open, ct),
            await queries.OpenAlertsAsync(null, 3, ct),
            await queries.GpsMonitorAsync(null, ct),
            await queries.SyncAsync(open, ct));
    }

    public async Task<VehiclePositionDto> GetTripTrackAsync(Guid tripId, int hours, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().Include(t => t.Vehicle).FirstOrDefaultAsync(t => t.Id == tripId, ct)
            ?? throw new NotFoundException("Trip", tripId);
        var since = clock.UtcNow.AddHours(-Math.Clamp(hours, 1, 72));
        var points = await db.SensorReadings.AsNoTracking()
            .Where(r => r.TripId == tripId && r.Latitude != null && r.RecordedAt >= since)
            .OrderBy(r => r.RecordedAt)
            .Take(2000)
            .Select(r => new TrackPointDto(r.Latitude!.Value, r.Longitude!.Value, r.Temperature, r.Humidity, r.SpeedKmh, r.RecordedAt))
            .ToListAsync(ct);
        return new VehiclePositionDto(trip.Id, trip.Vehicle!.FleetNumber, trip.Vehicle.VehicleCode, points);
    }
}
