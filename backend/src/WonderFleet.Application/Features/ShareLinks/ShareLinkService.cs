using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Options;
using WonderFleet.Application.Features.Notifications;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.ShareLinks;

public sealed record CreateShareLinkRequest(
    ShareAudience Audience,
    Guid PartnerId,
    IReadOnlyList<Guid>? TripIds,
    DateTimeOffset? ExpiresAt,
    bool SendEmail = true,
    bool SendSms = false,
    string? RecipientEmail = null,
    string? RecipientPhone = null);

public sealed record ShareLinkCreatedDto(
    Guid Id, string Url, string Audience, DateTimeOffset ExpiresAt, IReadOnlyList<string> TripCodes, bool EmailQueued, bool SmsQueued);

public sealed record ShareLinkDto(
    Guid Id, string Audience, Guid OwnerId, string OwnerName, string TokenHint, string State,
    DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt, DateTimeOffset? LastAccessedAt, int AccessCount,
    IReadOnlyList<string> TripCodes, string? RevokedReason);

public sealed record ShareLinkQuery(Guid? PartnerId, ShareAudience? Audience, bool ActiveOnly = false);

public sealed record RevokeShareLinkRequest(string? Reason);

public sealed class CreateShareLinkRequestValidator : AbstractValidator<CreateShareLinkRequest>
{
    public CreateShareLinkRequestValidator()
    {
        RuleFor(x => x.Audience).IsInEnum();
        RuleFor(x => x.PartnerId).NotEmpty();
        RuleFor(x => x.TripIds).Must(t => t is null || t.Count <= 50).WithMessage("A link can cover at most 50 trips.");
        RuleFor(x => x.RecipientEmail!).EmailAddressStrict().When(x => !string.IsNullOrWhiteSpace(x.RecipientEmail));
        RuleFor(x => x.RecipientPhone!).PhoneNumber().When(x => !string.IsNullOrWhiteSpace(x.RecipientPhone));
    }
}

public interface IShareLinkService
{
    Task<ShareLinkCreatedDto> CreateAsync(CreateShareLinkRequest request, CancellationToken ct);
    Task<IReadOnlyList<ShareLinkDto>> ListAsync(ShareLinkQuery query, CancellationToken ct);
    Task RevokeAsync(Guid id, string? reason, CancellationToken ct);
    /// Revokes links whose every scoped trip has ended. Called on trip completion and by the monitor.
    Task<int> ExpireLinksForEndedTripsAsync(CancellationToken ct);
    /// Hot path for portal JWT validation (cached briefly).
    Task<bool> IsActiveAsync(Guid shareLinkId, CancellationToken ct);
}

internal sealed class ShareLinkService(
    IApplicationDbContext db,
    ISecureTokenGenerator tokens,
    INotificationComposer notify,
    ICurrentActor actor,
    IClock clock,
    IAuditLogger audit,
    IMemoryCache cache,
    IOptions<ShareLinkOptions> options,
    IOptions<AppOptions> app) : IShareLinkService
{
    private static readonly TripStatus[] Ended = [TripStatus.Completed, TripStatus.Cancelled];

    public async Task<ShareLinkCreatedDto> CreateAsync(CreateShareLinkRequest request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var o = options.Value;

        string ownerName, ownerEmail, ownerPhone, contactName;
        IQueryable<Trip> tripQuery = db.Trips.Where(t => !Ended.Contains(t.Status));
        if (request.Audience == ShareAudience.LogisticsPartner)
        {
            var p = await db.LogisticsPartners.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.PartnerId, ct)
                ?? throw new NotFoundException("Logistics partner", request.PartnerId);
            EnsureActive(p.Status, p.CompanyName);
            (ownerName, ownerEmail, ownerPhone, contactName) = (p.CompanyName, p.Email, p.PhoneNumber, p.ContactPerson);
            tripQuery = tripQuery.Where(t => t.LogisticsPartnerId == p.Id);
        }
        else
        {
            var p = await db.AgroProcessors.AsNoTracking().Include(x => x.Contacts).FirstOrDefaultAsync(x => x.Id == request.PartnerId, ct)
                ?? throw new NotFoundException("Agro-processor", request.PartnerId);
            EnsureActive(p.Status, p.Name);
            var c = p.PrimaryContact;
            (ownerName, ownerEmail, ownerPhone, contactName) = (p.Name, c?.Email ?? "", c?.PhoneNumber ?? "", c?.FullName ?? p.Name);
            tripQuery = tripQuery.Where(t => t.AgroProcessorId == p.Id);
        }

        if (request.TripIds is { Count: > 0 })
        {
            var ids = request.TripIds.Distinct().ToList();
            tripQuery = tripQuery.Where(t => ids.Contains(t.Id));
        }

        var trips = await tripQuery.Include(t => t.Vehicle).AsNoTracking().ToListAsync(ct);
        if (trips.Count == 0)
            throw new BusinessRuleException("share_link.no_active_trips", "There are no active trips to share with this partner.");
        if (request.TripIds is { Count: > 0 } && trips.Count != request.TripIds.Distinct().Count())
            throw new BusinessRuleException("share_link.invalid_trips", "Some trips do not belong to this partner or have already ended.");

        var defaultExpiry = trips.Max(t => t.ExpectedArrival).AddHours(o.GraceHoursAfterArrival);
        var expiresAt = request.ExpiresAt?.ToUniversalTime() ?? defaultExpiry;
        var maxExpiry = now.AddHours(o.MaxLifetimeHours);
        if (expiresAt <= now.AddMinutes(5))
            throw RequestValidationException.For(nameof(request.ExpiresAt), "Expiry must be at least 5 minutes in the future.");
        if (expiresAt > maxExpiry) expiresAt = maxExpiry;

        var raw = tokens.Generate(32);
        var link = new ShareLink
        {
            TokenHash = tokens.Hash(raw),
            TokenHint = raw[..6],
            Audience = request.Audience,
            LogisticsPartnerId = request.Audience == ShareAudience.LogisticsPartner ? request.PartnerId : null,
            AgroProcessorId = request.Audience == ShareAudience.AgroProcessor ? request.PartnerId : null,
            RecipientEmail = Text.Trimmed(request.RecipientEmail) ?? Text.Trimmed(ownerEmail),
            RecipientPhone = Text.Trimmed(request.RecipientPhone) ?? Text.Trimmed(ownerPhone),
            ExpiresAt = expiresAt,
            CreatedByAdminId = actor.AdminId,
            CreatedAt = now,
        };
        foreach (var t in trips) link.Trips.Add(new ShareLinkTrip { ShareLinkId = link.Id, TripId = t.Id });
        db.ShareLinks.Add(link);

        // The raw token exists only in this response and in the outgoing message; the fragment (#) keeps it out of server logs.
        var url = $"{app.Value.FrontendBaseUrl.TrimEnd('/')}/share#{raw}";
        var fleetList = string.Join(", ", trips.Select(t => $"{t.Vehicle?.FleetNumber} ({t.OriginLabel} → {t.DestinationLabel})"));

        var emailQueued = false;
        if (request.SendEmail && link.RecipientEmail is not null)
        {
            notify.Email(link.RecipientEmail,
                request.Audience == ShareAudience.LogisticsPartner ? EmailTemplates.ShareLinkLogistics : EmailTemplates.ShareLinkAgroProcessor,
                new Dictionary<string, string?>
                {
                    ["Name"] = contactName,
                    ["OrganisationName"] = ownerName,
                    ["ShareUrl"] = url,
                    ["ExpiresAt"] = Wat(expiresAt),
                    ["TripCount"] = trips.Count.ToString(),
                    ["FleetList"] = fleetList,
                });
            emailQueued = true;
        }

        var smsQueued = false;
        if (request.SendSms && link.RecipientPhone is not null && Phone.IsValid(link.RecipientPhone))
        {
            notify.Sms(link.RecipientPhone, $"WonderFleet: live tracking for {trips.Count} shipment(s) is ready. Open {url} (expires {Wat(expiresAt)}). No login needed.");
            smsQueued = true;
        }

        audit.Record("share_link.created", "ShareLink", link.Id, new { request.Audience, request.PartnerId, trips = trips.Count, expiresAt });
        await db.SaveChangesAsync(ct);

        return new ShareLinkCreatedDto(link.Id, url, link.Audience.ToString(), expiresAt,
            trips.Select(t => t.TripCode).ToList(), emailQueued, smsQueued);
    }

    public async Task<IReadOnlyList<ShareLinkDto>> ListAsync(ShareLinkQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var q = db.ShareLinks.AsNoTracking();
        if (query.PartnerId is { } pid) q = q.Where(l => l.LogisticsPartnerId == pid || l.AgroProcessorId == pid);
        if (query.Audience is { } aud) q = q.Where(l => l.Audience == aud);
        if (query.ActiveOnly) q = q.Where(l => l.RevokedAt == null && l.ExpiresAt > now);

        var rows = await q.OrderByDescending(l => l.CreatedAt).Take(200)
            .Select(l => new
            {
                l.Id, l.Audience, l.TokenHint, l.ExpiresAt, l.CreatedAt, l.LastAccessedAt, l.AccessCount, l.RevokedAt, l.RevokedReason,
                OwnerId = l.LogisticsPartnerId ?? l.AgroProcessorId ?? Guid.Empty,
                OwnerName = l.LogisticsPartner != null ? l.LogisticsPartner.CompanyName : l.AgroProcessor != null ? l.AgroProcessor.Name : "",
                TripCodes = l.Trips.Select(t => t.Trip!.TripCode).ToList(),
            })
            .ToListAsync(ct);

        return rows.Select(l => new ShareLinkDto(l.Id, l.Audience.ToString(), l.OwnerId, l.OwnerName, l.TokenHint + "…",
            l.RevokedAt is not null ? "Revoked" : l.ExpiresAt <= now ? "Expired" : "Active",
            l.ExpiresAt, l.CreatedAt, l.LastAccessedAt, l.AccessCount, l.TripCodes, l.RevokedReason)).ToList();
    }

    public async Task RevokeAsync(Guid id, string? reason, CancellationToken ct)
    {
        var link = await db.ShareLinks.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Share link", id);
        link.Revoke(string.IsNullOrWhiteSpace(reason) ? "revoked_by_admin" : reason.Trim()[..Math.Min(reason.Trim().Length, 100)], clock.UtcNow);
        audit.Record("share_link.revoked", "ShareLink", id, new { reason });
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey(id));
    }

    public async Task<int> ExpireLinksForEndedTripsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var candidates = await db.ShareLinks
            .Where(l => l.RevokedAt == null && l.ExpiresAt > now)
            .Where(l => l.Trips.All(st => Ended.Contains(st.Trip!.Status)))
            .ToListAsync(ct);
        foreach (var link in candidates)
        {
            link.Revoke("trips_ended", now);
            cache.Remove(CacheKey(link.Id));
        }
        return candidates.Count;
    }

    public async Task<bool> IsActiveAsync(Guid shareLinkId, CancellationToken ct) =>
        await cache.GetOrCreateAsync(CacheKey(shareLinkId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            var now = clock.UtcNow;
            return await db.ShareLinks.AsNoTracking().AnyAsync(l =>
                l.Id == shareLinkId && l.RevokedAt == null && l.ExpiresAt > now &&
                l.Trips.Any(st => !Ended.Contains(st.Trip!.Status)), ct);
        });

    internal static string CacheKey(Guid id) => $"share-link:active:{id}";

    private static void EnsureActive(PartnerStatus status, string name)
    {
        if (status != PartnerStatus.Active)
            throw new BusinessRuleException("share_link.partner_inactive", $"{name} is {status}. Activate the partner before sharing access.");
    }

    private static string Wat(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(1)).ToString("dd MMM yyyy, HH:mm") + " WAT";
}
