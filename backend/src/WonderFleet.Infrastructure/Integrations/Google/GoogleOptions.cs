namespace WonderFleet.Infrastructure.Integrations.Google;

public sealed class GoogleOptions
{
    public const string Section = "Google";
    /// Maps Platform key with Geocoding, Places (New), Routes and Weather enabled. Restrict it by IP in the Cloud console.
    public string? ApiKey { get; set; }
    /// Biases geocoding and autocomplete to Nigeria.
    public string RegionCode { get; set; } = "NG";
    public int TimeoutSeconds { get; set; } = 15;
    public int GeocodeCacheHours { get; set; } = 24;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class WeatherOptions
{
    public const string Section = "Weather";
    /// Google | OpenMeteo
    public string Provider { get; set; } = "Google";
    /// Falls back to Open-Meteo (keyless) when the Google Weather API fails or is not enabled.
    public bool FallbackToOpenMeteo { get; set; } = true;
    public string OpenMeteoBaseUrl { get; set; } = "https://api.open-meteo.com";
    public int CurrentCacheMinutes { get; set; } = 10;
    public int ForecastCacheMinutes { get; set; } = 30;
}
