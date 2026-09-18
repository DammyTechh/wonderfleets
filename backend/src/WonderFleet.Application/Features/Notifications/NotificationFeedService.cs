using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Models;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Notifications;

public sealed record NotificationQuery : PageQuery
{
    public NotificationCategory? Category { get; init; }
    public bool UnreadOnly { get; init; }
}

public sealed record NotificationDto(
    Guid Id, string Category, string Title, string Message, string? PartnerName, string? PartnerInitials, string? PartnerPhotoUrl,
    bool RequiresReview, bool IsRead, DateTimeOffset CreatedAt, string? RelatedEntityType, Guid? RelatedEntityId, string Group);

public sealed record UnreadCountDto(int Unread);

public interface INotificationFeedService
{
    Task<PagedResult<NotificationDto>> ListAsync(NotificationQuery query, CancellationToken ct);
    Task<UnreadCountDto> UnreadAsync(CancellationToken ct);
    Task MarkReadAsync(Guid id, CancellationToken ct);
    Task<UnreadCountDto> MarkAllReadAsync(CancellationToken ct);
    Task DismissAsync(Guid id, CancellationToken ct);
}

internal sealed class NotificationFeedService(IApplicationDbContext db, IClock clock, IMediaUrlSigner media) : INotificationFeedService
{
    private static readonly TimeSpan Wat = TimeSpan.FromHours(1);

    public async Task<PagedResult<NotificationDto>> ListAsync(NotificationQuery query, CancellationToken ct)
    {
        var q = db.Notifications.AsNoTracking().Where(n => n.DismissedAt == null);
        if (query.Category is { } c) q = q.Where(n => n.Category == c);
        if (query.UnreadOnly) q = q.Where(n => !n.IsRead);
        if (query.NormalizedSearch is { } term) q = q.Where(n => n.Title.ToLower().Contains(term) || n.Message.ToLower().Contains(term));

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(n => n.CreatedAt).Skip(query.Skip).Take(query.PageSize)
            .Select(n => new
            {
                n, Partner = n.LogisticsPartner != null ? n.LogisticsPartner.CompanyName : null,
                Photo = n.LogisticsPartner != null ? n.LogisticsPartner.PhotoPath : null,
            })
            .ToListAsync(ct);

        var today = clock.UtcNow.ToOffset(Wat).Date;
        return new PagedResult<NotificationDto>(rows.Select(r => new NotificationDto(
            r.n.Id, r.n.Category.ToString(), r.n.Title, r.n.Message, r.Partner, r.Partner is null ? null : Text.Initials(r.Partner),
            media.Sign(r.Photo), r.n.RequiresReview, r.n.IsRead, r.n.CreatedAt, r.n.RelatedEntityType, r.n.RelatedEntityId,
            Group(r.n.CreatedAt.ToOffset(Wat).Date, today))).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<UnreadCountDto> UnreadAsync(CancellationToken ct) =>
        new(await db.Notifications.CountAsync(n => !n.IsRead && n.DismissedAt == null, ct));

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Notification", id);
        if (n.IsRead) return;
        n.IsRead = true;
        n.ReadAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<UnreadCountDto> MarkAllReadAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        // Bounded batches keep the transaction small even with a large backlog.
        for (var i = 0; i < 20; i++)
        {
            var batch = await db.Notifications.Where(n => !n.IsRead).OrderBy(n => n.CreatedAt).Take(500).ToListAsync(ct);
            if (batch.Count == 0) break;
            foreach (var n in batch) { n.IsRead = true; n.ReadAt = now; }
            await db.SaveChangesAsync(ct);
        }
        return await UnreadAsync(ct);
    }

    public async Task DismissAsync(Guid id, CancellationToken ct)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Notification", id);
        var now = clock.UtcNow;
        n.DismissedAt = now;
        if (!n.IsRead) { n.IsRead = true; n.ReadAt = now; }
        await db.SaveChangesAsync(ct);
    }

    private static string Group(DateTime day, DateTime today) =>
        day == today ? "Today"
        : day == today.AddDays(-1) ? "Yesterday"
        : day > today.AddDays(-7) ? "Earlier this week"
        : "Older";
}
