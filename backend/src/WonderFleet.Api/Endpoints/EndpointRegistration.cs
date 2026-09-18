using WonderFleet.Api.Setup;

namespace WonderFleet.Api.Endpoints;

internal static class EndpointRegistration
{
    public const string BasePath = "/api/v1";

    public static IEndpointRouteBuilder MapWonderFleetEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(BasePath);

        // Endpoint documentation lives in docs/swagger (hand-written OpenAPI), not in these files.
        api.MapAuthEndpoints();
        api.MapProfileEndpoints();
        api.MapDashboardEndpoints();
        api.MapPartnerEndpoints();
        api.MapProcessorEndpoints();
        api.MapDriverEndpoints();
        api.MapDeviceEndpoints();
        api.MapProduceEndpoints();
        api.MapFleetEndpoints();
        api.MapTripEndpoints();
        api.MapFuelEndpoints();
        api.MapTrackingEndpoints();
        api.MapShareLinkEndpoints();
        api.MapAlertEndpoints();
        api.MapNotificationEndpoints();
        api.MapAnalyticsEndpoints();
        api.MapReportEndpoints();
        api.MapRouteAiEndpoints();
        api.MapGeoEndpoints();
        api.MapPortalEndpoints();
        api.MapMediaEndpoints();
        api.MapTelemetryEndpoints();

        return app;
    }

    /// Admin-only group with a tag used by the hand-written OpenAPI documents.
    public static RouteGroupBuilder AdminGroup(this IEndpointRouteBuilder app, string prefix, string tag) =>
        app.MapGroup(prefix).RequireAuthorization(AuthPolicies.Admin).WithTags(tag);

    public static RouteGroupBuilder PortalGroup(this IEndpointRouteBuilder app, string prefix, string policy, string tag) =>
        app.MapGroup(prefix).RequireAuthorization(policy).WithTags(tag);
}
