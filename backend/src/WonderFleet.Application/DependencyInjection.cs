using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WonderFleet.Application.Common.Options;
using WonderFleet.Application.Features.Alerts;
using WonderFleet.Application.Features.Analytics;
using WonderFleet.Application.Features.Auth;
using WonderFleet.Application.Features.Dashboard;
using WonderFleet.Application.Features.Devices;
using WonderFleet.Application.Features.Fleet;
using WonderFleet.Application.Features.Fuel;
using WonderFleet.Application.Features.Geo;
using WonderFleet.Application.Features.Notifications;
using WonderFleet.Application.Features.Partners;
using WonderFleet.Application.Features.Portal;
using WonderFleet.Application.Features.Profile;
using WonderFleet.Application.Features.RouteAi;
using WonderFleet.Application.Features.ShareLinks;
using WonderFleet.Application.Features.Telemetry;
using WonderFleet.Application.Features.Tracking;
using WonderFleet.Application.Features.Weather;

namespace WonderFleet.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AppOptions>().Bind(configuration.GetSection(AppOptions.Section)).ValidateOnStart();
        services.AddOptions<ShareLinkOptions>().Bind(configuration.GetSection(ShareLinkOptions.Section));
        services.AddOptions<AlertingOptions>().Bind(configuration.GetSection(AlertingOptions.Section));
        services.AddOptions<TelemetryOptions>().Bind(configuration.GetSection(TelemetryOptions.Section));
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.Section));
        services.AddOptions<FuelOptions>().Bind(configuration.GetSection(FuelOptions.Section));

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton);

        services.AddSingleton<TelemetrySyncState>();

        // Internal building blocks
        services.AddScoped<TrackingQueries>();
        services.AddScoped<PartnerAccessRevoker>();
        services.AddScoped<ThresholdPublisher>();
        services.AddScoped<TripNotifications>();
        services.AddScoped<INotificationComposer, NotificationComposer>();
        services.AddScoped<IAlertEngine, AlertEngine>();

        // Feature services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ILogisticsPartnerService, LogisticsPartnerService>();
        services.AddScoped<IAgroProcessorService, AgroProcessorService>();
        services.AddScoped<IDriverService, DriverService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IProduceService, ProduceService>();
        services.AddScoped<IFleetService, FleetService>();
        services.AddScoped<ITripService, TripService>();
        services.AddScoped<IFuelService, FuelService>();
        services.AddScoped<ITrackingService, TrackingService>();
        services.AddScoped<IShareLinkService, ShareLinkService>();
        services.AddScoped<IPortalService, PortalService>();
        services.AddScoped<IAlertAdminService, AlertAdminService>();
        services.AddScoped<INotificationFeedService, NotificationFeedService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IRouteAiService, RouteAiService>();
        services.AddScoped<IWeatherQueryService, WeatherQueryService>();
        services.AddScoped<IGeoService, GeoService>();
        services.AddScoped<ITelemetryIngestionService, TelemetryIngestionService>();
        services.AddScoped<ITelemetryMonitor, TelemetryMonitor>();

        return services;
    }
}
