using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Models;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Alerts;

public sealed record AlertListQuery : PageQuery
{
    /// open | resolved | all (default open)
    public string? State { get; init; }
    public AlertSeverity? Severity { get; init; }
    public AlertType? Type { get; init; }
    public Guid? TripId { get; init; }
}

public sealed record AlertSummaryDto(int Critical, int Warning, int Informational, int Resolved, int ResolvedWindowDays);

public sealed record AlertDto(
    Guid Id, string Type, string Severity, string Status, string Title, string Message,
    string? DeviceSerial, Guid? TripId, string? TripCode, string? VehicleCode, string? FleetNumber, string? Route,
    decimal? ReadingValue, decimal? SecondaryReadingValue, decimal? ThresholdValue, string ReadingDisplay, string? ThresholdDisplay,
    DateTimeOffset TriggeredAt, DateTimeOffset LastTriggeredAt, int OccurrenceCount,
    DateTimeOffset? AcknowledgedAt, DateTimeOffset? ResolvedAt, string? ResolutionNote);

public sealed record ResolveAlertRequest(string? Note);

public sealed record AlertRuleDto(Guid Id, string AlertType, string Name, string Description, bool IsEnabled, decimal? ThresholdValue, int? DurationMinutes, string Display);

public sealed record UpdateAlertRuleRequest(bool IsEnabled, decimal? ThresholdValue, int? DurationMinutes);

public sealed record ChannelSettingDto(Guid Id, string Channel, bool IsEnabled, bool CriticalOnly, string Display);

public sealed record UpdateChannelSettingRequest(bool IsEnabled, bool CriticalOnly);

public sealed record AlertActivityDto(IReadOnlyList<string> Days, IReadOnlyList<string> Categories, IReadOnlyList<AlertActivityCellDto> Cells);

public sealed record AlertActivityCellDto(string Day, string Category, int Count, string Level);

public sealed class ResolveAlertRequestValidator : AbstractValidator<ResolveAlertRequest>
{
    public ResolveAlertRequestValidator() => RuleFor(x => x.Note).MaximumLength(500);
}

public sealed class UpdateAlertRuleRequestValidator : AbstractValidator<UpdateAlertRuleRequest>
{
    public UpdateAlertRuleRequestValidator()
    {
        RuleFor(x => x.ThresholdValue).InclusiveBetween(-30, 100).When(x => x.ThresholdValue.HasValue);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 24 * 60).When(x => x.DurationMinutes.HasValue);
    }
}

public interface IAlertAdminService
{
    Task<AlertSummaryDto> GetSummaryAsync(CancellationToken ct);
    Task<PagedResult<AlertDto>> ListAsync(AlertListQuery query, CancellationToken ct);
    Task<AlertDto> GetAsync(Guid id, CancellationToken ct);
    Task<AlertDto> AcknowledgeAsync(Guid id, CancellationToken ct);
    Task<AlertDto> ResolveAsync(Guid id, ResolveAlertRequest request, CancellationToken ct);
    Task<IReadOnlyList<AlertRuleDto>> GetRulesAsync(CancellationToken ct);
    Task<AlertRuleDto> UpdateRuleAsync(Guid id, UpdateAlertRuleRequest request, CancellationToken ct);
    Task<IReadOnlyList<ChannelSettingDto>> GetChannelsAsync(CancellationToken ct);
    Task<ChannelSettingDto> UpdateChannelAsync(Guid id, UpdateChannelSettingRequest request, CancellationToken ct);
    Task<AlertActivityDto> GetActivityAsync(int days, CancellationToken ct);
}

internal sealed class AlertAdminService(
    IApplicationDbContext db,
    IAlertEngine engine,
    IAnalyticsReadStore analytics,
    IRealtimePublisher realtime,
    ICurrentActor actor,
    IClock clock,
    IAuditLogger audit) : IAlertAdminService
{
    private const int ResolvedWindowDays = 30;
    internal static readonly string[] ActivityCategories = ["Temp", "Hum", "Route", "Device"];

    public async Task<AlertSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        var since = clock.UtcNow.AddDays(-ResolvedWindowDays);
        var open = await db.Alerts.AsNoTracking().Where(a => a.Status != AlertStatus.Resolved)
            .GroupBy(a => a.Severity).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var resolved = await db.Alerts.CountAsync(a => a.Status == AlertStatus.Resolved && a.ResolvedAt >= since, ct);
        int C(AlertSeverity s) => open.FirstOrDefault(o => o.Key == s)?.Count ?? 0;
        return new AlertSummaryDto(C(AlertSeverity.Critical), C(AlertSeverity.Warning), C(AlertSeverity.Informational), resolved, ResolvedWindowDays);
    }

    public async Task<PagedResult<AlertDto>> ListAsync(AlertListQuery query, CancellationToken ct)
    {
        var q = db.Alerts.AsNoTracking();
        q = (query.State ?? "open").ToLowerInvariant() switch
        {
            "resolved" => q.Where(a => a.Status == AlertStatus.Resolved),
            "all" => q,
            _ => q.Where(a => a.Status != AlertStatus.Resolved),
        };
        if (query.Severity is { } sev) q = q.Where(a => a.Severity == sev);
        if (query.Type is { } type) q = q.Where(a => a.AlertType == type);
        if (query.TripId is { } tid) q = q.Where(a => a.TripId == tid);
        if (query.NormalizedSearch is { } term)
            q = q.Where(a => a.Title.ToLower().Contains(term)
                || (a.Device != null && a.Device.Serial.ToLower().Contains(term))
                || (a.Trip != null && (a.Trip.TripCode.ToLower().Contains(term) || a.Trip.Vehicle!.FleetNumber.ToLower().Contains(term)
                                        || a.Trip.Vehicle.VehicleCode.ToLower().Contains(term))));

        var total = await q.CountAsync(ct);
        var rows = await Project(q.OrderByDescending(a => a.Status != AlertStatus.Resolved)
                .ThenByDescending(a => a.Severity).ThenByDescending(a => a.LastTriggeredAt)
                .Skip(query.Skip).Take(query.PageSize))
            .ToListAsync(ct);
        return new PagedResult<AlertDto>(rows.Select(Map).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<AlertDto> GetAsync(Guid id, CancellationToken ct) =>
        Map(await Project(db.Alerts.AsNoTracking().Where(a => a.Id == id)).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Alert", id));

    public async Task<AlertDto> AcknowledgeAsync(Guid id, CancellationToken ct)
    {
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Alert", id);
        alert.Acknowledge(actor.AdminId, clock.UtcNow);
        audit.Record("alert.acknowledged", nameof(Alert), id);
        await db.SaveChangesAsync(ct);
        await PublishAsync(alert, ct);
        return await GetAsync(id, ct);
    }

    public async Task<AlertDto> ResolveAsync(Guid id, ResolveAlertRequest request, CancellationToken ct)
    {
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Alert", id);
        alert.Resolve(actor.AdminId, Text.Trimmed(request.Note) ?? "Resolved by admin.", clock.UtcNow);
        audit.Record("alert.resolved", nameof(Alert), id, new { request.Note });
        await db.SaveChangesAsync(ct);
        await PublishAsync(alert, ct);
        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<AlertRuleDto>> GetRulesAsync(CancellationToken ct) =>
        (await db.AlertRules.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct)).Select(Map).ToList();

    public async Task<AlertRuleDto> UpdateRuleAsync(Guid id, UpdateAlertRuleRequest request, CancellationToken ct)
    {
        var rule = await db.AlertRules.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Alert rule", id);
        var usesThreshold = rule.AlertType is AlertType.TemperatureBreach or AlertType.HighHumidity or AlertType.LowBattery;
        var usesDuration = rule.AlertType is AlertType.Stoppage or AlertType.DeviceOffline or AlertType.Delay;
        if (usesThreshold && request.ThresholdValue is null)
            throw RequestValidationException.For(nameof(request.ThresholdValue), "This rule needs a threshold value.");
        if (usesDuration && request.DurationMinutes is null)
            throw RequestValidationException.For(nameof(request.DurationMinutes), "This rule needs a duration in minutes.");
        if (rule.AlertType == AlertType.HighHumidity && request.ThresholdValue is < 0 or > 100)
            throw RequestValidationException.For(nameof(request.ThresholdValue), "Humidity must be between 0 and 100%.");
        if (rule.AlertType == AlertType.LowBattery && request.ThresholdValue is < 1 or > 90)
            throw RequestValidationException.For(nameof(request.ThresholdValue), "Battery threshold must be between 1 and 90%.");

        rule.IsEnabled = request.IsEnabled;
        if (usesThreshold) rule.ThresholdValue = request.ThresholdValue;
        if (usesDuration) rule.DurationMinutes = request.DurationMinutes;
        audit.Record("alert_rule.updated", nameof(AlertRule), id, request);
        await db.SaveChangesAsync(ct);
        engine.InvalidateConfig();
        return Map(rule);
    }

    public async Task<IReadOnlyList<ChannelSettingDto>> GetChannelsAsync(CancellationToken ct) =>
        (await db.NotificationChannelSettings.AsNoTracking().OrderBy(c => c.Channel).ToListAsync(ct)).Select(Map).ToList();

    public async Task<ChannelSettingDto> UpdateChannelAsync(Guid id, UpdateChannelSettingRequest request, CancellationToken ct)
    {
        var channel = await db.NotificationChannelSettings.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Channel", id);
        channel.IsEnabled = request.IsEnabled;
        channel.CriticalOnly = request.CriticalOnly;
        audit.Record("notification_channel.updated", nameof(NotificationChannelSetting), id, request);
        await db.SaveChangesAsync(ct);
        engine.InvalidateConfig();
        return Map(channel);
    }

    public async Task<AlertActivityDto> GetActivityAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 7, 31);
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(1)).Date);
        var first = today.AddDays(-(days - 1));
        var from = new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(1));
        var cells = await analytics.GetAlertActivityAsync(from, now, ct);

        var dayList = Enumerable.Range(0, days).Select(i => first.AddDays(i)).ToList();
        var result = new List<AlertActivityCellDto>();
        foreach (var d in dayList)
            foreach (var c in ActivityCategories)
            {
                var cell = cells.FirstOrDefault(x => x.Day == d && x.Category == c);
                var level = cell is null ? "None" : cell.WorstSeverity == nameof(AlertSeverity.Critical) ? "Critical"
                    : cell.WorstSeverity == nameof(AlertSeverity.Warning) ? "Warning" : "Informational";
                result.Add(new AlertActivityCellDto(Label(d, days), c, cell?.Count ?? 0, level));
            }
        return new AlertActivityDto(dayList.Select(d => Label(d, days)).ToList(), ActivityCategories, result);
    }

    // ------------------------------------------------------------------ helpers

    private static string Label(DateOnly d, int days) => days <= 7 ? d.ToString("ddd") : d.ToString("dd MMM");

    private async Task PublishAsync(Alert a, CancellationToken ct) =>
        await realtime.PublishAlertAsync(new AlertEvent(a.Id, a.TripId, a.AlertType.ToString(), a.Severity.ToString(), a.Status.ToString(),
            a.Title, a.Message, clock.UtcNow), ct);

    private sealed record Row(Alert A, string? Serial, string? TripCode, string? VehicleCode, string? FleetNumber, string? Origin, string? Destination);

    private static IQueryable<Row> Project(IQueryable<Alert> q) => q.Select(a => new Row(
        a,
        a.Device != null ? a.Device.Serial : null,
        a.Trip != null ? a.Trip.TripCode : null,
        a.Trip != null ? a.Trip.Vehicle!.VehicleCode : null,
        a.Trip != null ? a.Trip.Vehicle!.FleetNumber : null,
        a.Trip != null ? a.Trip.OriginLabel : null,
        a.Trip != null ? a.Trip.DestinationLabel : null));

    private static AlertDto Map(Row r)
    {
        var a = r.A;
        return new AlertDto(a.Id, a.AlertType.ToString(), a.Severity.ToString(), a.Status.ToString(), a.Title, a.Message,
            r.Serial, a.TripId, r.TripCode, r.VehicleCode, r.FleetNumber,
            r.Origin is null ? null : Text.Route(r.Origin, r.Destination!),
            a.ReadingValue, a.SecondaryReadingValue, a.ThresholdValue, ReadingDisplay(a), ThresholdDisplay(a),
            a.TriggeredAt, a.LastTriggeredAt, a.OccurrenceCount, a.AcknowledgedAt, a.ResolvedAt, a.ResolutionNote);
    }

    /// "28.1°C · 40%" as on the Alerts page.
    internal static string ReadingDisplay(Alert a) => a.AlertType switch
    {
        AlertType.TemperatureBreach or AlertType.LowTemperature =>
            $"{a.ReadingValue:0.#}°C{(a.SecondaryReadingValue is { } h ? $" · {h:0}%" : "")}",
        AlertType.HighHumidity or AlertType.LowHumidity =>
            $"{(a.SecondaryReadingValue is { } t ? $"{t:0.#}°C · " : "")}{a.ReadingValue:0}%",
        AlertType.LowBattery => $"{a.ReadingValue:0}% battery",
        AlertType.Stoppage => "Stationary",
        AlertType.DeviceOffline => "No signal",
        AlertType.Delay => "Behind schedule",
        _ => a.ReadingValue?.ToString("0.#") ?? "—",
    };

    internal static string? ThresholdDisplay(Alert a) => a.ThresholdValue is not { } v ? null : a.AlertType switch
    {
        AlertType.TemperatureBreach => $"+ {v:0.#}°C threshold",
        AlertType.LowTemperature => $"- {v:0.#}°C threshold",
        AlertType.HighHumidity => $"+ {v:0}% threshold",
        AlertType.LowHumidity => $"- {v:0}% threshold",
        AlertType.LowBattery => $"< {v:0}% threshold",
        _ => null,
    };

    private static AlertRuleDto Map(AlertRule r) => new(r.Id, r.AlertType.ToString(), r.Name, r.Description, r.IsEnabled,
        r.ThresholdValue, r.DurationMinutes, r.AlertType switch
        {
            AlertType.TemperatureBreach => $"Above {r.ThresholdValue:0.#}°C (fallback when a trip has no range)",
            AlertType.HighHumidity => $"Above {r.ThresholdValue:0}% RH (fallback when a trip has no range)",
            AlertType.LowBattery => $"Below {r.ThresholdValue:0}%",
            AlertType.Stoppage => $"Stopped > {r.DurationMinutes} min",
            AlertType.DeviceOffline => $"No data > {r.DurationMinutes} min",
            AlertType.Delay => $"Late > {r.DurationMinutes} min",
            _ => r.Description,
        });

    private static ChannelSettingDto Map(NotificationChannelSetting c) => new(c.Id, c.Channel.ToString(), c.IsEnabled, c.CriticalOnly,
        !c.IsEnabled ? "Off" : c.CriticalOnly ? "Critical only" : "All alerts");
}
