namespace WonderFleet.Infrastructure.Integrations.Firebase;

public sealed class FirebaseOptions
{
    public const string Section = "Firebase";

    /// Realtime Database URL, e.g. https://wonderfleet-12693-default-rtdb.europe-west1.firebasedatabase.app
    public string? DatabaseUrl { get; set; }

    /// Node holding the live device documents (the hardware writes vehicles/{key}).
    public string DevicesNode { get; set; } = "vehicles";

    /// Node the API writes cargo limits to (settings/{key}).
    public string SettingsNode { get; set; } = "settings";

    /// Service account JSON (raw or base64). Preferred: least privilege and short-lived tokens.
    public string? ServiceAccountJson { get; set; }

    /// Legacy database secret. Only used when no service account is configured.
    public string? DatabaseSecret { get; set; }

    /// Rules allow public reads on some prototype databases; then no credential is needed.
    public bool AllowUnauthenticated { get; set; }

    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(DatabaseUrl);

    /// True when a request to the database can actually be authorised.
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(ServiceAccountJson) || !string.IsNullOrWhiteSpace(DatabaseSecret) || AllowUnauthenticated;
}
