using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Options;
using WonderFleet.Application.Features.Notifications;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;
using WonderFleet.Domain.Services;

namespace WonderFleet.Application.Features.Alerts;

public sealed record AlertSpec(
    AlertType Type, AlertSeverity Severity, string Title, string Message,
    decimal? Value = null, decimal? Secondary = null, decimal? Threshold = null);

public sealed record AlertingConfig(
    IReadOnlyDictionary<AlertType, AlertRule> Rules,
    NotificationChannelSetting? Email,
    NotificationChannelSetting? Sms)
{
    public AlertRule? Rule(AlertType type) => Rules.GetValueOrDefault(type);

    /// Missing rule = enabled (safety first). Temperature/humidity rules gate both directions.
    public bool IsEnabled(AlertType type) => type switch
    {
        AlertType.TemperatureBreach or AlertType.LowTemperature => Rule(AlertType.TemperatureBreach)?.IsEnabled ?? true,
        AlertType.HighHumidity or AlertType.LowHumidity => Rule(AlertType.HighHumidity)?.IsEnabled ?? true,
        _ => Rule(type)?.IsEnabled ?? true,
    };

    public int Minutes(AlertType type, int fallback) => Rule(type)?.DurationMinutes ?? fallback;
}

public interface IAlertEngine
{
    Task<AlertingConfig> GetConfigAsync(CancellationToken ct);
    void InvalidateConfig();
    Task EvaluateReadingAsync(Device device, Trip? trip, SensorReading reading, CancellationToken ct);
    Task<Alert> RaiseAsync(AlertSpec spec, Device? device, Trip? trip, CancellationToken ct);
    Task ResolveOpenAsync(Guid? deviceId, Guid? tripId, AlertType type, string note, CancellationToken ct);
    Task ResolveAllForTripAsync(Guid tripId, string note, CancellationToken ct);
    /// Realtime events produced in this scope; publish them after SaveChanges succeeds.
    IReadOnlyList<AlertEvent> DrainEvents();
}

internal sealed class AlertEngine(
    IApplicationDbContext db,
    INotificationComposer notify,
    IClock clock,
    IMemoryCache cache,
    IOptions<AlertingOptions> options,
    IOptions<AppOptions> app) : IAlertEngine
{
    private const string ConfigCacheKey = "alerting:config";
    private static readonly AlertType[] ConditionTypes =
        [AlertType.TemperatureBreach, AlertType.LowTemperature, AlertType.HighHumidity, AlertType.LowHumidity];

    private readonly List<AlertEvent> _events = [];
    private List<(string Email, string? Phone, string Name)>? _admins;

    public async Task<AlertingConfig> GetConfigAsync(CancellationToken ct) =>
        (await cache.GetOrCreateAsync(ConfigCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            var rules = await db.AlertRules.AsNoTracking().ToListAsync(ct);
            var channels = await db.NotificationChannelSettings.AsNoTracking().ToListAsync(ct);
            return new AlertingConfig(
                rules.ToDictionary(r => r.AlertType),
                channels.FirstOrDefault(c => c.Channel == NotificationChannel.Email),
                channels.FirstOrDefault(c => c.Channel == NotificationChannel.Sms));
        }))!;

    public void InvalidateConfig() => cache.Remove(ConfigCacheKey);

    public IReadOnlyList<AlertEvent> DrainEvents()
    {
        var copy = _events.ToList();
        _events.Clear();
        return copy;
    }

    public async Task EvaluateReadingAsync(Device device, Trip? trip, SensorReading reading, CancellationToken ct)
    {
        var cfg = await GetConfigAsync(ct);
        var open = await db.Alerts.Where(a => a.DeviceId == device.Id && a.Status != AlertStatus.Resolved).ToListAsync(ct);
        var label = Label(device, trip);

        if (trip is not null)
        {
            var breaches = ThresholdEvaluator.Evaluate(reading.Temperature, reading.Humidity, trip.Thresholds)
                .Where(b => cfg.IsEnabled(b.Type))
                .ToList();

            foreach (var b in breaches)
                await UpsertAsync(BuildConditionSpec(b, reading, trip, label), device, trip, open, cfg, ct);

            foreach (var stale in open.Where(a => ConditionTypes.Contains(a.AlertType) && a.IsOpen && breaches.All(b => b.Type != a.AlertType)).ToList())
                await ResolveCoreAsync(stale, trip, $"{label} returned to the safe range ({trip.MinTemperature:0.#}–{trip.MaxTemperature:0.#}°C, {trip.MinHumidity:0}–{trip.MaxHumidity:0}% RH).", cfg, ct);

            trip.SensorStatus = ThresholdEvaluator.ToSensorStatus(breaches);
        }

        if (cfg.Rule(AlertType.LowBattery) is { IsEnabled: true, ThresholdValue: { } minBattery } && reading.BatteryLevel is { } battery)
        {
            if (battery < minBattery)
            {
                await UpsertAsync(new AlertSpec(AlertType.LowBattery, AlertSeverity.Informational, "Low battery",
                    $"Device {device.Serial}{(trip is null ? "" : $" in {trip.Vehicle?.FleetNumber}")} is reporting {battery}% battery. Charge before next dispatch.",
                    battery, null, minBattery), device, trip, open, cfg, ct);
            }
            else if (battery >= minBattery + options.Value.BatteryHysteresis)
            {
                foreach (var a in open.Where(a => a.AlertType == AlertType.LowBattery && a.IsOpen).ToList())
                    await ResolveCoreAsync(a, trip, $"Device {device.Serial} battery recovered to {battery}%.", cfg, ct);
            }
        }
    }

    public async Task<Alert> RaiseAsync(AlertSpec spec, Device? device, Trip? trip, CancellationToken ct)
    {
        var cfg = await GetConfigAsync(ct);
        var open = await OpenQuery(device?.Id, trip?.Id, spec.Type).ToListAsync(ct);
        return await UpsertAsync(spec, device, trip, open, cfg, ct);
    }

    public async Task ResolveOpenAsync(Guid? deviceId, Guid? tripId, AlertType type, string note, CancellationToken ct)
    {
        var open = await OpenQuery(deviceId, tripId, type).Include(a => a.Trip).ToListAsync(ct);
        if (open.Count == 0) return;
        var cfg = await GetConfigAsync(ct);
        foreach (var alert in open) await ResolveCoreAsync(alert, alert.Trip, note, cfg, ct);
    }

    public async Task ResolveAllForTripAsync(Guid tripId, string note, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var open = await db.Alerts.Where(a => a.TripId == tripId && a.Status != AlertStatus.Resolved).ToListAsync(ct);
        foreach (var a in open)
        {
            a.Resolve(null, note, now);
            _events.Add(ToEvent(a));
        }
    }

    // ------------------------------------------------------------------ internals

    private IQueryable<Alert> OpenQuery(Guid? deviceId, Guid? tripId, AlertType type)
    {
        var q = db.Alerts.Where(a => a.AlertType == type && a.Status != AlertStatus.Resolved);
        return deviceId is not null ? q.Where(a => a.DeviceId == deviceId) : q.Where(a => a.TripId == tripId);
    }

    private async Task<Alert> UpsertAsync(AlertSpec spec, Device? device, Trip? trip, List<Alert> open, AlertingConfig cfg, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var existing = open.FirstOrDefault(a => a.AlertType == spec.Type && a.IsOpen);
        if (existing is not null)
        {
            var before = existing.Severity;
            existing.Escalate(spec.Severity, spec.Value, spec.Secondary, now);
            existing.Message = spec.Message;
            if (existing.Severity > before)
            {
                await FanOutAsync(existing, trip, device, cfg, escalated: true, ct);
                _events.Add(ToEvent(existing));
            }
            return existing;
        }

        var alert = new Alert
        {
            AlertType = spec.Type,
            Severity = spec.Severity,
            Title = spec.Title,
            Message = spec.Message,
            DeviceId = device?.Id,
            TripId = trip?.Id,
            VehicleId = trip?.VehicleId ?? device?.VehicleId,
            ReadingValue = spec.Value,
            SecondaryReadingValue = spec.Secondary,
            ThresholdValue = spec.Threshold,
            TriggeredAt = now,
            LastTriggeredAt = now,
        };
        db.Alerts.Add(alert);
        open.Add(alert);
        await FanOutAsync(alert, trip, device, cfg, escalated: false, ct);
        _events.Add(ToEvent(alert));
        return alert;
    }

    private async Task ResolveCoreAsync(Alert alert, Trip? trip, string note, AlertingConfig cfg, CancellationToken ct)
    {
        var wasCritical = alert.Severity == AlertSeverity.Critical;
        alert.Resolve(null, note, clock.UtcNow);
        _events.Add(ToEvent(alert));

        notify.InApp(NotificationCategory.Hardware, $"{alert.Title} resolved", note, "Alert", alert.Id, trip?.LogisticsPartnerId);

        if (wasCritical && Allows(cfg.Email, AlertSeverity.Critical))
        {
            foreach (var admin in await AdminsAsync(ct))
            {
                notify.Email(admin.Email, EmailTemplates.AlertResolved, new Dictionary<string, string?>
                {
                    ["Name"] = admin.Name,
                    ["AlertTitle"] = alert.Title,
                    ["ResolutionNote"] = note,
                    ["FleetNumber"] = trip?.Vehicle?.FleetNumber ?? "—",
                    ["Route"] = trip is null ? "—" : Text.Route(trip.OriginLabel, trip.DestinationLabel),
                    ["ResolvedAt"] = Wat(clock.UtcNow),
                    ["ActionUrl"] = $"{app.Value.FrontendBaseUrl}/alerts",
                }, alert.Id);
            }
        }
    }

    private async Task FanOutAsync(Alert alert, Trip? trip, Device? device, AlertingConfig cfg, bool escalated, CancellationToken ct)
    {
        var category = alert.AlertType == AlertType.Delay ? NotificationCategory.Partner
            : alert.Severity == AlertSeverity.Critical ? NotificationCategory.Critical
            : NotificationCategory.Hardware;
        var title = escalated ? $"{alert.Title} escalated" : alert.Title;
        notify.InApp(category, title, alert.Message, "Alert", alert.Id, trip?.LogisticsPartnerId,
            requiresReview: alert.Severity != AlertSeverity.Informational);

        var sendEmail = Allows(cfg.Email, alert.Severity);
        var sendSms = Allows(cfg.Sms, alert.Severity);
        if (!sendEmail && !sendSms) return;

        var fleet = trip?.Vehicle?.FleetNumber ?? device?.Serial ?? "—";
        var route = trip is null ? "—" : Text.Route(trip.OriginLabel, trip.DestinationLabel);
        var model = new Dictionary<string, string?>
        {
            ["AlertTitle"] = alert.Title,
            ["AlertMessage"] = alert.Message,
            ["Severity"] = alert.Severity.ToString(),
            ["FleetNumber"] = fleet,
            ["DeviceSerial"] = device?.Serial ?? "—",
            ["Route"] = route,
            ["Reading"] = FormatReading(alert),
            ["Threshold"] = alert.ThresholdValue?.ToString("0.##") ?? "—",
            ["TriggeredAt"] = Wat(alert.LastTriggeredAt),
        };
        var sms = $"WonderFleet {alert.Severity.ToString().ToUpperInvariant()}: {alert.Title} on {fleet} ({route}). {alert.Message}";

        foreach (var admin in await AdminsAsync(ct))
        {
            if (sendEmail) notify.Email(admin.Email, EmailTemplates.CriticalAlert, With(model, admin.Name, $"{app.Value.FrontendBaseUrl}/alerts"), alert.Id);
            if (sendSms && admin.Phone is not null) notify.Sms(admin.Phone, sms, alert.Id);
        }

        // External stakeholders are only paged for critical cargo conditions.
        if (trip is null || alert.Severity != AlertSeverity.Critical) return;

        if (options.Value.NotifyLogisticsPartnerOnCritical)
        {
            var partner = await db.LogisticsPartners.AsNoTracking()
                .Where(p => p.Id == trip.LogisticsPartnerId)
                .Select(p => new { p.Email, p.PhoneNumber, p.ContactPerson })
                .FirstOrDefaultAsync(ct);
            if (partner is not null)
            {
                if (sendEmail) notify.Email(partner.Email, EmailTemplates.CriticalAlert, With(model, partner.ContactPerson, null), alert.Id);
                if (sendSms) notify.Sms(partner.PhoneNumber, sms, alert.Id);
            }
        }

        if (options.Value.NotifyAgroProcessorOnCritical)
        {
            var contact = await db.AgroProcessorContacts.AsNoTracking()
                .Where(c => c.AgroProcessorId == trip.AgroProcessorId)
                .OrderByDescending(c => c.IsPrimary)
                .Select(c => new { c.Email, c.PhoneNumber, c.FullName })
                .FirstOrDefaultAsync(ct);
            if (contact is not null)
            {
                if (sendEmail) notify.Email(contact.Email, EmailTemplates.CriticalAlert, With(model, contact.FullName, null), alert.Id);
                if (sendSms) notify.Sms(contact.PhoneNumber, sms, alert.Id);
            }
        }
    }

    private async Task<List<(string Email, string? Phone, string Name)>> AdminsAsync(CancellationToken ct) =>
        _admins ??= (await db.AdminUsers.AsNoTracking().Where(a => a.IsActive)
                .Select(a => new { a.Email, a.PhoneNumber, a.FullName }).ToListAsync(ct))
            .Select(a => (a.Email, a.PhoneNumber, a.FullName)).ToList();

    private static bool Allows(NotificationChannelSetting? s, AlertSeverity severity) =>
        s is { IsEnabled: true } && (!s.CriticalOnly || severity == AlertSeverity.Critical);

    private static Dictionary<string, string?> With(Dictionary<string, string?> model, string name, string? actionUrl) =>
        new(model) { ["Name"] = name, ["ActionUrl"] = actionUrl };

    private static AlertSpec BuildConditionSpec(ThresholdBreach b, SensorReading r, Trip trip, string label)
    {
        var reading = $"{r.Temperature?.ToString("0.#") ?? "—"}°C · {r.Humidity?.ToString("0") ?? "—"}% RH";
        return b.Type switch
        {
            AlertType.TemperatureBreach => new(b.Type, b.Severity, "Temperature breach",
                $"{label} cargo is at {b.Value:0.#}°C, above the {b.Threshold:0.#}°C limit ({reading}). Stop in shade or ventilate to cool the load.",
                b.Value, r.Humidity, b.Threshold),
            AlertType.LowTemperature => new(b.Type, b.Severity, "Low temperature",
                $"{label} cargo is at {b.Value:0.#}°C, below the {b.Threshold:0.#}°C limit ({reading}). Reduce cooling to prevent chilling injury.",
                b.Value, r.Humidity, b.Threshold),
            AlertType.HighHumidity => new(b.Type, b.Severity, "High humidity",
                $"{label} humidity is {b.Value:0}% RH, above the {b.Threshold:0}% limit ({reading}). Improve ventilation to limit rot and mould.",
                b.Value, r.Temperature, b.Threshold),
            _ => new(b.Type, b.Severity, "Low humidity",
                $"{label} humidity is {b.Value:0}% RH, below the {b.Threshold:0}% limit ({reading}). Cover produce to reduce moisture loss.",
                b.Value, r.Temperature, b.Threshold),
        };
    }

    private static string Label(Device device, Trip? trip) =>
        trip?.Vehicle is { } v ? $"{v.FleetNumber} ({trip.OriginLabel} → {trip.DestinationLabel})" : $"Device {device.Serial}";

    private static string FormatReading(Alert a) => a.AlertType switch
    {
        AlertType.TemperatureBreach or AlertType.LowTemperature => $"{a.ReadingValue:0.#}°C",
        AlertType.HighHumidity or AlertType.LowHumidity => $"{a.ReadingValue:0}% RH",
        AlertType.LowBattery => $"{a.ReadingValue:0}%",
        _ => a.ReadingValue?.ToString("0.#") ?? "—",
    };

    internal static string Wat(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(1)).ToString("dd MMM yyyy, HH:mm") + " WAT";

    private static AlertEvent ToEvent(Alert a) => new(a.Id, a.TripId, a.AlertType.ToString(), a.Severity.ToString(),
        a.Status.ToString(), a.Title, a.Message, a.LastTriggeredAt);
}
