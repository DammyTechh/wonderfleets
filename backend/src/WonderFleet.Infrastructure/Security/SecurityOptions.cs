namespace WonderFleet.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "wonderfleet-api";
    public string AdminAudience { get; set; } = "wonderfleet-admin";
    public string PortalAudience { get; set; } = "wonderfleet-portal";
    /// Base64 keys of at least 256 bits. Admin and portal tokens use different keys so one can never be replayed as the other.
    public string? AdminSigningKey { get; set; }
    public string? PortalSigningKey { get; set; }
    public int AdminTokenMinutes { get; set; } = 15;
}

public sealed class SecurityOptions
{
    public const string Section = "Security";
    /// Base64 key (>= 256 bits) for HMAC hashing of refresh/share tokens.
    public string? TokenHashKey { get; set; }
    /// Base64 key (>= 256 bits) for signed media URLs.
    public string? MediaSigningKey { get; set; }
    public int MediaUrlMinutes { get; set; } = 60;
    public int BcryptWorkFactor { get; set; } = 12;
}

public static class TokenClaims
{
    public const string TokenType = "wf_typ";
    public const string SecurityStamp = "wf_ss";
    public const string Audience = "wf_aud";
    public const string OwnerId = "wf_owner";
    public const string AdminType = "admin";
    public const string PortalType = "portal";
}

internal static class KeyMaterial
{
    public const int MinimumBytes = 32;

    /// Decodes a configured base64 key; in Development an ephemeral key is generated so the app still starts.
    public static byte[] Resolve(string? configured, string name, bool allowEphemeral)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(configured.Trim()); }
            catch (FormatException) { throw new InvalidOperationException($"{name} must be a base64 string."); }
            if (bytes.Length < MinimumBytes) throw new InvalidOperationException($"{name} must be at least {MinimumBytes * 8} bits.");
            return bytes;
        }
        if (!allowEphemeral) throw new InvalidOperationException($"{name} is required. Generate one with: openssl rand -base64 48");
        return System.Security.Cryptography.RandomNumberGenerator.GetBytes(48);
    }
}

/// Resolved key ring (singleton) so every component sees the same keys even when ephemeral.
public sealed class KeyRing
{
    public KeyRing(JwtOptions jwt, SecurityOptions security, bool allowEphemeral)
    {
        AdminSigningKey = KeyMaterial.Resolve(jwt.AdminSigningKey, "Jwt:AdminSigningKey", allowEphemeral);
        PortalSigningKey = KeyMaterial.Resolve(jwt.PortalSigningKey, "Jwt:PortalSigningKey", allowEphemeral);
        TokenHashKey = KeyMaterial.Resolve(security.TokenHashKey, "Security:TokenHashKey", allowEphemeral);
        MediaSigningKey = KeyMaterial.Resolve(security.MediaSigningKey, "Security:MediaSigningKey", allowEphemeral);
        if (AdminSigningKey.AsSpan().SequenceEqual(PortalSigningKey))
            throw new InvalidOperationException("Jwt:AdminSigningKey and Jwt:PortalSigningKey must be different.");
    }

    public byte[] AdminSigningKey { get; }
    public byte[] PortalSigningKey { get; }
    public byte[] TokenHashKey { get; }
    public byte[] MediaSigningKey { get; }
}
