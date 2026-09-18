using WonderFleet.Domain.Common;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Domain.Entities;

/// Fleet-wide default rules. Trip-level thresholds take precedence when a trip is present.
public class AlertRule : AuditableEntity
{
    public AlertType AlertType { get; set; }
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public bool IsEnabled { get; set; } = true;
    public decimal? ThresholdValue { get; set; }
    public int? DurationMinutes { get; set; }
}

public class NotificationChannelSetting : AuditableEntity
{
    public NotificationChannel Channel { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool CriticalOnly { get; set; } = true;
}

public class Alert : AuditableEntity
{
    public AlertType AlertType { get; set; }
    public AlertSeverity Severity { get; set; }
    public AlertStatus Status { get; private set; } = AlertStatus.Active;
    public string Title { get; set; } = default!;
    public string Message { get; set; } = default!;
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }
    public Guid? TripId { get; set; }
    public Trip? Trip { get; set; }
    public Guid? VehicleId { get; set; }
    public decimal? ReadingValue { get; set; }
    public decimal? SecondaryReadingValue { get; set; }
    public decimal? ThresholdValue { get; set; }
    public DateTimeOffset TriggeredAt { get; set; }
    public DateTimeOffset LastTriggeredAt { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public Guid? AcknowledgedBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    public string? ResolutionNote { get; private set; }

    public bool IsOpen => Status != AlertStatus.Resolved;

    public void Escalate(AlertSeverity severity, decimal? value, decimal? secondary, DateTimeOffset now)
    {
        if (severity > Severity) Severity = severity;
        ReadingValue = value;
        SecondaryReadingValue = secondary;
        LastTriggeredAt = now;
        OccurrenceCount++;
    }

    public void Acknowledge(Guid? by, DateTimeOffset now)
    {
        if (Status != AlertStatus.Active) return;
        Status = AlertStatus.Acknowledged;
        AcknowledgedAt = now;
        AcknowledgedBy = by;
    }

    public void Resolve(Guid? by, string? note, DateTimeOffset now)
    {
        if (Status == AlertStatus.Resolved) return;
        Status = AlertStatus.Resolved;
        ResolvedAt = now;
        ResolvedBy = by;
        ResolutionNote = note;
    }
}

/// In-app notification feed item (the bell / Notifications page).
public class Notification : Entity
{
    public NotificationCategory Category { get; set; }
    public string Title { get; set; } = default!;
    public string Message { get; set; } = default!;
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public Guid? LogisticsPartnerId { get; set; }
    public LogisticsPartner? LogisticsPartner { get; set; }
    public bool RequiresReview { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// Transactional outbox row for Email/SMS. A background worker delivers with retries.
public class NotificationDelivery : Entity
{
    public const int MaxAttempts = 6;

    public NotificationChannel Channel { get; set; }
    public string Recipient { get; set; } = default!;
    public string? Subject { get; set; }
    public string Body { get; set; } = default!;
    public string? TextBody { get; set; }
    public string TemplateKey { get; set; } = default!;
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public string? ProviderMessageId { get; set; }
    public Guid? AlertId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }

    public void MarkSent(string? providerId, DateTimeOffset now)
    {
        Status = DeliveryStatus.Sent;
        ProviderMessageId = providerId;
        SentAt = now;
        LastError = null;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        if (Attempts >= MaxAttempts)
        {
            Status = DeliveryStatus.Failed;
            return;
        }
        Status = DeliveryStatus.Pending;
        // Exponential backoff: 30s, 1m, 2m, 4m, 8m.
        NextAttemptAt = now.AddSeconds(30 * Math.Pow(2, Attempts - 1));
    }
}
