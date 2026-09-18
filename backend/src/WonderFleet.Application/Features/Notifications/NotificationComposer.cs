using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Options;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Notifications;

public static class EmailTemplates
{
    public const string CriticalAlert = "critical-alert";
    public const string AlertResolved = "alert-resolved";
    public const string ShareLinkLogistics = "share-link-logistics";
    public const string ShareLinkAgroProcessor = "share-link-agro-processor";
    public const string TripDispatched = "trip-dispatched";
    public const string TripDelivered = "trip-delivered";
    public const string DeliveryDelayed = "delivery-delayed";
    public const string PasswordChanged = "password-changed";
    public const string PartnerOnboarded = "partner-onboarded";
    public const string PartnerStatusChanged = "partner-status-changed";

    public static readonly IReadOnlyList<string> All =
    [
        CriticalAlert, AlertResolved, ShareLinkLogistics, ShareLinkAgroProcessor, TripDispatched,
        TripDelivered, DeliveryDelayed, PasswordChanged, PartnerOnboarded, PartnerStatusChanged,
    ];
}

/// Writes notifications into the current unit of work: in-app rows plus Email/SMS outbox rows.
/// Nothing is sent inline — the dispatcher worker delivers after the transaction commits.
public interface INotificationComposer
{
    void Email(string to, string templateKey, IDictionary<string, string?> model, Guid? alertId = null);
    void Sms(string phone, string message, Guid? alertId = null);
    Notification InApp(NotificationCategory category, string title, string message,
        string? entityType = null, Guid? entityId = null, Guid? logisticsPartnerId = null, bool requiresReview = false);
}

internal sealed class NotificationComposer(
    IApplicationDbContext db,
    IEmailTemplateRenderer renderer,
    IClock clock,
    IOptions<AppOptions> app) : INotificationComposer
{
    public void Email(string to, string templateKey, IDictionary<string, string?> model, Guid? alertId = null)
    {
        if (string.IsNullOrWhiteSpace(to)) return;
        var o = app.Value;
        var full = new Dictionary<string, string?>(model, StringComparer.OrdinalIgnoreCase)
        {
            ["AppName"] = "WonderFleet",
            ["Company"] = "OfeminiAgricTech",
            ["LogoUrl"] = o.LogoUrl,
            ["FrontendUrl"] = o.FrontendBaseUrl,
            ["SupportEmail"] = o.SupportEmail,
            ["Year"] = clock.UtcNow.Year.ToString(),
        };
        foreach (var kv in model) full[kv.Key] = kv.Value;

        var rendered = renderer.Render(templateKey, full);
        db.NotificationDeliveries.Add(new NotificationDelivery
        {
            Channel = NotificationChannel.Email,
            Recipient = to.Trim(),
            Subject = rendered.Subject,
            Body = rendered.Html,
            TextBody = rendered.Text,
            TemplateKey = templateKey,
            AlertId = alertId,
            CreatedAt = clock.UtcNow,
            NextAttemptAt = clock.UtcNow,
        });
    }

    public void Sms(string phone, string message, Guid? alertId = null)
    {
        if (!Phone.IsValid(phone)) return;
        var body = message.Length > 320 ? message[..317] + "..." : message;
        db.NotificationDeliveries.Add(new NotificationDelivery
        {
            Channel = NotificationChannel.Sms,
            Recipient = Phone.ToE164(phone),
            Body = body,
            TemplateKey = "sms",
            AlertId = alertId,
            CreatedAt = clock.UtcNow,
            NextAttemptAt = clock.UtcNow,
        });
    }

    public Notification InApp(NotificationCategory category, string title, string message,
        string? entityType = null, Guid? entityId = null, Guid? logisticsPartnerId = null, bool requiresReview = false)
    {
        var n = new Notification
        {
            Category = category,
            Title = title.Length > 150 ? title[..150] : title,
            Message = message.Length > 500 ? message[..500] : message,
            RelatedEntityType = entityType,
            RelatedEntityId = entityId,
            LogisticsPartnerId = logisticsPartnerId,
            RequiresReview = requiresReview,
            CreatedAt = clock.UtcNow,
        };
        db.Notifications.Add(n);
        return n;
    }
}
