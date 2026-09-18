using System.Text.Json;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Domain.Entities;
using WonderFleet.Infrastructure.Persistence;

namespace WonderFleet.Infrastructure.Services;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// Writes audit rows into the caller's unit of work. Details are serialised as JSON and truncated.
internal sealed class AuditLogger(ApplicationDbContext db, ICurrentActor actor, IClock clock) : IAuditLogger
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(string action, string? entityType = null, object? entityId = null, object? details = null)
    {
        string? json = null;
        if (details is not null)
        {
            json = JsonSerializer.Serialize(details, Json);
            if (json.Length > 4000) json = JsonSerializer.Serialize(new { truncated = true, preview = json[..3900] }, Json);
        }

        db.AuditLogs.Add(new AuditLog
        {
            ActorType = actor.ActorType,
            ActorId = (actor.AdminId ?? actor.ShareLinkId)?.ToString(),
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            IpAddress = actor.IpAddress,
            UserAgent = actor.UserAgent is { Length: > 300 } ua ? ua[..300] : actor.UserAgent,
            Details = json,
            CreatedAt = clock.UtcNow,
        });
    }
}

/// Actor for background work (no HTTP context).
public sealed class SystemActor : ICurrentActor
{
    public Domain.Enums.ActorType ActorType => Domain.Enums.ActorType.System;
    public Guid? AdminId => null;
    public Guid? ShareLinkId => null;
    public string? IpAddress => null;
    public string? UserAgent => null;
}
