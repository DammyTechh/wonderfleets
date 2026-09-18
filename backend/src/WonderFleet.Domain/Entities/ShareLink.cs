using WonderFleet.Domain.Common;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Domain.Entities;

/// Login-free, time-limited access for a logistics partner or agro-processor.
/// Only a SHA-256 hash of the token is stored. The link dies at ExpiresAt, on revoke,
/// or automatically once every trip in its scope has ended.
public class ShareLink : Entity
{
    public string TokenHash { get; set; } = default!;
    public string TokenHint { get; set; } = default!;
    public ShareAudience Audience { get; set; }
    public Guid? LogisticsPartnerId { get; set; }
    public LogisticsPartner? LogisticsPartner { get; set; }
    public Guid? AgroProcessorId { get; set; }
    public AgroProcessor? AgroProcessor { get; set; }
    public string? RecipientEmail { get; set; }
    public string? RecipientPhone { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public DateTimeOffset? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
    public Guid? CreatedByAdminId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<ShareLinkTrip> Trips { get; } = [];

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(string reason, DateTimeOffset now)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        RevokedReason = reason;
    }

    public void RegisterAccess(DateTimeOffset now)
    {
        LastAccessedAt = now;
        AccessCount++;
    }
}

public class ShareLinkTrip
{
    public Guid ShareLinkId { get; set; }
    public ShareLink? ShareLink { get; set; }
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
}
