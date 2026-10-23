using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Options;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Notifications;

public sealed record DeliveryRowDto(
    Guid Id, string Channel, string Recipient, string? Subject, string TemplateKey,
    string Status, int Attempts, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);

public sealed record DeliveryHealthDto(
    string EmailProvider, string SmsProvider, string From,
    bool EmailConfigured, bool SmsConfigured,
    int Pending, int Sent24h, int Failed24h,
    IReadOnlyList<DeliveryRowDto> Recent);

public sealed record TestEmailResultDto(bool Queued, string Recipient, string Message);

/// <summary>
/// Answers "why did no email arrive?" without a database session or a log search.
///
/// Every delivery already records the provider's own refusal in `last_error` — a bounced
/// Resend call says exactly what was wrong, usually that the sending domain is not verified
/// or the key is wrong — but until now nothing exposed it, so a misconfiguration looked
/// identical to an application that simply never tried.
/// </summary>
public interface IDeliveryDiagnostics
{
    Task<DeliveryHealthDto> GetAsync(CancellationToken ct);
    Task<TestEmailResultDto> SendTestEmailAsync(CancellationToken ct);
}

internal sealed class DeliveryDiagnostics(
    IApplicationDbContext db,
    INotificationComposer notify,
    ICurrentActor actor,
    IClock clock,
    IEmailProbe probe,
    IOptions<AppOptions> app) : IDeliveryDiagnostics
{
    public async Task<DeliveryHealthDto> GetAsync(CancellationToken ct)
    {
        var since = clock.UtcNow.AddHours(-24);
        var recent = await db.NotificationDeliveries.AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Take(25)
            .Select(d => new DeliveryRowDto(
                d.Id, d.Channel.ToString(), d.Recipient, d.Subject, d.TemplateKey,
                d.Status.ToString(), d.Attempts, d.LastError, d.CreatedAt, d.SentAt))
            .ToListAsync(ct);

        var pending = await db.NotificationDeliveries.CountAsync(d => d.Status == DeliveryStatus.Pending, ct);
        var sent = await db.NotificationDeliveries.CountAsync(d => d.Status == DeliveryStatus.Sent && d.SentAt >= since, ct);
        var failed = await db.NotificationDeliveries.CountAsync(d => d.Status == DeliveryStatus.Failed && d.CreatedAt >= since, ct);

        return new DeliveryHealthDto(
            probe.EmailProvider, probe.SmsProvider, probe.From,
            probe.EmailConfigured, probe.SmsConfigured,
            pending, sent, failed, recent);
    }

    /// Queues a message to the signed-in administrator. The dispatcher sends it within its
    /// polling interval, and whatever the provider answers lands in the list above.
    public async Task<TestEmailResultDto> SendTestEmailAsync(CancellationToken ct)
    {
        var id = actor.AdminId;
        var admin = id is null ? null : await db.AdminUsers.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (admin is null) return new TestEmailResultDto(false, "", "No signed-in administrator to send to.");

        notify.Email(admin.Email, EmailTemplates.PasswordChanged, new Dictionary<string, string?>
        {
            ["Name"] = admin.Name,
            ["Subject"] = "WonderFleet test email",
            ["Heading"] = "Email delivery is working",
            ["Message"] = "This is a test from WonderFleet Settings. If it reached you, alerts will too.",
            ["ActionUrl"] = app.Value.FrontendBaseUrl,
            ["When"] = clock.UtcNow.ToString("u"),
        });
        await db.SaveChangesAsync(ct);

        return new TestEmailResultDto(true, admin.Email,
            $"Queued for {admin.Email}. Refresh in a few seconds — if it fails, the reason appears in the list below.");
    }
}

/// Reports how messaging is configured without the Application layer taking a dependency on
/// the provider options, which live in Infrastructure.
public interface IEmailProbe
{
    string EmailProvider { get; }
    string SmsProvider { get; }
    string From { get; }
    bool EmailConfigured { get; }
    bool SmsConfigured { get; }
}
