using WonderFleet.Api.Setup;
using WonderFleet.Application.Features.Alerts;
using WonderFleet.Application.Features.Analytics;
using WonderFleet.Application.Features.Geo;
using WonderFleet.Application.Features.Notifications;
using WonderFleet.Application.Features.RouteAi;
using WonderFleet.Application.Features.Weather;
using WonderFleet.Application.Common.Models;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Api.Endpoints;

internal static class OperationsEndpoints
{
    public static void MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/alerts", "Alerts");

        group.MapGet("/summary", (IAlertAdminService service, CancellationToken ct) => service.GetSummaryAsync(ct));

        group.MapGet("/", (IAlertAdminService service, CancellationToken ct,
                string? search, string? state, AlertSeverity? severity, AlertType? type, Guid? tripId, int page = 1, int pageSize = 20) =>
            service.ListAsync(new AlertListQuery
            {
                Search = search, State = state, Severity = severity, Type = type, TripId = tripId, Page = page, PageSize = pageSize,
            }, ct));

        group.MapGet("/activity", (IAlertAdminService service, CancellationToken ct, int days = 7) =>
            service.GetActivityAsync(days, ct));

        group.MapGet("/{id:guid}", (Guid id, IAlertAdminService service, CancellationToken ct) => service.GetAsync(id, ct));

        group.MapPost("/{id:guid}/acknowledge", (Guid id, IAlertAdminService service, CancellationToken ct) =>
            service.AcknowledgeAsync(id, ct));

        group.MapPost("/{id:guid}/resolve", (Guid id, ResolveAlertRequest? request, IAlertAdminService service, CancellationToken ct) =>
            service.ResolveAsync(id, request ?? new ResolveAlertRequest(null), ct));

        // Alert rules and channel toggles (Temperature > 8 °C, SMS critical only, ...)
        group.MapGet("/rules", (IAlertAdminService service, CancellationToken ct) => service.GetRulesAsync(ct));

        group.MapPut("/rules/{id:guid}", (Guid id, UpdateAlertRuleRequest request, IAlertAdminService service, CancellationToken ct) =>
            service.UpdateRuleAsync(id, request, ct)).Validate<UpdateAlertRuleRequest>();

        group.MapGet("/channels", (IAlertAdminService service, CancellationToken ct) => service.GetChannelsAsync(ct));

        group.MapPut("/channels/{id:guid}", (Guid id, UpdateChannelSettingRequest request, IAlertAdminService service, CancellationToken ct) =>
            service.UpdateChannelAsync(id, request, ct));
    }

    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/notifications", "Notifications");

        group.MapGet("/", (INotificationFeedService service, CancellationToken ct,
                string? search, NotificationCategory? category, bool unreadOnly = false, int page = 1, int pageSize = 20) =>
            service.ListAsync(new NotificationQuery
            {
                Search = search, Category = category, UnreadOnly = unreadOnly, Page = page, PageSize = pageSize,
            }, ct));

        group.MapGet("/unread-count", (INotificationFeedService service, CancellationToken ct) => service.UnreadAsync(ct));

        group.MapPost("/{id:guid}/read", async (Guid id, INotificationFeedService service, CancellationToken ct) =>
        {
            await service.MarkReadAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/read-all", (INotificationFeedService service, CancellationToken ct) => service.MarkAllReadAsync(ct));

        group.MapPost("/{id:guid}/dismiss", async (Guid id, INotificationFeedService service, CancellationToken ct) =>
        {
            await service.DismissAsync(id, ct);
            return Results.NoContent();
        });
    }

    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/analytics", "Analytics");

        group.MapGet("/", (IAnalyticsService service, CancellationToken ct, string? period, Guid? deviceId) =>
            service.GetOverviewAsync(new AnalyticsQuery(period, deviceId), ct));

        group.MapGet("/devices", (IAnalyticsService service, CancellationToken ct) => service.GetDeviceOptionsAsync(ct));
    }

    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/reports", "Status reports");

        group.MapGet("/", (IReportService service, string? month) => Results.Ok(service.GetCatalog(month)));

        group.MapGet("/{type}", async (ReportType type, IReportService service, CancellationToken ct, string? format, string? month) =>
                (await service.GenerateAsync(new ReportRequest(type, format, month), ct)).ToResult())
            .RequireRateLimiting(RateLimitPolicies.Reports);
    }

    public static void MapRouteAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/route-ai", "WonderFleet Route AI");

        group.MapGet("/summary", (IRouteAiService service, CancellationToken ct) => service.GetSummaryAsync(ct));

        group.MapPost("/optimize", (OptimizeRouteRequest request, IRouteAiService service, CancellationToken ct) =>
            service.OptimizeAsync(request, ct)).Validate<OptimizeRouteRequest>();

        group.MapGet("/", (IRouteAiService service, CancellationToken ct, Guid? tripId, string? search, int page = 1, int pageSize = 10) =>
            service.ListAsync(new PageQuery { Search = search, Page = page, PageSize = pageSize }, tripId, ct));

        group.MapGet("/{id:guid}", (Guid id, IRouteAiService service, CancellationToken ct) => service.GetAsync(id, ct));
    }

    public static void MapGeoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/geo", "Geo and weather");

        group.MapGet("/geocode", (string address, IGeoService service, CancellationToken ct) => service.GeocodeAsync(address, ct));

        group.MapGet("/autocomplete", (string input, IGeoService service, CancellationToken ct, string? sessionToken) =>
            service.AutocompleteAsync(input, sessionToken, ct));

        group.MapGet("/weather", (double lat, double lng, IWeatherQueryService service, CancellationToken ct) =>
            service.GetCurrentAsync(lat, lng, ct));

        group.MapGet("/weather/forecast", (double lat, double lng, IWeatherQueryService service, CancellationToken ct, int hours = 24) =>
            service.GetForecastAsync(lat, lng, hours, ct));

        group.MapGet("/weather/trip/{tripId:guid}", (Guid tripId, IWeatherQueryService service, CancellationToken ct) =>
            service.GetTripWeatherAsync(tripId, ct));
    }
}
