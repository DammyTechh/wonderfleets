using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Features.Alerts;
using WonderFleet.Application.Features.Notifications;
using WonderFleet.Application.Features.ShareLinks;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Telemetry;

/// Time-based rules that cannot be evaluated on a single reading: offline devices, stoppages, delays, link expiry.
public interface ITelemetryMonitor
{
    Task RunOnceAsync(CancellationToken ct);
}

internal sealed class TelemetryMonitor(
    IApplicationDbContext db,
    IAlertEngine alerts,
    INotificationComposer notify,
    IShareLinkService shareLinks,
    IRealtimePublisher realtime,
    IClock clock) : ITelemetryMonitor
{
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var cfg = await alerts.GetConfigAsync(ct);

        await DetectOfflineDevicesAsync(cfg, now, ct);
        await DetectStoppagesAsync(cfg, now, ct);
        await DetectDelaysAsync(cfg, now, ct);
        await shareLinks.ExpireLinksForEndedTripsAsync(ct);

        await db.SaveChangesAsync(ct);
        foreach (var evt in alerts.DrainEvents()) await realtime.PublishAlertAsync(evt, ct);
    }

    private async Task DetectOfflineDevicesAsync(AlertingConfig cfg, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddMinutes(-cfg.Minutes(AlertType.DeviceOffline, 10));
        var stale = await db.Devices.Where(d => d.IsOnline && (d.LastChangedAt == null || d.LastChangedAt < cutoff)).ToListAsync(ct);
        foreach (var device in stale)
        {
            device.IsOnline = false;
            var trip = await db.Trips.Include(t => t.Vehicle)
                .FirstOrDefaultAsync(t => t.DeviceId == device.Id && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct);
            if (trip is null) continue;

            trip.SensorStatus = SensorStatus.Offline;
            if (cfg.IsEnabled(AlertType.DeviceOffline))
                await alerts.RaiseAsync(new AlertSpec(AlertType.DeviceOffline, AlertSeverity.Warning, "Device offline",
                    $"No telemetry from device {device.Serial} on {trip.Vehicle?.FleetNumber} since {AlertEngine.Wat(device.LastChangedAt ?? now)}. Check power and GSM coverage."),
                    device, trip, ct);
        }
    }

    private async Task DetectStoppagesAsync(AlertingConfig cfg, DateTimeOffset now, CancellationToken ct)
    {
        if (!cfg.IsEnabled(AlertType.Stoppage)) return;
        var minutes = cfg.Minutes(AlertType.Stoppage, 30);
        var cutoff = now.AddMinutes(-minutes);
        var stuck = await db.Trips.Include(t => t.Vehicle).Include(t => t.Device)
            .Where(t => (t.Status == TripStatus.InTransit || t.Status == TripStatus.Delayed)
                        && t.SensorStatus != SensorStatus.Offline
                        && t.LastMovedAt != null && t.LastMovedAt < cutoff)
            .ToListAsync(ct);

        foreach (var trip in stuck)
        {
            trip.MarkStopped();
            await alerts.RaiseAsync(new AlertSpec(AlertType.Stoppage, AlertSeverity.Warning, "Stoppage alert",
                $"{trip.Vehicle?.FleetNumber} ({Text.Route(trip.OriginLabel, trip.DestinationLabel)}) has not moved for over {minutes} minutes."),
                trip.Device, trip, ct);
        }
    }

    private async Task DetectDelaysAsync(AlertingConfig cfg, DateTimeOffset now, CancellationToken ct)
    {
        if (!cfg.IsEnabled(AlertType.Delay)) return;
        var cutoff = now.AddMinutes(-cfg.Minutes(AlertType.Delay, 30));
        var late = await db.Trips.Include(t => t.Vehicle).Include(t => t.LogisticsPartner)
            .Where(t => (t.Status == TripStatus.InTransit || t.Status == TripStatus.Stopped) && t.ExpectedArrival < cutoff)
            .Where(t => !db.Alerts.Any(a => a.TripId == t.Id && a.AlertType == AlertType.Delay && a.Status != AlertStatus.Resolved))
            .ToListAsync(ct);

        foreach (var trip in late)
        {
            trip.MarkDelayed(now);
            var hours = Math.Max(1, (int)Math.Round((now - trip.ExpectedArrival).TotalHours));
            var route = Text.Route(trip.OriginLabel, trip.DestinationLabel);
            await alerts.RaiseAsync(new AlertSpec(AlertType.Delay, AlertSeverity.Warning, "Delivery delay reported",
                $"{trip.Vehicle?.FleetNumber} on the {route} corridor is running about {hours} hour(s) behind schedule."),
                null, trip, ct);

            var contact = await db.AgroProcessorContacts.AsNoTracking()
                .Where(c => c.AgroProcessorId == trip.AgroProcessorId).OrderByDescending(c => c.IsPrimary)
                .FirstOrDefaultAsync(ct);
            if (contact is not null)
                notify.Email(contact.Email, EmailTemplates.DeliveryDelayed, new Dictionary<string, string?>
                {
                    ["Name"] = contact.FullName,
                    ["FleetNumber"] = trip.Vehicle?.FleetNumber,
                    ["Route"] = route,
                    ["DelayHours"] = hours.ToString(),
                    ["ExpectedArrival"] = AlertEngine.Wat(trip.ExpectedArrival),
                    ["PartnerName"] = trip.LogisticsPartner?.CompanyName,
                });
        }
    }
}
