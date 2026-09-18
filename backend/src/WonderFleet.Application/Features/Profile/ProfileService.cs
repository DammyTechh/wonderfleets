using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Features.Auth;
using WonderFleet.Application.Features.Notifications;

namespace WonderFleet.Application.Features.Profile;

public sealed record UpdateProfileRequest(string FullName, string Email, string? PhoneNumber, string? Address);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName).Name();
        RuleFor(x => x.Email).EmailAddressStrict();
        RuleFor(x => x.PhoneNumber!).PhoneNumber().When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.Address).MaximumLength(300);
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).StrongPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("New password must differ from the current password.");
        RuleFor(x => x.ConfirmPassword).Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}

public interface IProfileService
{
    Task<AdminProfileDto> GetAsync(CancellationToken ct);
    Task<AdminProfileDto> UpdateAsync(UpdateProfileRequest request, CancellationToken ct);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct);
    Task<AdminProfileDto> UploadPhotoAsync(UploadedFile file, CancellationToken ct);
}

internal sealed class ProfileService(
    IApplicationDbContext db,
    ICurrentActor actor,
    IPasswordHasher hasher,
    IClock clock,
    IAuditLogger audit,
    IFileStorage storage,
    IMediaUrlSigner media,
    INotificationComposer notify) : IProfileService
{
    public async Task<AdminProfileDto> GetAsync(CancellationToken ct) => (await LoadAsync(ct)).ToDto(media);

    public async Task<AdminProfileDto> UpdateAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        var admin = await LoadAsync(ct);
        var normalized = request.Email.Trim().ToUpperInvariant();
        if (normalized != admin.NormalizedEmail &&
            await db.AdminUsers.AnyAsync(a => a.NormalizedEmail == normalized && a.Id != admin.Id, ct))
            throw new ConflictException("That email address is already in use.");

        admin.FullName = request.FullName.Trim();
        admin.Email = request.Email.Trim();
        admin.NormalizedEmail = normalized;
        admin.PhoneNumber = request.PhoneNumber is null ? null : Phone.ToE164(request.PhoneNumber);
        admin.Address = Text.Trimmed(request.Address);
        audit.Record("profile.updated", "AdminUser", admin.Id);
        await db.SaveChangesAsync(ct);
        return admin.ToDto(media);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var admin = await LoadAsync(ct);
        if (!hasher.Verify(request.CurrentPassword, admin.PasswordHash))
            throw RequestValidationException.For(nameof(request.CurrentPassword), "Current password is incorrect.");

        var now = clock.UtcNow;
        admin.SetPassword(hasher.Hash(request.NewPassword), now);
        admin.MustChangePassword = false;

        // Sign out every other session.
        var sessions = await db.RefreshTokens.Where(t => t.AdminUserId == admin.Id && t.RevokedAt == null).ToListAsync(ct);
        foreach (var s in sessions) s.Revoke(now, actor.IpAddress, "password_changed");

        notify.Email(admin.Email, EmailTemplates.PasswordChanged, new Dictionary<string, string?>
        {
            ["Name"] = admin.FullName,
            ["ChangedAt"] = now.ToOffset(TimeSpan.FromHours(1)).ToString("dd MMM yyyy, HH:mm") + " WAT",
            ["IpAddress"] = actor.IpAddress ?? "unknown",
        });
        audit.Record("profile.password_changed", "AdminUser", admin.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AdminProfileDto> UploadPhotoAsync(UploadedFile file, CancellationToken ct)
    {
        var (ext, _) = FileRules.EnsureImage(file);
        var admin = await LoadAsync(ct);
        await using var stream = file.OpenReadStream();
        var path = await storage.SaveAsync(stream, "admins", ext, ct);
        var old = admin.PhotoPath;
        admin.PhotoPath = path;
        audit.Record("profile.photo_changed", "AdminUser", admin.Id);
        await db.SaveChangesAsync(ct);
        if (old is not null) await storage.DeleteAsync(old, ct);
        return admin.ToDto(media);
    }

    private async Task<Domain.Entities.AdminUser> LoadAsync(CancellationToken ct)
    {
        var id = actor.AdminId ?? throw new UnauthorizedException();
        return await db.AdminUsers.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new UnauthorizedException();
    }
}
