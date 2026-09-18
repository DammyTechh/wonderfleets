using WonderFleet.Domain.Entities;

namespace WonderFleet.Application.Common.Interfaces;

// ---------------------------------------------------------------- messaging
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string? TextBody);
public sealed record SendResult(bool Success, string? ProviderMessageId, string? Error);

public interface IEmailSender
{
    Task<SendResult> SendAsync(EmailMessage message, CancellationToken ct);
}

public interface ISmsSender
{
    Task<SendResult> SendAsync(string phoneE164, string message, CancellationToken ct);
}

public sealed record RenderedEmail(string Subject, string Html, string Text);

public interface IEmailTemplateRenderer
{
    RenderedEmail Render(string templateKey, IReadOnlyDictionary<string, string?> model);
}

// ---------------------------------------------------------------- maps / weather / AI
public sealed record GeoPoint(double Latitude, double Longitude);
public sealed record GeocodeResult(string FormattedAddress, GeoPoint Location, string? City, string? State);
public sealed record PlaceSuggestion(string PlaceId, string Description);

public sealed record RouteOption(
    int Index, double DistanceKm, int DurationMinutes, string EncodedPolyline, string Description, IReadOnlyList<string> Warnings,
    /// Duration without traffic. The ratio against DurationMinutes is the congestion signal used by fuel planning.
    int? StaticDurationMinutes = null);

public interface IMapsService
{
    Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken ct);
    Task<IReadOnlyList<PlaceSuggestion>> AutocompleteAsync(string input, string? sessionToken, CancellationToken ct);
    Task<IReadOnlyList<RouteOption>> ComputeRoutesAsync(GeoPoint origin, GeoPoint destination, DateTimeOffset departure, CancellationToken ct);
}

public sealed record WeatherNow(double TemperatureC, double RelativeHumidity, string Condition, string? IconUri, DateTimeOffset ObservedAt);
public sealed record WeatherHour(DateTimeOffset Time, double TemperatureC, double RelativeHumidity, string Condition, int? PrecipitationProbability);

public interface IWeatherService
{
    Task<WeatherNow?> GetCurrentAsync(GeoPoint point, CancellationToken ct);
    Task<IReadOnlyList<WeatherHour>> GetHourlyForecastAsync(GeoPoint point, int hours, CancellationToken ct);
}

public sealed record RouteAdviceRequest(
    string Origin, string Destination, DateTimeOffset RequestedDeparture, IReadOnlyList<string> Produce,
    decimal? MaxSafeTemperature, IReadOnlyList<RouteWeatherContext> Routes);

public sealed record RouteWeatherContext(RouteOption Route, IReadOnlyList<WeatherHour> WeatherSamples);

public sealed record RouteAdvice(
    int SelectedRouteIndex, DateTimeOffset? RecommendedDeparture, string HeatRiskLevel,
    decimal EstimatedSpoilageReductionPct, string Summary, IReadOnlyList<string> DriverTips, string Model);

/// LLM-backed route ranking (OpenAI). Implementations must return null on failure so a heuristic can take over.
public interface IRouteAdvisor
{
    Task<RouteAdvice?> AdviseAsync(RouteAdviceRequest request, CancellationToken ct);
}

// ---------------------------------------------------------------- device cloud
/// Raw node the firmware writes at vehicles/{key}. Unknown/extra fields are ignored.
public sealed record DeviceSnapshot(
    string FirebaseKey, decimal? Temperature, decimal? Humidity, double? Latitude, double? Longitude,
    bool IsActive, int? BatteryLevel, DateTimeOffset? DeviceTimestamp);

public interface IDeviceCloudGateway
{
    Task<IReadOnlyList<DeviceSnapshot>> ReadAllSnapshotsAsync(CancellationToken ct);
    /// Pushes cargo limits to settings/{key} so the device can alarm locally even when offline.
    Task PushThresholdsAsync(string firebaseKey, CargoThresholds thresholds, CancellationToken ct);
}

// ---------------------------------------------------------------- realtime
public sealed record TelemetryEvent(
    Guid? TripId, Guid DeviceId, string? FleetNumber, double? Latitude, double? Longitude, double? SpeedKmh,
    decimal? Temperature, decimal? Humidity, string SensorStatus, string? TripStatus, DateTimeOffset RecordedAt);

public sealed record AlertEvent(Guid AlertId, Guid? TripId, string Type, string Severity, string Status, string Title, string Message, DateTimeOffset At);

public interface IRealtimePublisher
{
    Task PublishTelemetryAsync(TelemetryEvent evt, CancellationToken ct);
    Task PublishAlertAsync(AlertEvent evt, CancellationToken ct);
    Task PublishNotificationAsync(Guid notificationId, string title, string category, CancellationToken ct);
}
