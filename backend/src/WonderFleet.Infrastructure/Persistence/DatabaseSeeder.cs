using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Infrastructure.Persistence;

public sealed class SeedOptions
{
    public const string Section = "Seed";
    public string AdminEmail { get; set; } = "ofeminiagrictech@gmail.com";
    public string? AdminPassword { get; set; }
    public string AdminName { get; set; } = "OfeminiAgricTech Admin";
    /// Forces a password change banner after the first sign-in.
    public bool RequirePasswordChange { get; set; } = true;
}

/// There is no admin registration: the first administrator is seeded from configuration.
public sealed class DatabaseSeeder(
    ApplicationDbContext db,
    IPasswordHasher hasher,
    ICodeGenerator codes,
    IOptions<SeedOptions> options,
    IHostEnvironment environment,
    ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var o = options.Value;
        var normalized = o.AdminEmail.Trim().ToUpperInvariant();
        if (await db.AdminUsers.AnyAsync(a => a.NormalizedEmail == normalized, ct)) return;

        if (string.IsNullOrWhiteSpace(o.AdminPassword))
        {
            logger.LogWarning("Seed:AdminPassword is not set; the default administrator was not created.");
            return;
        }
        if (o.AdminPassword.Length < 10 && environment.IsProduction())
            throw new InvalidOperationException("Seed:AdminPassword is too weak for production.");

        var admin = new AdminUser
        {
            AdminCode = await codes.NextAdminCodeAsync(ct),
            FullName = o.AdminName,
            Email = o.AdminEmail.Trim(),
            NormalizedEmail = normalized,
            Role = AdminRole.SuperAdmin,
            IsActive = true,
            MustChangePassword = o.RequirePasswordChange,
        };
        admin.SetPassword(hasher.Hash(o.AdminPassword), DateTimeOffset.UtcNow);
        db.AdminUsers.Add(admin);
        db.AuditLogs.Add(new AuditLog
        {
            ActorType = ActorType.System,
            Action = "admin.seeded",
            EntityType = nameof(AdminUser),
            EntityId = admin.Id.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded administrator {AdminCode} ({Email})", admin.AdminCode, admin.Email);
    }
}
