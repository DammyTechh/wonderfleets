using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Models;
using WonderFleet.Application.Features.Notifications;
using WonderFleet.Application.Features.Telemetry;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Partners;

public sealed record ProcessorContactInput(string FullName, string PhoneNumber, string Email, bool IsPrimary);

/// "Add a Processor": processor info (GPS-referenced facility) + one or more contact persons.
public sealed record AgroProcessorRequest(
    string Name, string Address, double? Latitude, double? Longitude, string? City, string? State,
    IReadOnlyList<ProcessorContactInput> Contacts);

public sealed record AgroProcessorListItemDto(
    Guid Id, string ProcessorCode, string Name, string Initials, string? PhotoUrl,
    string? ContactName, string? ContactEmail, string? ContactPhone, int FleetOrders, string Location, string Status);

public sealed record ProcessorContactDto(Guid Id, string FullName, string PhoneNumber, string Email, bool IsPrimary);

public sealed record AgroProcessorStatsDto(int TotalOrders, int ActiveShipments, int Delivered, int OpenCriticalAlerts);

public sealed record AgroProcessorDetailDto(
    Guid Id, string ProcessorCode, string Name, string Initials, string? PhotoUrl, string Status,
    string Address, double? Latitude, double? Longitude, string? City, string? State, string Location,
    IReadOnlyList<ProcessorContactDto> Contacts, AgroProcessorStatsDto Stats,
    IReadOnlyList<RecentShipmentDto> RecentShipments, DateTimeOffset CreatedAt);

public sealed class AgroProcessorRequestValidator : AbstractValidator<AgroProcessorRequest>
{
    public AgroProcessorRequestValidator()
    {
        RuleFor(x => x.Name).Name(200);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Latitude).Latitude();
        RuleFor(x => x.Longitude).Longitude();
        RuleFor(x => x).Must(x => x.Latitude.HasValue == x.Longitude.HasValue)
            .WithName("Latitude").WithMessage("Provide both latitude and longitude, or neither.");
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.Contacts).NotEmpty().WithMessage("Add at least one contact person.")
            .Must(c => c is null || c.Count <= 10).WithMessage("At most 10 contact persons.")
            .Must(c => c is null || c.Count(x => x.IsPrimary) <= 1).WithMessage("Only one contact can be primary.");
        RuleForEach(x => x.Contacts).ChildRules(c =>
        {
            c.RuleFor(y => y.FullName).Name();
            c.RuleFor(y => y.PhoneNumber).PhoneNumber();
            c.RuleFor(y => y.Email).EmailAddressStrict();
        });
    }
}

public interface IAgroProcessorService
{
    Task<PagedResult<AgroProcessorListItemDto>> ListAsync(PartnerListQuery query, CancellationToken ct);
    Task<AgroProcessorDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<IdResponse> CreateAsync(AgroProcessorRequest request, CancellationToken ct);
    Task<AgroProcessorDetailDto> UpdateAsync(Guid id, AgroProcessorRequest request, CancellationToken ct);
    Task ChangeStatusAsync(Guid id, ChangePartnerStatusRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<string?> UploadPhotoAsync(Guid id, UploadedFile file, CancellationToken ct);
    Task<ReportFile> ExportAsync(PartnerListQuery query, CancellationToken ct);
}

internal sealed class AgroProcessorService(
    IApplicationDbContext db,
    ICodeGenerator codes,
    IClock clock,
    IAuditLogger audit,
    IFileStorage storage,
    IMediaUrlSigner media,
    IMapsService maps,
    INotificationComposer notify,
    IReportRenderer reports,
    PartnerAccessRevoker revoker) : IAgroProcessorService
{
    public async Task<PagedResult<AgroProcessorListItemDto>> ListAsync(PartnerListQuery query, CancellationToken ct)
    {
        var q = Filter(query);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(p => p.CreatedAt).Skip(query.Skip).Take(query.PageSize)
            .Select(p => new
            {
                p.Id, p.ProcessorCode, p.Name, p.PhotoPath, p.City, p.State, p.Address, p.Status,
                Contact = p.Contacts.OrderByDescending(c => c.IsPrimary).Select(c => new { c.FullName, c.Email, c.PhoneNumber }).FirstOrDefault(),
                Orders = db.Trips.Count(t => t.AgroProcessorId == p.Id),
            })
            .ToListAsync(ct);

        return new PagedResult<AgroProcessorListItemDto>(rows.Select(p => new AgroProcessorListItemDto(
            p.Id, p.ProcessorCode, p.Name, Text.Initials(p.Name), media.Sign(p.PhotoPath),
            p.Contact?.FullName, p.Contact?.Email, p.Contact?.PhoneNumber, p.Orders,
            LogisticsPartnerService.Location(p.City, p.State, p.Address), p.Status.ToString())).ToList(),
            query.Page, query.PageSize, total);
    }

    public async Task<AgroProcessorDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.AgroProcessors.AsNoTracking().Include(x => x.Contacts)
            .FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
            ?? throw new NotFoundException("Agro-processor", id);

        var trips = db.Trips.AsNoTracking().Where(t => t.AgroProcessorId == id);
        var stats = new AgroProcessorStatsDto(
            await trips.CountAsync(ct),
            await trips.CountAsync(t => TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct),
            await trips.CountAsync(t => t.Status == TripStatus.Completed, ct),
            await db.Alerts.CountAsync(a => a.Trip!.AgroProcessorId == id && a.Status != AlertStatus.Resolved && a.Severity == AlertSeverity.Critical, ct));

        var recent = await trips.OrderByDescending(t => t.CreatedAt).Take(10)
            .Select(t => new { t.Id, t.TripCode, t.Vehicle!.FleetNumber, t.OriginLabel, t.DestinationLabel, t.Status, t.SensorStatus, t.CompletedAt, t.StartedAt, t.LoadingTime })
            .ToListAsync(ct);

        return new AgroProcessorDetailDto(
            p.Id, p.ProcessorCode, p.Name, Text.Initials(p.Name), media.Sign(p.PhotoPath), p.Status.ToString(),
            p.Address, p.Latitude, p.Longitude, p.City, p.State, LogisticsPartnerService.Location(p.City, p.State, p.Address),
            p.Contacts.OrderByDescending(c => c.IsPrimary).ThenBy(c => c.FullName)
                .Select(c => new ProcessorContactDto(c.Id, c.FullName, c.PhoneNumber, c.Email, c.IsPrimary)).ToList(),
            stats,
            recent.Select(t => new RecentShipmentDto(t.Id, t.TripCode, t.FleetNumber, Text.Route(t.OriginLabel, t.DestinationLabel),
                LogisticsPartnerService.ShipmentLabel(t.Status, t.SensorStatus), t.CompletedAt ?? t.StartedAt ?? t.LoadingTime)).ToList(),
            p.CreatedAt);
    }

    public async Task<IdResponse> CreateAsync(AgroProcessorRequest request, CancellationToken ct)
    {
        await EnsureUniqueAsync(request.Name, null, ct);
        var processor = new AgroProcessor { ProcessorCode = await codes.NextProcessorCodeAsync(ct), Status = PartnerStatus.Pending };
        await ApplyAsync(processor, request, ct);
        db.AgroProcessors.Add(processor);

        notify.InApp(NotificationCategory.Partner, "New agro-processor added",
            $"{processor.Name} ({processor.ProcessorCode}) was onboarded and is pending activation.",
            nameof(AgroProcessor), processor.Id, requiresReview: true);
        audit.Record("agro_processor.created", nameof(AgroProcessor), processor.Id, new { processor.ProcessorCode });
        await db.SaveChangesAsync(ct);
        return new IdResponse(processor.Id, processor.ProcessorCode);
    }

    public async Task<AgroProcessorDetailDto> UpdateAsync(Guid id, AgroProcessorRequest request, CancellationToken ct)
    {
        var processor = await db.AgroProcessors.Include(p => p.Contacts)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct)
            ?? throw new NotFoundException("Agro-processor", id);
        await EnsureUniqueAsync(request.Name, id, ct);

        db.AgroProcessorContacts.RemoveRange(processor.Contacts);
        processor.Contacts.Clear();
        // Flush removals first so the "one primary contact" partial unique index never sees two primaries.
        await db.SaveChangesAsync(ct);

        await ApplyAsync(processor, request, ct);
        audit.Record("agro_processor.updated", nameof(AgroProcessor), id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task ChangeStatusAsync(Guid id, ChangePartnerStatusRequest request, CancellationToken ct)
    {
        var processor = await db.AgroProcessors.Include(p => p.Contacts)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct)
            ?? throw new NotFoundException("Agro-processor", id);
        if (processor.Status == request.Status) return;

        var previous = processor.Status;
        processor.Status = request.Status;
        var contact = processor.PrimaryContact;
        if (contact is not null)
        {
            if (request.Status == PartnerStatus.Active && previous == PartnerStatus.Pending)
                notify.Email(contact.Email, EmailTemplates.PartnerOnboarded, new Dictionary<string, string?>
                {
                    ["Name"] = contact.FullName,
                    ["OrganisationName"] = processor.Name,
                    ["PartnerCode"] = processor.ProcessorCode,
                    ["PartnerType"] = "agro-processor",
                });
            else
                notify.Email(contact.Email, EmailTemplates.PartnerStatusChanged, new Dictionary<string, string?>
                {
                    ["Name"] = contact.FullName,
                    ["OrganisationName"] = processor.Name,
                    ["Status"] = request.Status.ToString(),
                    ["Reason"] = Text.Trimmed(request.Reason) ?? "No reason was provided.",
                });
        }

        if (request.Status == PartnerStatus.Suspended)
            await revoker.RevokeForAgroProcessorAsync(id, "partner_suspended", ct);

        notify.InApp(NotificationCategory.Partner, $"Agro-processor {request.Status.ToString().ToLowerInvariant()}",
            $"{processor.Name} moved from {previous} to {request.Status}.", nameof(AgroProcessor), id);
        audit.Record("agro_processor.status_changed", nameof(AgroProcessor), id, new { from = previous, to = request.Status, request.Reason });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var processor = await db.AgroProcessors.FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct)
            ?? throw new NotFoundException("Agro-processor", id);
        if (await db.Trips.AnyAsync(t => t.AgroProcessorId == id && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct))
            throw new BusinessRuleException("partner.has_open_trips", "This processor has shipments in progress. Complete or cancel them first.");

        processor.DeletedAt = clock.UtcNow;
        processor.Status = PartnerStatus.Suspended;
        await revoker.RevokeForAgroProcessorAsync(id, "partner_removed", ct);
        audit.Record("agro_processor.deleted", nameof(AgroProcessor), id);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> UploadPhotoAsync(Guid id, UploadedFile file, CancellationToken ct)
    {
        var (ext, _) = FileRules.EnsureImage(file);
        var processor = await db.AgroProcessors.FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct)
            ?? throw new NotFoundException("Agro-processor", id);
        await using var stream = file.OpenReadStream();
        var path = await storage.SaveAsync(stream, "processors", ext, ct);
        var old = processor.PhotoPath;
        processor.PhotoPath = path;
        audit.Record("agro_processor.photo_changed", nameof(AgroProcessor), id);
        await db.SaveChangesAsync(ct);
        if (old is not null) await storage.DeleteAsync(old, ct);
        return media.Sign(path);
    }

    public async Task<ReportFile> ExportAsync(PartnerListQuery query, CancellationToken ct)
    {
        var rows = await Filter(query).OrderBy(p => p.ProcessorCode).Take(5000)
            .Select(p => new
            {
                p.ProcessorCode, p.Name, p.Address, p.City, p.State, p.Status, p.Latitude, p.Longitude,
                Contact = p.Contacts.OrderByDescending(c => c.IsPrimary).Select(c => new { c.FullName, c.Email, c.PhoneNumber }).FirstOrDefault(),
                Orders = db.Trips.Count(t => t.AgroProcessorId == p.Id),
            })
            .ToListAsync(ct);
        var table = new ReportTable("Agro-processors", $"Exported {AlertsFormat.Wat(clock.UtcNow)}",
            ["Code", "Name", "Contact", "Email", "Phone", "Address", "Location", "GPS", "Fleet orders", "Status"],
            rows.Select(p => (IReadOnlyList<string>)new List<string> {
                p.ProcessorCode, p.Name, p.Contact?.FullName ?? "", p.Contact?.Email ?? "", p.Contact?.PhoneNumber ?? "",
                p.Address, LogisticsPartnerService.Location(p.City, p.State, null),
                p.Latitude is null ? "" : FormattableString.Invariant($"{p.Latitude:0.00000},{p.Longitude:0.00000}"),
                p.Orders.ToString(), p.Status.ToString(),
            }).ToList(),
            []);
        audit.Record("agro_processor.exported", nameof(AgroProcessor), null, new { rows = rows.Count });
        await db.SaveChangesAsync(ct);
        return reports.RenderCsv(table, $"agro-processors-{clock.UtcNow:yyyyMMdd}.csv");
    }

    private IQueryable<AgroProcessor> Filter(PartnerListQuery query)
    {
        var q = db.AgroProcessors.AsNoTracking().Where(p => p.DeletedAt == null);
        if (query.Status is { } status) q = q.Where(p => p.Status == status);
        if (query.NormalizedSearch is { } term)
            q = q.Where(p => p.Name.ToLower().Contains(term) || p.ProcessorCode.ToLower().Contains(term)
                             || p.Contacts.Any(c => c.FullName.ToLower().Contains(term) || c.Email.ToLower().Contains(term)));
        return q;
    }

    private async Task EnsureUniqueAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var n = name.Trim().ToLowerInvariant();
        if (await db.AgroProcessors.AnyAsync(p => p.DeletedAt == null && p.Id != exceptId && p.Name.ToLower() == n, ct))
            throw new ConflictException("An agro-processor with this name already exists.", "processor.duplicate_name");
    }

    private async Task ApplyAsync(AgroProcessor p, AgroProcessorRequest r, CancellationToken ct)
    {
        var address = r.Address.Trim();
        var addressChanged = !string.Equals(p.Address, address, StringComparison.OrdinalIgnoreCase);
        p.Name = r.Name.Trim();
        p.Address = address;
        p.City = Text.Trimmed(r.City);
        p.State = Text.Trimmed(r.State);

        if (r.Latitude.HasValue && r.Longitude.HasValue)
        {
            p.Latitude = r.Latitude;
            p.Longitude = r.Longitude;
        }
        else if (addressChanged || p.Latitude is null)
        {
            // GPS-referenced facility address: geocode when the map picker did not supply a pin.
            var geo = await PartnerGeo.TryGeocodeAsync(maps, address, ct);
            p.Latitude = geo?.Location.Latitude;
            p.Longitude = geo?.Location.Longitude;
            p.City ??= geo?.City;
            p.State ??= geo?.State;
        }

        var contacts = r.Contacts.ToList();
        var primaryIndex = Math.Max(0, contacts.FindIndex(c => c.IsPrimary));
        for (var i = 0; i < contacts.Count; i++)
        {
            var c = contacts[i];
            p.Contacts.Add(new AgroProcessorContact
            {
                AgroProcessorId = p.Id,
                FullName = c.FullName.Trim(),
                PhoneNumber = Phone.ToE164(c.PhoneNumber),
                Email = c.Email.Trim(),
                IsPrimary = i == primaryIndex,
            });
        }
    }
}
