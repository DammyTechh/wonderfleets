using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Options;
using WonderFleet.Domain.Entities;

namespace WonderFleet.Application.Features.Auth;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct);
    Task LogoutAsync(string? refreshToken, CancellationToken ct);
    Task<AdminProfileDto> GetCurrentAsync(CancellationToken ct);
}

internal sealed class AuthService(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    IJwtTokenService jwt,
    ISecureTokenGenerator tokens,
    ICurrentActor actor,
    IClock clock,
    IAuditLogger audit,
    IMediaUrlSigner media,
    IOptions<AuthOptions> options,
    ILogger<AuthService> logger) : IAuthService
{
    // One generic message for every failure path: never reveal whether the email exists.
    private const string InvalidCredentials = "Invalid email or password.";

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var normalized = request.Email.Trim().ToUpperInvariant();
        var admin = await db.AdminUsers.FirstOrDefaultAsync(a => a.NormalizedEmail == normalized, ct);

        if (admin is null)
        {
            hasher.SimulateVerify();
            audit.Record("auth.login_failed", "AdminUser", null, new { reason = "unknown_email" });
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException(InvalidCredentials, "invalid_credentials");
        }

        if (admin.IsLockedOut(now))
        {
            hasher.SimulateVerify();
            throw new UnauthorizedException("Too many failed attempts. Try again later.", "account_locked");
        }

        if (!admin.IsActive || !hasher.Verify(request.Password, admin.PasswordHash))
        {
            admin.RegisterFailedLogin(now);
            audit.Record("auth.login_failed", "AdminUser", admin.Id, new { reason = admin.IsActive ? "bad_password" : "inactive" });
            await db.SaveChangesAsync(ct);
            if (admin.IsLockedOut(now))
                logger.LogWarning("Admin {AdminId} locked out after repeated failed logins from {Ip}", admin.Id, actor.IpAddress);
            throw new UnauthorizedException(InvalidCredentials, "invalid_credentials");
        }

        admin.RegisterSuccessfulLogin(now);
        var result = IssueTokens(admin, familyId: Guid.NewGuid(), now);
        audit.Record("auth.login_succeeded", "AdminUser", admin.Id);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = tokens.Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new UnauthorizedException("Session expired. Please sign in again.", "invalid_refresh_token");

        if (stored.RevokedAt is not null)
        {
            // A rotated token was replayed: assume theft and kill the whole session family.
            await RevokeFamilyAsync(stored.FamilyId, "reuse_detected", now, ct);
            audit.Record("auth.refresh_reuse_detected", "AdminUser", stored.AdminUserId, new { stored.FamilyId });
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Refresh token reuse detected for admin {AdminId}; family {FamilyId} revoked", stored.AdminUserId, stored.FamilyId);
            throw new UnauthorizedException("Session expired. Please sign in again.", "invalid_refresh_token");
        }

        if (!stored.IsActive(now))
            throw new UnauthorizedException("Session expired. Please sign in again.", "invalid_refresh_token");

        var admin = await db.AdminUsers.FirstOrDefaultAsync(a => a.Id == stored.AdminUserId, ct);
        if (admin is null || !admin.IsActive)
            throw new UnauthorizedException("Session expired. Please sign in again.", "invalid_refresh_token");

        var result = IssueTokens(admin, stored.FamilyId, now);
        stored.Revoke(now, actor.IpAddress, "rotated", tokens.Hash(result.RefreshToken));
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = tokens.Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return;
        if (actor.AdminId is { } adminId && stored.AdminUserId != adminId) return;

        await RevokeFamilyAsync(stored.FamilyId, "logout", clock.UtcNow, ct);
        audit.Record("auth.logout", "AdminUser", stored.AdminUserId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AdminProfileDto> GetCurrentAsync(CancellationToken ct)
    {
        var id = actor.AdminId ?? throw new UnauthorizedException();
        var admin = await db.AdminUsers.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new UnauthorizedException();
        return admin.ToDto(media);
    }

    private AuthResult IssueTokens(AdminUser admin, Guid familyId, DateTimeOffset now)
    {
        var access = jwt.CreateAdminToken(admin);
        var raw = tokens.Generate(48);
        var refresh = new RefreshToken
        {
            AdminUserId = admin.Id,
            TokenHash = tokens.Hash(raw),
            FamilyId = familyId,
            CreatedAt = now,
            CreatedByIp = actor.IpAddress,
            ExpiresAt = now.AddDays(options.Value.RefreshTokenDays),
        };
        db.RefreshTokens.Add(refresh);
        return new AuthResult(access.Token, access.ExpiresAt, raw, refresh.ExpiresAt, admin.ToDto(media));
    }

    private async Task RevokeFamilyAsync(Guid familyId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var family = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var token in family) token.Revoke(now, actor.IpAddress, reason);
    }
}

internal static class AdminMapping
{
    public static AdminProfileDto ToDto(this AdminUser a, IMediaUrlSigner media) => new(
        a.Id, a.AdminCode, a.FullName, a.Email, a.PhoneNumber, a.Address,
        media.Sign(a.PhotoPath), a.Role.ToString(), a.MustChangePassword, a.LastLoginAt);
}
