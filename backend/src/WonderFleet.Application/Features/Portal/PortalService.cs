using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Models;
using WonderFleet.Application.Common.Options;
using WonderFleet.Application.Features.Tracking;
using WonderFleet.Application.Features.Weather;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Portal;

public sealed record StartPortalSessionRequest(string Token);

public sealed class StartPortalSessionRequestValidator : AbstractValidator<StartPortalSessionRequest>
{
    public StartPortalSessionRequestValidator() =>
        RuleFor(x => x.Token).NotEmpty().Length(20, 128).Matches("^[A-Za-z0-9_-]+$");
}

public sealed record PortalSessionDto(string AccessToken, DateTimeOffset ExpiresAt, string Audience, string OrganisationName, DateTimeOffset LinkExpiresAt, int TripCount);

public sealed record PortalHeaderDto(string OrganisationName, string Audience, DateTimeOffset LinkExpiresAt);

// ---- logistics partner view (transporter: positions, drivers, what to do)
public sealed record PortalVehicleDto(Guid TripId, string VehicleCode, string FleetNumber, string? DriverName, string DriverInitials, string Route, string TripStatus, string Status);

public sealed record PortalActionAlertDto(string Title, string Message, string Severity, string FleetNumber, DateTimeOffset At);

public sealed record LogisticsTrackingDto(
    PortalHeaderDto Header,
    IReadOnlyList<MapMarkerDto> Markers,
    StatusCountsDto StatusCounts,
    GpsMonitorDto GpsMonitor,
    DriverPoolDto DriverPool,
    IReadOnlyList<PortalActionAlertDto> ActionAlerts,
    IReadOnlyList<PointWeatherDto> WeatherAlongRoutes);

// ---- agro-processor view (cargo owner: condition of produce, ETA, history)
public sealed record AgroShipmentDto(
    Guid TripId, string TripCode, string FleetNumber, string Route, IReadOnlyList<string> Produce,
    decimal EstimatedWeightTonnes, string? PackagingType, int? UnitCount, string TripStatus, string SensorStatus,
    decimal? Temperature, decimal? Humidity, decimal MinTemperature, decimal MaxTemperature, decimal MinHumidity, decimal MaxHumidity,
    DateTimeOffset LoadingTime, DateTimeOffset ExpectedArrival, DateTimeOffset? StartedAt,
    double DistanceTravelledKm, double? PlannedDistanceKm, int? ProgressPercent,
    string LogisticsPartner, string LogisticsPartnerPhone, string? DriverName);

public sealed record AgroTrackingDto(
    PortalHeaderDto Header,
    IReadOnlyList<MapMarkerDto> Markers,
    StatusCountsDto StatusCounts,
    DeviceSummaryDto CargoConditions,
    GpsMonitorDto GpsMonitor,
    IReadOnlyList<PortalActionAlertDto> CargoAlerts,
    IReadOnlyList<PointWeatherDto> DestinationWeather);

public interface IPortalService
{
    Task<PortalSessionDto> StartSessionAsync(string token, CancellationToken ct);
    Task<PortalHeaderDto> GetHeaderAsync(CancellationToken ct);
    Task<PagedResult<PortalVehicleDto>> GetLogisticsVehiclesAsync(PageQuery query, CancellationToken ct);
    Task<LogisticsTrackingDto> GetLogisticsTrackingAsync(CancellationToken ct);
    Task<IReadOnlyList<AgroShipmentDto>> GetAgroShipmentsAsync(CancellationToken ct);
    Task<AgroTrackingDto> GetAgroTrackingAsync(CancellationToken ct);
    Task<IReadOnlyList<TrackPointDto>> GetAgroReadingsAsync(Guid tripId, int hours, CancellationToken ct);
}

internal sealed class PortalService(
    IApplicationDbContext db,
    ISecureTokenGenerator tokens,
    IJwtTokenService jwt,
    ICurrentActor actor,
    IClock clock,
    IAuditLogger audit,
    TrackingQueries queries,
    IWeatherQueryService weather,
    IOptions<ShareLinkOptions> options) : IPortalService
{
    private static readonly TripStatus[] Ended = [TripStatus.Completed, TripStatus.Cancelled];
    private const string Expired = "This tracking link has expired or is no longer available.";

    private sealed record Scope(ShareLink Link, string OrganisationName, Guid OwnerId, IReadOnlyList<Guid> TripIds);

    public async Task<PortalSessionDto> StartSessionAsync(string token, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = tokens.Hash(token);
        var link = await db.ShareLinks
            .Include(l => l.LogisticsPartner)
            .Include(l => l.AgroProcessor)
            .Include(l => l.Trips).ThenInclude(t => t.Trip)
            .FirstOrDefaultAsync(l => l.TokenHash == hash, ct);

        if (link is null || !link.IsUsable(now) || link.Trips.All(t => t.Trip is null || Ended.Contains(t.Trip.Status)))
        {
            audit.Record("portal.session_denied", "ShareLink", link?.Id, new { reason = link is null ? "unknown" : "expired" });
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException(Expired, "link_expired");
        }

        var ownerStatus = link.LogisticsPartner?.Status ?? link.AgroProcessor?.Status;
        if (ownerStatus == PartnerStatus.Suspended)
            throw new UnauthorizedException(Expired, "link_expired");

        link.RegisterAccess(now);
        var sessionEnd = Min(link.ExpiresAt, now.AddMinutes(options.Value.PortalSessionMinutes));
        var issued = jwt.CreatePortalToken(link, sessionEnd);
        audit.Record("portal.session_started", "ShareLink", link.Id);
        await db.SaveChangesAsync(ct);

        var name = link.LogisticsPartner?.CompanyName ?? link.AgroProcessor?.Name ?? "";
        return new PortalSessionDto(issued.Token, issued.ExpiresAt, link.Audience.ToString(), name, link.ExpiresAt,
            link.Trips.Count(t => t.Trip is not null && !Ended.Contains(t.Trip.Status)));
    }

    public async Task<PortalHeaderDto> GetHeaderAsync(CancellationToken ct)
    {
        var s = await ScopeAsync(null, ct);
        return Header(s);
    }

    public async Task<PagedResult<PortalVehicleDto>> GetLogisticsVehiclesAsync(PageQuery query, CancellationToken ct)
    {
        var s = await ScopeAsync(ShareAudience.LogisticsPartner, ct);
        var q = db.Trips.AsNoTracking().Where(t => s.TripIds.Contains(t.Id));
        if (query.NormalizedSearch is { } term)
            q = q.Where(t => t.Vehicle!.VehicleCode.ToLower().Contains(term) || t.Vehicle.FleetNumber.ToLower().Contains(term)
                             || (t.Driver != null && t.Driver.FullName.ToLower().Contains(term)));

        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(t => t.Vehicle!.VehicleCode).Skip(query.Skip).Take(query.PageSize)
            .Select(t => new
            {
                t.Id, t.Vehicle!.VehicleCode, t.Vehicle.FleetNumber, Driver = t.Driver != null ? t.Driver.FullName : null,
                t.OriginLabel, t.DestinationLabel, t.Status, t.SensorStatus,
            })
            .ToListAsync(ct);

        return new PagedResult<PortalVehicleDto>(rows.Select(r => new PortalVehicleDto(r.Id, r.VehicleCode, r.FleetNumber, r.Driver,
            Text.Initials(r.Driver), Text.Route(r.OriginLabel, r.DestinationLabel), r.Status.ToString(), r.SensorStatus.ToString())).ToList(),
            query.Page, query.PageSize, total);
    }

    public async Task<LogisticsTrackingDto> GetLogisticsTrackingAsync(CancellationToken ct)
    {
        var s = await ScopeAsync(ShareAudience.LogisticsPartner, ct);
        var trips = queries.OpenTrips(s.TripIds);
        var markers = await queries.MarkersAsync(trips, includeClimate: false, ct);
        var weatherPoints = markers.Take(5).Select(m => ($"{m.FleetNumber} · current position", m.Latitude, m.Longitude));

        return new LogisticsTrackingDto(
            Header(s),
            markers,
            await queries.StatusCountsAsync(trips, ct),
            await queries.GpsMonitorAsync(s.TripIds, ct),
            await queries.DriverPoolAsync(s.OwnerId, 10, ct),
            await ActionAlertsAsync(s.TripIds, ct),
            await weather.TryGetManyAsync(weatherPoints, ct));
    }

    public async Task<IReadOnlyList<AgroShipmentDto>> GetAgroShipmentsAsync(CancellationToken ct)
    {
        var s = await ScopeAsync(ShareAudience.AgroProcessor, ct);
        var trips = await db.Trips.AsNoTracking()
            .Where(t => s.TripIds.Contains(t.Id))
            .Include(t => t.Vehicle).Include(t => t.Driver).Include(t => t.LogisticsPartner)
            .Include(t => t.Produce).ThenInclude(p => p.ProduceType)
            .OrderBy(t => t.ExpectedArrival)
            .ToListAsync(ct);

        return trips.Select(t => new AgroShipmentDto(
            t.Id, t.TripCode, t.Vehicle!.FleetNumber, Text.Route(t.OriginLabel, t.DestinationLabel),
            t.Produce.Select(p => p.ProduceType!.Name).ToList(), t.EstimatedWeightTonnes, t.PackagingType?.ToString(), t.UnitCount,
            t.Status.ToString(), t.SensorStatus.ToString(), t.LastTemperature, t.LastHumidity,
            t.MinTemperature, t.MaxTemperature, t.MinHumidity, t.MaxHumidity,
            t.LoadingTime, t.ExpectedArrival, t.StartedAt, Math.Round(t.DistanceTravelledKm, 1), t.PlannedDistanceKm,
            t.PlannedDistanceKm is > 0 ? (int)Math.Min(100, Math.Round(t.DistanceTravelledKm / t.PlannedDistanceKm.Value * 100)) : null,
            t.LogisticsPartner!.CompanyName, t.LogisticsPartner.PhoneNumber, TrackingQueries.ShortName(t.Driver?.FullName))).ToList();
    }

    public async Task<AgroTrackingDto> GetAgroTrackingAsync(CancellationToken ct)
    {
        var s = await ScopeAsync(ShareAudience.AgroProcessor, ct);
        var trips = queries.OpenTrips(s.TripIds);
        var destinations = await trips
            .Where(t => t.DestinationLatitude != null && t.DestinationLongitude != null)
            .Select(t => new { t.DestinationLabel, Lat = t.DestinationLatitude!.Value, Lng = t.DestinationLongitude!.Value })
            .Distinct().Take(5).ToListAsync(ct);

        return new AgroTrackingDto(
            Header(s),
            await queries.MarkersAsync(trips, includeClimate: true, ct),
            await queries.StatusCountsAsync(trips, ct),
            await queries.DeviceSummaryAsync(trips, ct),
            await queries.GpsMonitorAsync(s.TripIds, ct),
            await ActionAlertsAsync(s.TripIds, ct),
            await weather.TryGetManyAsync(destinations.Select(d => (d.DestinationLabel, d.Lat, d.Lng)), ct));
    }

    public async Task<IReadOnlyList<TrackPointDto>> GetAgroReadingsAsync(Guid tripId, int hours, CancellationToken ct)
    {
        var s = await ScopeAsync(ShareAudience.AgroProcessor, ct);
        if (!s.TripIds.Contains(tripId)) throw new ForbiddenException();

        var since = clock.UtcNow.AddHours(-Math.Clamp(hours, 1, 72));
        var rows = await db.SensorReadings.AsNoTracking()
            .Where(r => r.TripId == tripId && r.RecordedAt >= since)
            .OrderBy(r => r.RecordedAt)
            .Select(r => new TrackPointDto(r.Latitude ?? 0, r.Longitude ?? 0, r.Temperature, r.Humidity, r.SpeedKmh, r.RecordedAt))
            .Take(5000)
            .ToListAsync(ct);

        // Downsample to at most 500 points to keep charts light on mobile data.
        if (rows.Count <= 500) return rows;
        var step = (int)Math.Ceiling(rows.Count / 500d);
        return rows.Where((_, i) => i % step == 0).ToList();
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Scope> ScopeAsync(ShareAudience? required, CancellationToken ct)
    {
        var id = actor.ShareLinkId ?? throw new UnauthorizedException(Expired, "link_expired");
        var now = clock.UtcNow;
        var link = await db.ShareLinks.AsNoTracking()
            .Include(l => l.LogisticsPartner).Include(l => l.AgroProcessor)
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null || !link.IsUsable(now)) throw new UnauthorizedException(Expired, "link_expired");
        if (required is not null && link.Audience != required) throw new ForbiddenException();

        var tripIds = await db.ShareLinkTrips.AsNoTracking()
            .Where(st => st.ShareLinkId == id && !Ended.Contains(st.Trip!.Status))
            .Select(st => st.TripId).ToListAsync(ct);
        if (tripIds.Count == 0) throw new UnauthorizedException(Expired, "link_expired");

        return link.Audience == ShareAudience.LogisticsPartner
            ? new Scope(link, link.LogisticsPartner!.CompanyName, link.LogisticsPartnerId!.Value, tripIds)
            : new Scope(link, link.AgroProcessor!.Name, link.AgroProcessorId!.Value, tripIds);
    }

    private async Task<IReadOnlyList<PortalActionAlertDto>> ActionAlertsAsync(IReadOnlyList<Guid> tripIds, CancellationToken ct) =>
        await db.Alerts.AsNoTracking()
            .Where(a => a.TripId != null && tripIds.Contains(a.TripId.Value) && a.Status != AlertStatus.Resolved)
            .OrderByDescending(a => a.Severity).ThenByDescending(a => a.LastTriggeredAt)
            .Take(10)
            .Select(a => new PortalActionAlertDto(a.Title, a.Message, a.Severity.ToString(), a.Trip!.Vehicle!.FleetNumber, a.LastTriggeredAt))
            .ToListAsync(ct);

    private static PortalHeaderDto Header(Scope s) => new(s.OrganisationName, s.Link.Audience.ToString(), s.Link.ExpiresAt);

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
