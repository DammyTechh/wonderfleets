using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Options;
using WonderFleet.Domain.Entities;

namespace WonderFleet.Infrastructure.Security;

internal sealed class BcryptPasswordHasher(IOptions<SecurityOptions> options) : IPasswordHasher
{
    // Computed once; used to equalise timing for unknown accounts.
    private static readonly Lazy<string> DummyHash = new(() => BCrypt.Net.BCrypt.EnhancedHashPassword(Guid.NewGuid().ToString("N"), 12));

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.EnhancedHashPassword(password, Math.Clamp(options.Value.BcryptWorkFactor, 10, 15));

    public bool Verify(string password, string hash)
    {
        try { return BCrypt.Net.BCrypt.EnhancedVerify(password, hash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }

    public void SimulateVerify() => _ = BCrypt.Net.BCrypt.EnhancedVerify("not-the-password", DummyHash.Value);
}

internal sealed class HmacTokenGenerator(KeyRing keys) : ISecureTokenGenerator
{
    public string Generate(int byteLength = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(Math.Clamp(byteLength, 16, 64));
        return Base64Url(bytes);
    }

    public string Hash(string token) =>
        Convert.ToHexString(HMACSHA256.HashData(keys.TokenHashKey, Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class JwtTokenService(KeyRing keys, IOptions<JwtOptions> options, IOptions<ShareLinkOptions> shareOptions, IClock clock) : IJwtTokenService
{
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public IssuedToken CreateAdminToken(AdminUser admin)
    {
        var o = options.Value;
        var now = clock.UtcNow;
        var expires = now.AddMinutes(Math.Clamp(o.AdminTokenMinutes, 5, 60));
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = admin.Id.ToString(),
            [JwtRegisteredClaimNames.Email] = admin.Email,
            [JwtRegisteredClaimNames.Name] = admin.FullName,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            [ClaimTypes.Role] = admin.Role.ToString(),
            [TokenClaims.TokenType] = TokenClaims.AdminType,
            [TokenClaims.SecurityStamp] = admin.SecurityStamp,
        };
        return new IssuedToken(Create(claims, o.AdminAudience, keys.AdminSigningKey, now, expires), expires);
    }

    public IssuedToken CreatePortalToken(ShareLink link, DateTimeOffset notAfter)
    {
        var o = options.Value;
        var now = clock.UtcNow;
        var cap = now.AddMinutes(Math.Max(5, shareOptions.Value.PortalSessionMinutes));
        var expires = notAfter < cap ? notAfter : cap;
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = link.Id.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            [ClaimTypes.Role] = link.Audience.ToString(),
            [TokenClaims.TokenType] = TokenClaims.PortalType,
            [TokenClaims.Audience] = link.Audience.ToString(),
            [TokenClaims.OwnerId] = (link.LogisticsPartnerId ?? link.AgroProcessorId ?? Guid.Empty).ToString(),
        };
        return new IssuedToken(Create(claims, o.PortalAudience, keys.PortalSigningKey, now, expires), expires);
    }

    private string Create(Dictionary<string, object> claims, string audience, byte[] key, DateTimeOffset now, DateTimeOffset expires) =>
        _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256),
        });
}

/// Signed, expiring media URLs: /api/v1/media?p={path}&e={unix}&s={hmac}. Expiry is rounded to the hour so URLs cache well.
public sealed class MediaUrlSigner(KeyRing keys, IOptions<SecurityOptions> options, IOptions<AppOptions> app, IClock clock) : IMediaUrlSigner
{
    public string? Sign(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath)) return null;
        var minutes = Math.Clamp(options.Value.MediaUrlMinutes, 5, 24 * 60);
        var expiry = clock.UtcNow.AddMinutes(minutes);
        var rounded = new DateTimeOffset(expiry.Year, expiry.Month, expiry.Day, expiry.Hour, 0, 0, TimeSpan.Zero).AddHours(1);
        var exp = rounded.ToUnixTimeSeconds();
        var sig = Signature(storagePath, exp);
        return $"{app.Value.PublicApiBaseUrl.TrimEnd('/')}/api/v1/media?p={Uri.EscapeDataString(storagePath)}&e={exp}&s={sig}";
    }

    public bool Verify(string storagePath, long expiresUnix, string signature)
    {
        if (DateTimeOffset.FromUnixTimeSeconds(expiresUnix) < clock.UtcNow) return false;
        var expected = Encoding.ASCII.GetBytes(Signature(storagePath, expiresUnix));
        var given = Encoding.ASCII.GetBytes(signature ?? "");
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    private string Signature(string path, long exp) =>
        HmacTokenGenerator.Base64Url(HMACSHA256.HashData(keys.MediaSigningKey,
            Encoding.UTF8.GetBytes(path + "|" + exp.ToString(CultureInfo.InvariantCulture))));
}
