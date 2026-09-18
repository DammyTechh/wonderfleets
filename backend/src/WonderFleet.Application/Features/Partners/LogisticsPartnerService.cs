using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Models;
using WonderFleet.Application.Common.Options;
using WonderFleet.Application.Features.Notifications;
using WonderFleet.Application.Features.Telemetry;
using WonderFleet.Application.Features.Tracking;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Partners;

public interface ILogisticsPartnerService
{
    Task<PagedResult<LogisticsPartnerListItemDto>> ListAsync(PartnerListQuery query, CancellationToken ct);
    Task<LogisticsPartnerProfileDto> GetAsync(Guid id, CancellationToken ct);
    Task<IdResponse> CreateAsync(LogisticsPartnerRequest request, CancellationToken ct);
    Task<LogisticsPartnerProfileDto> UpdateAsync(Guid id, LogisticsPartnerRequest request, CancellationToken ct);
    Task ChangeStatusAsync(Guid id, ChangePartnerStatusRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<string?> UploadPhotoAsync(Guid id, UploadedFile file, CancellationToken ct);
    Task<PartnerDocumentDto> UploadDocumentAsync(Guid id, UploadPartnerDocumentRequest request, UploadedFile file, CancellationToken ct);
    Task<PartnerDocumentDto> VerifyDocumentAsync(Guid id, Guid documentId, VerifyDocumentRequest request, CancellationToken ct);
    Task DeleteDocumentAsync(Guid id, Guid documentId, CancellationToken ct);
    Task<FileDownload> DownloadDocumentAsync(Guid id, Guid documentId, CancellationToken ct);
    Task<ReportFile> ExportAsync(PartnerListQuery query, CancellationToken ct);
}

internal sealed class LogisticsPartnerService(
    IApplicationDbContext db,
    ICodeGenerator codes,
    IClock clock,
    IAuditLogger audit,
    IFileStorage storage,
    IMediaUrlSigner media,
    IMapsService maps,
    INotificationComposer notify,
    IReportRenderer reports,
    TrackingQueries tracking,
    PartnerAccessRevoker revoker,
    IOptions<AppOptions> app) : ILogisticsPartnerService
{
    public async Task<PagedResult<LogisticsPartnerListItemDto>> ListAsync(PartnerListQuery query, CancellationToken ct)
    {
        var q = Filter(query);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(p => p.CreatedAt).Skip(query.Skip).Take(query.PageSize)
            .Select(p => new
            {
                p.Id, p.PartnerCode, p.CompanyName, p.PhotoPath, p.Email, p.PhoneNumber, p.FleetSize,
                p.City, p.State, p.OfficeAddress, p.Status,
                Registered = p.Vehicles.Count(v => v.DeletedAt == null),
            })
            .ToListAsync(ct);

        return new PagedResult<LogisticsPartnerListItemDto>(rows.Select(p => new LogisticsPartnerListItemDto(
            p.Id, p.PartnerCode, p.CompanyName, Text.Initials(p.CompanyName), media.Sign(p.PhotoPath), p.Email, p.PhoneNumber,
            Math.Max(p.FleetSize, p.Registered), p.Registered, Location(p.City, p.State, p.OfficeAddress), p.Status.ToString())).ToList(),
            query.Page, query.PageSize, total);
    }

    public async Task<LogisticsPartnerProfileDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.LogisticsPartners.AsNoTracking()
            .Include(x => x.TruckTypes).Include(x => x.Corridors).Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
            ?? throw new NotFoundException("Logistics partner", id);

        var now = clock.UtcNow;
        var weekAgo = now.AddDays(-7);
        var trips = db.Trips.AsNoTracking().Where(t => t.LogisticsPartnerId == id);
        var registered = await db.Vehicles.CountAsync(v => v.LogisticsPartnerId == id && v.DeletedAt == null, ct);
        var completed = await trips.CountAsync(t => t.Status == TripStatus.Completed, ct);
        var active = await trips.CountAsync(t => TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct);
        var activeThisWeek = await trips.CountAsync(t => TelemetryIngestionService.OpenTripStatuses.Contains(t.Status) && t.CreatedAt >= weekAgo, ct);

        var recent = await trips.OrderByDescending(t => t.CreatedAt).Take(10)
            .Select(t => new { t.Id, t.TripCode, t.Vehicle!.FleetNumber, t.OriginLabel, t.DestinationLabel, t.Status, t.SensorStatus, t.CompletedAt, t.StartedAt, t.LoadingTime })
            .ToListAsync(ct);

        var driverPool = await tracking.DriverPoolAsync(id, 20, ct);
        var location = Location(p.City, p.State, p.OfficeAddress);

        return new LogisticsPartnerProfileDto(
            p.Id, p.PartnerCode, p.CompanyName, Text.Initials(p.CompanyName), media.Sign(p.PhotoPath), p.Status.ToString(), p.OnboardedAt,
            new PartnerContactCardDto(p.ContactPerson, location, p.PhoneNumber, p.Email),
            new PartnerStatsDto(Math.Max(p.FleetSize, registered), completed, active, activeThisWeek),
            new CompanyInfoDto(p.CompanyName, p.CacNumber, p.ContactPerson, p.PhoneNumber, p.Email, p.OfficeAddress,
                p.YearsOfOperation, Math.Max(p.DriverPoolSize, driverPool.OnTrip + driverPool.Available + driverPool.OffDuty),
                p.AvailabilityStatus.ToString(), p.InsuranceCoverageType?.ToString(), p.InsuranceExpiryDate),
            p.Corridors.OrderBy(c => c.Name).Select(c => c.Name).ToList(),
            p.Documents.OrderByDescending(d => d.UploadedAt).Select(d => ToDto(p.Id, d)).ToList(),
            driverPool,
            p.TruckTypes.OrderBy(t => t.MaxTonnage).Select(t => new TruckTypeDto(t.Id, t.TruckType, t.MaxTonnage, t.Quantity)).ToList(),
            recent.Select(t => new RecentShipmentDto(t.Id, t.TripCode, t.FleetNumber, Text.Route(t.OriginLabel, t.DestinationLabel),
                ShipmentLabel(t.Status, t.SensorStatus), t.CompletedAt ?? t.StartedAt ?? t.LoadingTime)).ToList(),
            p.City, p.State);
    }

    public async Task<IdResponse> CreateAsync(LogisticsPartnerRequest request, CancellationToken ct)
    {
        await EnsureUniqueAsync(request, null, ct);
        var partner = new LogisticsPartner
        {
            PartnerCode = await codes.NextPartnerCodeAsync(ct),
            Status = PartnerStatus.Pending,
        };
        await ApplyAsync(partner, request, ct);
        db.LogisticsPartners.Add(partner);

        notify.InApp(NotificationCategory.Partner, "New logistics partner added",
            $"{partner.CompanyName} ({partner.PartnerCode}) was onboarded and is pending activation.",
            nameof(LogisticsPartner), partner.Id, partner.Id, requiresReview: true);
        audit.Record("logistics_partner.created", nameof(LogisticsPartner), partner.Id, new { partner.PartnerCode });
        await db.SaveChangesAsync(ct);
        return new IdResponse(partner.Id, partner.PartnerCode);
    }

    public async Task<LogisticsPartnerProfileDto> UpdateAsync(Guid id, LogisticsPartnerRequest request, CancellationToken ct)
    {
        var partner = await db.LogisticsPartners.Include(p => p.TruckTypes).Include(p => p.Corridors)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct)
            ?? throw new NotFoundException("Logistics partner", id);
        await EnsureUniqueAsync(request, id, ct);

        db.PartnerTruckTypes.RemoveRange(partner.TruckTypes);
        db.PartnerCorridors.RemoveRange(partner.Corridors);
        partner.TruckTypes.Clear();
        partner.Corridors.Clear();
        await ApplyAsync(partner, request, ct);

        audit.Record("logistics_partner.updated", nameof(LogisticsPartner), id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task ChangeStatusAsync(Guid id, ChangePartnerStatusRequest request, CancellationToken ct)
    {
        var partner = await db.LogisticsPartners.FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct)
            ?? throw new NotFoundException("Logistics partner", id);
        if (partner.Status == request.Status) return;

        var now = clock.UtcNow;
        var previous = partner.Status;
        partner.Status = request.Status;

        if (request.Status == PartnerStatus.Active && partner.OnboardedAt is null)
        {
            partner.OnboardedAt = now;
            notify.Email(partner.Email, EmailTemplates.PartnerOnboarded, new Dictionary<string, string?>
            {
                ["Name"] = partner.ContactPerson,
                ["OrganisationName"] = partner.CompanyName,
                ["PartnerCode"] = partner.PartnerCode,
                ["PartnerType"] = "logistics partner",
            });
        }
        else
        {
            notify.Email(partner.Email, EmailTemplates.PartnerStatusChanged, new Dictionary<string, string?>
            {
                ["Name"] = partner.ContactPerson,
                ["OrganisationName"] = partner.CompanyName,
                ["Status"] = request.Status.ToString(),
                ["Reason"] = Text.Trimmed(request.Reason) ?? "No reason was provided.",
            });
        }

        if (request.Status == PartnerStatus.Suspended)
            await revoker.RevokeForLogisticsPartnerAsync(id, "partner_suspended", ct);

        notify.InApp(NotificationCategory.Partner, $"Partner {request.Status.ToString().ToLowerInvariant()}",
            $"{partner.CompanyName} moved from {previous} to {request.Status}.", nameof(LogisticsPartner), id, id);
        audit.Record("logistics_partner.status_changed", nameof(LogisticsPartner), id, new { from = previous, to = request.Status, request.Reason });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var partner = await db.LogisticsPartners.FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct)
            ?? throw new NotFoundException("Logistics partner", id);
        if (await db.Trips.AnyAsync(t => t.LogisticsPartnerId == id && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct))
            throw new BusinessRuleException("partner.has_open_trips", "This partner has shipments in progress. Complete or cancel them first.");

        var now = clock.UtcNow;
        partner.DeletedAt = now;
        partner.Status = PartnerStatus.Suspended;
        // Free unique keys so the company can be re-onboarded later without clashing with history.
        foreach (var v in await db.Vehicles.Where(v => v.LogisticsPartnerId == id && v.DeletedAt == null).ToListAsync(ct))
            v.DeletedAt = now;
        foreach (var d in await db.Drivers.Where(d => d.LogisticsPartnerId == id && d.DeletedAt == null).ToListAsync(ct))
            d.DeletedAt = now;
        foreach (var dev in await db.Devices.Where(d => d.Vehicle != null && d.Vehicle.LogisticsPartnerId == id).ToListAsync(ct))
            dev.VehicleId = null;

        await revoker.RevokeForLogisticsPartnerAsync(id, "partner_removed", ct);
        audit.Record("logistics_partner.deleted", nameof(LogisticsPartner), id);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> UploadPhotoAsync(Guid id, UploadedFile file, CancellationToken ct)
    {
        var (ext, _) = FileRules.EnsureImage(file);
        var partner = await db.LogisticsPartners.FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct)
            ?? throw new NotFoundException("Logistics partner", id);
        await using var stream = file.OpenReadStream();
        var path = await storage.SaveAsync(stream, "partners", ext, ct);
        var old = partner.PhotoPath;
        partner.PhotoPath = path;
        audit.Record("logistics_partner.photo_changed", nameof(LogisticsPartner), id);
        await db.SaveChangesAsync(ct);
        if (old is not null) await storage.DeleteAsync(old, ct);
        return media.Sign(path);
    }

    public async Task<PartnerDocumentDto> UploadDocumentAsync(Guid id, UploadPartnerDocumentRequest request, UploadedFile file, CancellationToken ct)
    {
        var (ext, contentType) = FileRules.EnsureDocument(file);
        if (!await db.LogisticsPartners.AnyAsync(p => p.Id == id && p.DeletedAt == null, ct))
            throw new NotFoundException("Logistics partner", id);
        if (await db.PartnerDocuments.CountAsync(d => d.LogisticsPartnerId == id, ct) >= 20)
            throw new BusinessRuleException("partner.too_many_documents", "A partner can have at most 20 documents.");

        await using var stream = file.OpenReadStream();
        var path = await storage.SaveAsync(stream, "partner-documents", ext, ct);
        var name = FileRules.SafeFileName(file.FileName);
        if (!name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name) + ext;

        var doc = new PartnerDocument
        {
            LogisticsPartnerId = id,
            DocumentType = request.DocumentType,
            FileName = name,
            StoragePath = path,
            ContentType = contentType,
            SizeBytes = file.Length,
            ExpiresOn = request.ExpiresOn,
            UploadedAt = clock.UtcNow,
        };
        db.PartnerDocuments.Add(doc);
        audit.Record("logistics_partner.document_uploaded", nameof(PartnerDocument), doc.Id, new { id, request.DocumentType });
        await db.SaveChangesAsync(ct);
        return ToDto(id, doc);
    }

    public async Task<PartnerDocumentDto> VerifyDocumentAsync(Guid id, Guid documentId, VerifyDocumentRequest request, CancellationToken ct)
    {
        var doc = await db.PartnerDocuments.FirstOrDefaultAsync(d => d.Id == documentId && d.LogisticsPartnerId == id, ct)
            ?? throw new NotFoundException("Document", documentId);
        doc.IsVerified = request.IsVerified;
        if (request.ExpiresOn.HasValue) doc.ExpiresOn = request.ExpiresOn;
        audit.Record("logistics_partner.document_verified", nameof(PartnerDocument), documentId, new { request.IsVerified });
        await db.SaveChangesAsync(ct);
        return ToDto(id, doc);
    }

    public async Task DeleteDocumentAsync(Guid id, Guid documentId, CancellationToken ct)
    {
        var doc = await db.PartnerDocuments.FirstOrDefaultAsync(d => d.Id == documentId && d.LogisticsPartnerId == id, ct)
            ?? throw new NotFoundException("Document", documentId);
        db.PartnerDocuments.Remove(doc);
        audit.Record("logistics_partner.document_deleted", nameof(PartnerDocument), documentId);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(doc.StoragePath, ct);
    }

    public async Task<FileDownload> DownloadDocumentAsync(Guid id, Guid documentId, CancellationToken ct)
    {
        var doc = await db.PartnerDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId && d.LogisticsPartnerId == id, ct)
            ?? throw new NotFoundException("Document", documentId);
        var stream = await storage.OpenReadAsync(doc.StoragePath, ct) ?? throw new NotFoundException("Document file", documentId);
        audit.Record("logistics_partner.document_downloaded", nameof(PartnerDocument), documentId);
        await db.SaveChangesAsync(ct);
        return new FileDownload(stream, doc.ContentType, doc.FileName);
    }

    public async Task<ReportFile> ExportAsync(PartnerListQuery query, CancellationToken ct)
    {
        var rows = await Filter(query).OrderBy(p => p.PartnerCode).Take(5000)
            .Select(p => new
            {
                p.PartnerCode, p.CompanyName, p.ContactPerson, p.Email, p.PhoneNumber, p.CacNumber, p.FleetSize,
                p.City, p.State, p.OfficeAddress, p.Status, p.AvailabilityStatus, p.InsuranceExpiryDate, p.OnboardedAt,
            })
            .ToListAsync(ct);
        var table = new ReportTable("Logistics partners", $"Exported {AlertsFormat.Wat(clock.UtcNow)}",
            ["Code", "Company", "Contact", "Email", "Phone", "CAC", "Fleet size", "Location", "Status", "Availability", "Insurance expiry", "Onboarded"],
            rows.Select(p => (IReadOnlyList<string>)new List<string> {
                p.PartnerCode, p.CompanyName, p.ContactPerson, p.Email, p.PhoneNumber, p.CacNumber, p.FleetSize.ToString(),
                Location(p.City, p.State, p.OfficeAddress), p.Status.ToString(), p.AvailabilityStatus.ToString(),
                p.InsuranceExpiryDate?.ToString("yyyy-MM-dd") ?? "", p.OnboardedAt?.ToString("yyyy-MM-dd") ?? "",
            }).ToList(),
            []);
        audit.Record("logistics_partner.exported", nameof(LogisticsPartner), null, new { rows = rows.Count });
        await db.SaveChangesAsync(ct);
        return reports.RenderCsv(table, $"logistics-partners-{clock.UtcNow:yyyyMMdd}.csv");
    }

    // ------------------------------------------------------------------ helpers

    private IQueryable<LogisticsPartner> Filter(PartnerListQuery query)
    {
        var q = db.LogisticsPartners.AsNoTracking().Where(p => p.DeletedAt == null);
        if (query.Status is { } status) q = q.Where(p => p.Status == status);
        if (query.NormalizedSearch is { } term)
            q = q.Where(p => p.CompanyName.ToLower().Contains(term) || p.PartnerCode.ToLower().Contains(term)
                             || p.Email.ToLower().Contains(term) || p.ContactPerson.ToLower().Contains(term));
        return q;
    }

    private async Task EnsureUniqueAsync(LogisticsPartnerRequest r, Guid? exceptId, CancellationToken ct)
    {
        var cac = r.CacNumber.Trim().ToLowerInvariant();
        var email = r.Email.Trim().ToLowerInvariant();
        var others = db.LogisticsPartners.Where(p => p.DeletedAt == null && p.Id != exceptId);
        if (await others.AnyAsync(p => p.CacNumber.ToLower() == cac, ct))
            throw new ConflictException("A partner with this CAC number already exists.", "partner.duplicate_cac");
        if (await others.AnyAsync(p => p.Email.ToLower() == email, ct))
            throw new ConflictException("A partner with this email already exists.", "partner.duplicate_email");
    }

    private async Task ApplyAsync(LogisticsPartner p, LogisticsPartnerRequest r, CancellationToken ct)
    {
        p.CompanyName = r.CompanyName.Trim();
        p.ContactPerson = r.ContactPerson.Trim();
        p.CacNumber = r.CacNumber.Trim().ToUpperInvariant();
        p.PhoneNumber = Phone.ToE164(r.PhoneNumber);
        p.Email = r.Email.Trim();
        p.FleetSize = r.FleetSize;
        p.DriverPoolSize = r.DriverPoolSize;
        p.YearsOfOperation = r.YearsOfOperation;
        p.AvailabilityStatus = r.AvailabilityStatus;
        p.InsuranceCoverageType = r.InsuranceCoverageType;
        p.InsuranceExpiryDate = r.InsuranceExpiryDate;
        var addressChanged = !string.Equals(p.OfficeAddress, Text.Trimmed(r.OfficeAddress), StringComparison.OrdinalIgnoreCase);
        p.OfficeAddress = Text.Trimmed(r.OfficeAddress);
        p.City = Text.Trimmed(r.City);
        p.State = Text.Trimmed(r.State);

        foreach (var t in r.TruckTypes ?? [])
            p.TruckTypes.Add(new PartnerTruckType { LogisticsPartnerId = p.Id, TruckType = t.TruckType.Trim(), MaxTonnage = t.MaxTonnage, Quantity = t.Quantity });
        foreach (var c in (r.Corridors ?? []).Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            p.Corridors.Add(new PartnerCorridor { LogisticsPartnerId = p.Id, Name = c });

        // Fill "Ikeja, Lagos State" from the office address when the admin did not type it (best effort).
        if (p.OfficeAddress is not null && addressChanged && (p.City is null || p.State is null))
        {
            var geo = await PartnerGeo.TryGeocodeAsync(maps, p.OfficeAddress, ct);
            p.City ??= geo?.City;
            p.State ??= geo?.State;
        }
    }

    private PartnerDocumentDto ToDto(Guid partnerId, PartnerDocument d) => new(
        d.Id, d.DocumentType.ToString(), d.FileName, d.ContentType, d.SizeBytes, d.IsVerified, d.ExpiresOn, d.UploadedAt,
        $"{app.Value.PublicApiBaseUrl.TrimEnd('/')}/api/v1/logistics-partners/{partnerId}/documents/{d.Id}/download");

    internal static string Location(string? city, string? state, string? address) =>
        city is not null && state is not null ? $"{city}, {state}"
        : city ?? state ?? address ?? "—";

    internal static string ShipmentLabel(TripStatus status, SensorStatus sensor) => status switch
    {
        TripStatus.Completed => "Delivered",
        TripStatus.Cancelled => "Cancelled",
        TripStatus.Scheduled => "Scheduled",
        TripStatus.Delayed => "Delayed",
        TripStatus.Stopped => "Stopped",
        _ => sensor == SensorStatus.Offline ? "In Transit" : "Live",
    };
}

internal static class PartnerGeo
{
    public static async Task<GeocodeResult?> TryGeocodeAsync(IMapsService maps, string address, CancellationToken ct)
    {
        try { return await maps.GeocodeAsync(address, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
    }
}

internal static class AlertsFormat
{
    public static string Wat(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(1)).ToString("dd MMM yyyy, HH:mm") + " WAT";
}

/// Kills every live share link of a partner (suspension / removal).
internal sealed class PartnerAccessRevoker(IApplicationDbContext db, IClock clock, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
{
    public Task RevokeForLogisticsPartnerAsync(Guid partnerId, string reason, CancellationToken ct) =>
        RevokeAsync(db.ShareLinks.Where(l => l.LogisticsPartnerId == partnerId), reason, ct);

    public Task RevokeForAgroProcessorAsync(Guid processorId, string reason, CancellationToken ct) =>
        RevokeAsync(db.ShareLinks.Where(l => l.AgroProcessorId == processorId), reason, ct);

    private async Task RevokeAsync(IQueryable<ShareLink> links, string reason, CancellationToken ct)
    {
        var now = clock.UtcNow;
        foreach (var link in await links.Where(l => l.RevokedAt == null && l.ExpiresAt > now).ToListAsync(ct))
        {
            link.Revoke(reason, now);
            cache.Remove(ShareLinks.ShareLinkService.CacheKey(link.Id));
        }
    }
}
