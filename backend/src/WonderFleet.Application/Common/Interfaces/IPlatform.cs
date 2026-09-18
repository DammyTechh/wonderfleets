using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Common.Interfaces;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// Who is performing the current operation (admin, share-link visitor, or background system).
public interface ICurrentActor
{
    ActorType ActorType { get; }
    Guid? AdminId { get; }
    Guid? ShareLinkId { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
    /// Runs a verification against a fixed hash so unknown-user logins take the same time as real ones.
    void SimulateVerify();
}

public interface ISecureTokenGenerator
{
    /// Cryptographically random, URL-safe token.
    string Generate(int byteLength = 32);
    /// Keyed (HMAC-SHA256) hash — tokens are useless even if the database leaks.
    string Hash(string token);
}

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt);

public interface IJwtTokenService
{
    IssuedToken CreateAdminToken(AdminUser admin);
    IssuedToken CreatePortalToken(ShareLink link, DateTimeOffset notAfter);
}

public interface IAuditLogger
{
    /// Adds an audit row to the current unit of work (persisted by the caller's SaveChanges).
    void Record(string action, string? entityType = null, object? entityId = null, object? details = null);
}

public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string category, string extension, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct);
    Task DeleteAsync(string storagePath, CancellationToken ct);
}

public sealed record UploadedFile(string FileName, string ContentType, long Length, Func<Stream> OpenReadStream);

/// Produces short-lived HMAC-signed URLs for stored media so <img> tags work without exposing storage.
public interface IMediaUrlSigner
{
    string? Sign(string? storagePath);
}
