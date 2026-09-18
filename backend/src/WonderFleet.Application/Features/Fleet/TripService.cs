using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Models;
using WonderFleet.Application.Features.Alerts;
using WonderFleet.Application.Features.Notifications;
using WonderFleet.Application.Features.ShareLinks;
using WonderFleet.Application.Features.Telemetry;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Fleet;

public interface ITripService
{
    Task<PagedResult<TripSummaryDto>> ListAsync(TripListQuery query, CancellationToken ct);
    Task<TripDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<TripDetailDto> StartAsync(Guid id, CancellationToken ct);
    Task<TripDetailDto> CompleteAsync(Guid id, CancellationToken ct);
    Task<TripDetailDto> CancelAsync(Guid id, CancelTripRequest request, CancellationToken ct);
    Task<TripDetailDto> UpdateThresholdsAsync(Guid id, UpdateThresholdsRequest request, CancellationToken ct);
    Task<TripDetailDto> AssignAsync(Guid id, AssignTripResourcesRequest request, CancellationToken ct);
}

internal sealed class TripService(
    IApplicationDbContext db,
    IClock clock,
    IAuditLogger audit,
    IAlertEngine alerts,
    IShareLinkService shareLinks,
    IRealtimePublisher realtime,
    INotificationComposer notify,
    ThresholdPublisher thresholds,
    TripNotifications tripNotifications) : ITripService
{
    public async Task<PagedResult<TripSummaryDto>> ListAsync(TripListQuery query, CancellationToken ct)
    {
        var q = db.Trips.AsNoTracking();
        if (query.Status is { } status) q = q.Where(t => t.Status == status);
        if (query.OpenOnly) q = q.Where(t => TelemetryIngestionService.OpenTripStatuses.Contains(t.Status));
        if (query.PartnerId is { } pid) q = q.Where(t => t.LogisticsPartnerId == pid);
        if (query.AgroProcessorId is { } aid) q = q.Where(t => t.AgroProcessorId == aid);
        if (query.VehicleId is { } vid) q = q.Where(t => t.VehicleId == vid);
        if (query.From is { } from) { var f = from.ToUniversalTime(); q = q.Where(t => t.LoadingTime >= f); }
        if (query.To is { } to) { var u = to.ToUniversalTime(); q = q.Where(t => t.LoadingTime < u); }
        if (query.NormalizedSearch is { } term)
            q = q.Where(t => t.TripCode.ToLower().Contains(term) || t.Vehicle!.FleetNumber.ToLower().Contains(term)
                || t.Vehicle.VehicleCode.ToLower().Contains(term) || t.OriginLabel.ToLower().Contains(term)
                || t.DestinationLabel.ToLower().Contains(term) || t.AgroProcessor!.Name.ToLower().Contains(term)
                || t.LogisticsPartner!.CompanyName.ToLower().Contains(term));

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(t => t.CreatedAt).Skip(query.Skip).Take(query.PageSize)
            .Select(t => new
            {
                t.Id, t.TripCode, t.Vehicle!.FleetNumber, t.Vehicle.VehicleCode, t.OriginLabel, t.DestinationLabel, t.Status, t.SensorStatus,
                Partner = t.LogisticsPartner!.CompanyName, Processor = t.AgroProcessor!.Name,
                Driver = t.Driver != null ? t.Driver.FullName : null,
                Produce = t.Produce.Select(p => p.ProduceType!.Name).ToList(),
                t.EstimatedWeightTonnes, t.LoadingTime, t.ExpectedArrival, t.StartedAt, t.CompletedAt, t.LastTemperature, t.LastHumidity,
            })
            .ToListAsync(ct);

        return new PagedResult<TripSummaryDto>(rows.Select(t => new TripSummaryDto(
            t.Id, t.TripCode, t.FleetNumber, t.VehicleCode, Text.Route(t.OriginLabel, t.DestinationLabel), t.Status.ToString(),
            t.SensorStatus.ToString(), t.Partner, t.Processor, t.Driver, t.Produce, t.EstimatedWeightTonnes,
            t.LoadingTime, t.ExpectedArrival, t.StartedAt, t.CompletedAt, t.LastTemperature, t.LastHumidity)).ToList(),
            query.Page, query.PageSize, total);
    }

    public async Task<TripDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await LoadAsync(id, tracking: false, ct);
        var now = clock.UtcNow;
        var openAlerts = await db.Alerts.CountAsync(a => a.TripId == id && a.Status != AlertStatus.Resolved, ct);
        var links = await db.ShareLinkTrips.CountAsync(st => st.TripId == id && st.ShareLink!.RevokedAt == null && st.ShareLink.ExpiresAt > now, ct);
        return Map(t, openAlerts, links);
    }

    public async Task<TripDetailDto> StartAsync(Guid id, CancellationToken ct)
    {
        var trip = await LoadAsync(id, tracking: true, ct);
        var now = clock.UtcNow;
        if (trip.DeviceId is null)
            throw new BusinessRuleException("trip.no_device", "Assign a monitoring device before starting the trip.");
        trip.Start(now);
        if (trip.Driver is not null) trip.Driver.Status = DriverStatus.OnTrip;
        tripNotifications.Dispatched(trip);
        audit.Record("trip.started", nameof(Trip), id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TripDetailDto> CompleteAsync(Guid id, CancellationToken ct)
    {
        var trip = await LoadAsync(id, tracking: true, ct);
        var now = clock.UtcNow;
        trip.Complete(now);
        await ReleaseAsync(trip, "Trip completed.", ct);
        if (trip.Driver is not null) trip.Driver.CompletedTrips++;
        tripNotifications.Delivered(trip, now);
        audit.Record("trip.completed", nameof(Trip), id, new { trip.DistanceTravelledKm, trip.Co2EmissionKg });
        await CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TripDetailDto> CancelAsync(Guid id, CancelTripRequest request, CancellationToken ct)
    {
        var trip = await LoadAsync(id, tracking: true, ct);
        trip.Cancel(clock.UtcNow);
        await ReleaseAsync(trip, $"Trip cancelled: {request.Reason.Trim()}", ct);
        notify.InApp(NotificationCategory.Partner, "Shipment cancelled",
            $"{trip.TripCode} ({Text.Route(trip.OriginLabel, trip.DestinationLabel)}) was cancelled. Reason: {request.Reason.Trim()}",
            nameof(Trip), id, trip.LogisticsPartnerId);
        audit.Record("trip.cancelled", nameof(Trip), id, new { request.Reason });
        await CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TripDetailDto> UpdateThresholdsAsync(Guid id, UpdateThresholdsRequest request, CancellationToken ct)
    {
        var trip = await LoadAsync(id, tracking: true, ct);
        if (trip.IsEnded) throw new BusinessRuleException("trip.ended", "Limits cannot change after the trip has ended.");
        var before = trip.Thresholds;
        trip.SetThresholds(new CargoThresholds(request.MinTemperature, request.MaxTemperature, request.MinHumidity, request.MaxHumidity));
        audit.Record("trip.thresholds_changed", nameof(Trip), id, new { before, after = trip.Thresholds });
        await db.SaveChangesAsync(ct);
        if (trip.Device is not null) await thresholds.TryPushAsync(trip.Device, trip.Thresholds, trip.TripCode, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TripDetailDto> AssignAsync(Guid id, AssignTripResourcesRequest request, CancellationToken ct)
    {
        var trip = await LoadAsync(id, tracking: true, ct);
        if (trip.IsEnded) throw new BusinessRuleException("trip.ended", "An ended trip cannot be changed.");
        var now = clock.UtcNow;
        var pushDevice = false;

        if (request.DriverId is { } driverId && driverId != trip.DriverId)
        {
            var driver = await AssignmentRules.LoadDriverAsync(db, driverId, trip.LogisticsPartnerId, trip.Id, ct);
            if (trip.Driver is not null && trip.Driver.Status == DriverStatus.OnTrip) trip.Driver.Status = DriverStatus.Available;
            trip.DriverId = driver.Id;
            trip.Driver = driver;
            if (trip.IsMoving) driver.Status = DriverStatus.OnTrip;
        }

        if (request.DeviceId is { } deviceId && deviceId != trip.DeviceId)
        {
            var device = await AssignmentRules.LoadDeviceAsync(db, deviceId, trip.Id, ct);
            if (trip.DeviceId is { } oldDevice)
                await alerts.ResolveOpenAsync(oldDevice, null, AlertType.DeviceOffline, "Device replaced on this trip.", ct);
            trip.DeviceId = device.Id;
            trip.Device = device;
            trip.SensorStatus = SensorStatus.Offline;
            device.VehicleId = trip.VehicleId;
            pushDevice = true;
        }

        if (request.ExpectedArrival is { } eta)
        {
            var wasDelayed = trip.Status == TripStatus.Delayed;
            trip.Reschedule(eta.ToUniversalTime(), now);
            if (wasDelayed && trip.Status != TripStatus.Delayed)
                await alerts.ResolveOpenAsync(null, trip.Id, AlertType.Delay, $"New ETA {TripNotifications.Wat(trip.ExpectedArrival)}.", ct);
        }

        audit.Record("trip.reassigned", nameof(Trip), id, new { request.DriverId, request.DeviceId, request.ExpectedArrival });
        await CommitAsync(ct);
        if (pushDevice && trip.Device is not null) await thresholds.TryPushAsync(trip.Device, trip.Thresholds, trip.TripCode, ct);
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task ReleaseAsync(Trip trip, string note, CancellationToken ct)
    {
        if (trip.Driver is not null && trip.Driver.Status == DriverStatus.OnTrip) trip.Driver.Status = DriverStatus.Available;
        await alerts.ResolveAllForTripAsync(trip.Id, note, ct);
    }

    /// Saves, then kills share links whose trips have all ended, then publishes realtime alert updates.
    private async Task CommitAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        if (await shareLinks.ExpireLinksForEndedTripsAsync(ct) > 0) await db.SaveChangesAsync(ct);
        foreach (var evt in alerts.DrainEvents()) await realtime.PublishAlertAsync(evt, ct);
    }

    private async Task<Trip> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var q = db.Trips
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.Device)
            .Include(t => t.LogisticsPartner)
            .Include(t => t.AgroProcessor).ThenInclude(p => p!.Contacts)
            .Include(t => t.Produce).ThenInclude(p => p.ProduceType)
            .AsSplitQuery();
        if (!tracking) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Trip", id);
    }

    private static TripDetailDto Map(Trip t, int openAlerts, int links) => new(
        t.Id, t.TripCode, t.Status.ToString(), t.SensorStatus.ToString(),
        t.VehicleId, t.Vehicle!.VehicleCode, t.Vehicle.FleetNumber, t.Vehicle.VehicleType, t.Vehicle.CapacityTonnes,
        t.LogisticsPartnerId, t.LogisticsPartner!.CompanyName, t.LogisticsPartner.PhoneNumber,
        t.AgroProcessorId, t.AgroProcessor!.Name,
        t.DriverId, t.Driver?.FullName, t.Driver?.PhoneNumber,
        t.DeviceId, t.Device?.Serial, t.Device?.IsOnline ?? false, t.Device?.BatteryLevel,
        t.Produce.Where(p => p.ProduceType is not null).Select(p => new ProduceTypeDto(p.ProduceTypeId, p.ProduceType!.Name,
            p.ProduceType.DefaultMinTemperature, p.ProduceType.DefaultMaxTemperature, p.ProduceType.DefaultMinHumidity, p.ProduceType.DefaultMaxHumidity)).ToList(),
        t.EstimatedWeightTonnes, t.PackagingType?.ToString(), t.UnitCount, t.AdditionalNotes,
        t.PickupAddress, t.OriginLabel, t.PickupLatitude, t.PickupLongitude,
        t.DestinationAddress, t.DestinationLabel, t.DestinationLatitude, t.DestinationLongitude,
        t.LoadingTime, t.ExpectedArrival, t.StartedAt, t.CompletedAt, t.CancelledAt,
        new ThresholdInput(t.MinTemperature, t.MaxTemperature, t.MinHumidity, t.MaxHumidity),
        t.LastTemperature, t.LastHumidity, t.LastLatitude, t.LastLongitude, t.LastSpeedKmh, t.LastPositionAt,
        Math.Round(t.DistanceTravelledKm, 1), t.PlannedDistanceKm, t.Co2EmissionKg, openAlerts, links, t.CreatedAt);
}
