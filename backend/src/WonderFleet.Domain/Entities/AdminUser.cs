using WonderFleet.Domain.Common;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Domain.Entities;

public class AdminUser : AuditableEntity
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    public string AdminCode { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string NormalizedEmail { get; set; } = default!;
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? PhotoPath { get; set; }
    public string PasswordHash { get; private set; } = default!;
    public AdminRole Role { get; set; } = AdminRole.Admin;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutEnd { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset? PasswordChangedAt { get; private set; }
    /// Rotated on password change; embedded in JWTs so previously issued tokens die immediately.
    public string SecurityStamp { get; private set; } = Guid.NewGuid().ToString("N");

    public List<RefreshToken> RefreshTokens { get; } = [];

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd.HasValue && LockoutEnd.Value > now;

    public void SetPassword(string hash, DateTimeOffset now)
    {
        PasswordHash = hash;
        PasswordChangedAt = now;
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public void RegisterFailedLogin(DateTimeOffset now)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedAttempts)
        {
            LockoutEnd = now.Add(LockoutWindow);
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutEnd = null;
        LastLoginAt = now;
    }
}

public class RefreshToken : Entity
{
    public Guid AdminUserId { get; set; }
    public string TokenHash { get; set; } = default!;
    /// Every token issued from one login shares a family; replaying a rotated token revokes the family.
    public Guid FamilyId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? RevokedReason { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, string? ip, string reason, string? replacedBy = null)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        RevokedByIp = ip;
        RevokedReason = reason;
        ReplacedByTokenHash = replacedBy;
    }
}
