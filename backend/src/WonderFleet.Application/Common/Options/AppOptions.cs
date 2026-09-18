namespace WonderFleet.Application.Common.Options;

public sealed class AppOptions
{
    public const string Section = "App";
    /// Public URL of the React app; used to build share links and email buttons.
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
    public string PublicApiBaseUrl { get; set; } = "http://localhost:8080";
    public string SupportEmail { get; set; } = "ofeminiagrictech@gmail.com";
    public string LogoUrl { get; set; } = "";
    public string TimeZoneId { get; set; } = "Africa/Lagos";
    /// Browser origins allowed to call the API (defaults to FrontendBaseUrl).
    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class ShareLinkOptions
{
    public const string Section = "ShareLinks";
    /// Grace period after the latest expected arrival before a link expires by time.
    public int GraceHoursAfterArrival { get; set; } = 12;
    public int MaxLifetimeHours { get; set; } = 24 * 7;
    public int PortalSessionMinutes { get; set; } = 120;
}

public sealed class AlertingOptions
{
    public const string Section = "Alerting";
    public bool NotifyLogisticsPartnerOnCritical { get; set; } = true;
    public bool NotifyAgroProcessorOnCritical { get; set; } = true;
    public int MonitorIntervalSeconds { get; set; } = 60;

    /// How often the notification outbox is drained.
    public int DispatchIntervalSeconds { get; set; } = 5;
    /// Low-battery alerts close once battery recovers above threshold + hysteresis.
    public int BatteryHysteresis { get; set; } = 5;
}

public sealed class TelemetryOptions
{
    public const string Section = "Telemetry";
    public bool PollingEnabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 10;
    /// Store an unchanged payload at most this often (keeps a heartbeat trail without flooding the table).
    public int HeartbeatSeconds { get; set; } = 300;
    /// Shared secret for the optional HTTPS webhook (X-WonderFleet-Signature: sha256=HMAC(body)).
    public string? WebhookSecret { get; set; }
}

public sealed class AuthOptions
{
    public const string Section = "Auth";
    public int RefreshTokenDays { get; set; } = 7;
    /// Cookie | Body | Both. Cookie is safest when API and app share a registrable domain.
    public string RefreshTokenTransport { get; set; } = "Both";
}

public sealed class FuelOptions
{
    public const string Section = "Fuel";
    /// Share of a typical corridor on rough or unpaved surface. Nigerian agri-corridors are far from perfect asphalt.
    public double DefaultRoughRoadShare { get; set; } = 0.25;
    /// Loading, unloading and checkpoint time the engine (and the reefer) keeps running.
    public double DefaultIdleHours { get; set; } = 1.5;
    /// Extra idle time allowed per eight hours of driving, for queues and rest stops.
    public double IdleHoursPerDrivingDay { get; set; } = 0.5;
    public string Currency { get; set; } = "NGN";
    public decimal FallbackDieselPrice { get; set; } = 1150m;
    public decimal FallbackPetrolPrice { get; set; } = 950m;
    /// Average speed assumed when no route service is available.
    public double FallbackAverageSpeedKmh { get; set; } = 45;
}
