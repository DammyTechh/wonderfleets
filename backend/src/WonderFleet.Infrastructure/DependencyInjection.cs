using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using QuestPDF.Infrastructure;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Infrastructure.BackgroundJobs;
using WonderFleet.Infrastructure.Integrations;
using WonderFleet.Infrastructure.Integrations.Firebase;
using WonderFleet.Infrastructure.Integrations.Google;
using WonderFleet.Infrastructure.Integrations.OpenAi;
using WonderFleet.Infrastructure.Notifications;
using WonderFleet.Infrastructure.Persistence;
using WonderFleet.Infrastructure.Realtime;
using WonderFleet.Infrastructure.Reporting;
using WonderFleet.Infrastructure.Security;
using WonderFleet.Infrastructure.Services;
using WonderFleet.Infrastructure.Storage;

namespace WonderFleet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.Section));
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.Section));
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.Section));
        services.AddOptions<SecurityOptions>().Bind(configuration.GetSection(SecurityOptions.Section));
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.Section));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.Section));
        services.AddOptions<SmsOptions>().Bind(configuration.GetSection(SmsOptions.Section));
        services.AddOptions<FirebaseOptions>().Bind(configuration.GetSection(FirebaseOptions.Section));
        services.AddOptions<GoogleOptions>().Bind(configuration.GetSection(GoogleOptions.Section));
        services.AddOptions<WeatherOptions>().Bind(configuration.GetSection(WeatherOptions.Section));
        services.AddOptions<OpenAiOptions>().Bind(configuration.GetSection(OpenAiOptions.Section));

        services.AddMemoryCache();
        services.AddSingleton<IClock, SystemClock>();

        AddPersistence(services, configuration);
        AddSecurity(services, environment);
        AddMessaging(services, configuration);
        AddIntegrations(services, configuration);

        services.AddSingleton<IReportRenderer, ReportRenderer>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IRealtimePublisher, SignalRRealtimePublisher>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<ICodeGenerator, CodeGenerator>();
        services.AddScoped<DatabaseSeeder>();
        services.AddSingleton<SqlMigrationRunner>();
        services.AddSingleton<LeaderLock>();

        QuestPDF.Settings.License = LicenseType.Community;

        services.AddHostedService<TelemetryPollingWorker>();
        services.AddHostedService<TelemetryMonitorWorker>();
        services.AddHostedService<NotificationDispatchWorker>();

        return services;
    }

    /// Readiness probe for the API, kept here so the database stays an infrastructure concern.
    public static IServiceCollection AddDatabaseHealthCheck(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);
        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        // Render exposes DATABASE_URL as a postgres:// URL; ConnectionStrings:Default wins when both are set.
        var raw = configuration.GetConnectionString("Default")
                  ?? configuration["Database:ConnectionString"]
                  ?? configuration["DATABASE_URL"]
                  ?? throw new InvalidOperationException(
                      "No database connection string. Set ConnectionStrings:Default or DATABASE_URL.");
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var builder = new NpgsqlDataSourceBuilder(DatabaseOptions.Build(raw, options));
            builder.EnableDynamicJson();
            return builder.Build();
        });

        services.AddSingleton<AuditableEntityInterceptor>();
        services.AddDbContext<ApplicationDbContext>((provider, options) =>
        {
            var timeout = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.CommandTimeoutSeconds;
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), npgsql =>
            {
                npgsql.CommandTimeout(Math.Clamp(timeout, 5, 300));
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(3), null);
                // Trip and partner reads include several collections; without this a
                // single JOIN would multiply rows across them (cartesian explosion).
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
            options.AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddSingleton<IAnalyticsReadStore, AnalyticsReadStore>();
    }

    private static void AddSecurity(IServiceCollection services, IHostEnvironment environment)
    {
        services.AddSingleton(provider => new KeyRing(
            provider.GetRequiredService<IOptions<JwtOptions>>().Value,
            provider.GetRequiredService<IOptions<SecurityOptions>>().Value,
            allowEphemeral: environment.IsDevelopment()));

        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ISecureTokenGenerator, HmacTokenGenerator>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<MediaUrlSigner>();
        services.AddSingleton<IMediaUrlSigner>(provider => provider.GetRequiredService<MediaUrlSigner>());
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

        var emailProvider = configuration["Email:Provider"] ?? "Log";
        if (string.Equals(emailProvider, "ResendApi", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IEmailSender, ResendApiEmailSender>(client => client.Timeout = TimeSpan.FromSeconds(20))
                .AddStandardResilienceHandler();
        }
        else if (string.Equals(emailProvider, "ResendSmtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, ResendSmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, LogEmailSender>();
        }

        var smsProvider = configuration["Sms:Provider"] ?? "Log";
        if (string.Equals(smsProvider, "Termii", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<ISmsSender, TermiiSmsSender>(client => client.Timeout = TimeSpan.FromSeconds(20))
                .AddStandardResilienceHandler();
        }
        else if (string.Equals(smsProvider, "Twilio", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<ISmsSender, TwilioSmsSender>(client => client.Timeout = TimeSpan.FromSeconds(20))
                .AddStandardResilienceHandler();
        }
        else
        {
            services.AddSingleton<ISmsSender, LogSmsSender>();
        }
    }

    private static void AddIntegrations(IServiceCollection services, IConfiguration configuration)
    {
        var firebaseTimeout = configuration.GetValue("Firebase:TimeoutSeconds", 20);
        services.AddHttpClient<IDeviceCloudGateway, FirebaseRtdbGateway>(client =>
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(firebaseTimeout, 5, 60)))
            .AddStandardResilienceHandler();

        var googleTimeout = configuration.GetValue("Google:TimeoutSeconds", 15);
        services.AddHttpClient(HttpClients.Google, client => client.Timeout = TimeSpan.FromSeconds(Math.Clamp(googleTimeout, 5, 60)))
            .AddStandardResilienceHandler();
        services.AddHttpClient(HttpClients.OpenMeteo, client => client.Timeout = TimeSpan.FromSeconds(15))
            .AddStandardResilienceHandler();
        services.AddHttpClient(HttpClients.OpenAi, client =>
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("OpenAi:TimeoutSeconds", 30), 10, 120)))
            .AddStandardResilienceHandler(options =>
            {
                // A single slow completion should not be retried aggressively.
                options.Retry.MaxRetryAttempts = 1;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(45);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(100);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(90);
            });

        services.AddSingleton<IMapsService, GoogleMapsService>();
        services.AddSingleton<IWeatherService, WeatherService>();
        services.AddSingleton<IRouteAdvisor, OpenAiRouteAdvisor>();
    }
}
