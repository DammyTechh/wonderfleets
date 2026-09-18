using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Models;
using WonderFleet.Application.Features.Telemetry;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Fleet;

public sealed record DriverRequest(
    Guid LogisticsPartnerId, string FullName, string PhoneNumber, string? LicenseNumber, string? HomeBase, DriverStatus? Status);

public sealed record DriverListQuery : PageQuery
{
    public Guid? PartnerId { get; init; }
    public DriverStatus? Status { get; init; }
    /// Driver Assignment search in the wizard only needs drivers who are free.
    public bool AvailableOnly { get; init; }
}

public sealed record DriverDto(
    Guid Id, string FullName, string Initials, string PhoneNumber, string? LicenseNumber, string? HomeBase,
    decimal Rating, int CompletedTrips, string Status, Guid LogisticsPartnerId, string PartnerName, string? CurrentAssignment);

public sealed class DriverRequestValidator : AbstractValidator<DriverRequest>
{
    public DriverRequestValidator()
    {
        RuleFor(x => x.LogisticsPartnerId).NotEmpty();
        RuleFor(x => x.FullName).Name();
        RuleFor(x => x.PhoneNumber).PhoneNumber();
        RuleFor(x => x.LicenseNumber).MaximumLength(40);
        RuleFor(x => x.HomeBase).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue)
            .NotEqual(DriverStatus.OnTrip).WithMessage("On-trip status is set automatically when a trip starts.");
    }
}

public interface IDriverService
{
    Task<PagedResult<DriverDto>> ListAsync(DriverListQuery query, CancellationToken ct);
    Task<DriverDto> GetAsync(Guid id, CancellationToken ct);
    Task<IdResponse> CreateAsync(DriverRequest request, CancellationToken ct);
    Task<DriverDto> UpdateAsync(Guid id, DriverRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

internal sealed class DriverService(IApplicationDbContext db, IAuditLogger audit, IClock clock) : IDriverService
{
    public async Task<PagedResult<DriverDto>> ListAsync(DriverListQuery query, CancellationToken ct)
    {
        var q = db.Drivers.AsNoTracking().Where(d => d.DeletedAt == null);
        if (query.PartnerId is { } pid) q = q.Where(d => d.LogisticsPartnerId == pid);
        if (query.Status is { } s) q = q.Where(d => d.Status == s);
        if (query.AvailableOnly)
            q = q.Where(d => d.Status == DriverStatus.Available
                             && !db.Trips.Any(t => t.DriverId == d.Id && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status)));
        if (query.NormalizedSearch is { } term)
            q = q.Where(d => d.FullName.ToLower().Contains(term) || d.PhoneNumber.Contains(term)
                             || (d.LicenseNumber != null && d.LicenseNumber.ToLower().Contains(term)));

        var total = await q.CountAsync(ct);
        var rows = await Project(q.OrderBy(d => d.FullName).Skip(query.Skip).Take(query.PageSize)).ToListAsync(ct);
        return new PagedResult<DriverDto>(rows.Select(Map).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<DriverDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await Project(db.Drivers.AsNoTracking().Where(d => d.Id == id && d.DeletedAt == null)).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Driver", id);
        return Map(row);
    }

    public async Task<IdResponse> CreateAsync(DriverRequest request, CancellationToken ct)
    {
        await EnsurePartnerAsync(request.LogisticsPartnerId, ct);
        var phone = Phone.ToE164(request.PhoneNumber);
        if (await db.Drivers.AnyAsync(d => d.DeletedAt == null && d.PhoneNumber == phone, ct))
            throw new ConflictException("A driver with this phone number already exists.", "driver.duplicate_phone");

        var driver = new Driver
        {
            LogisticsPartnerId = request.LogisticsPartnerId,
            FullName = request.FullName.Trim(),
            PhoneNumber = phone,
            LicenseNumber = Text.Trimmed(request.LicenseNumber)?.ToUpperInvariant(),
            HomeBase = Text.Trimmed(request.HomeBase),
            Status = request.Status ?? DriverStatus.Available,
        };
        db.Drivers.Add(driver);
        audit.Record("driver.created", nameof(Driver), driver.Id);
        await db.SaveChangesAsync(ct);
        return new IdResponse(driver.Id);
    }

    public async Task<DriverDto> UpdateAsync(Guid id, DriverRequest request, CancellationToken ct)
    {
        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.Id == id && d.DeletedAt == null, ct)
            ?? throw new NotFoundException("Driver", id);
        var onTrip = await OnOpenTripAsync(id, ct);
        if (onTrip && driver.LogisticsPartnerId != request.LogisticsPartnerId)
            throw new BusinessRuleException("driver.on_trip", "A driver on an active trip cannot change partner.");
        await EnsurePartnerAsync(request.LogisticsPartnerId, ct);

        var phone = Phone.ToE164(request.PhoneNumber);
        if (await db.Drivers.AnyAsync(d => d.DeletedAt == null && d.Id != id && d.PhoneNumber == phone, ct))
            throw new ConflictException("A driver with this phone number already exists.", "driver.duplicate_phone");

        driver.LogisticsPartnerId = request.LogisticsPartnerId;
        driver.FullName = request.FullName.Trim();
        driver.PhoneNumber = phone;
        driver.LicenseNumber = Text.Trimmed(request.LicenseNumber)?.ToUpperInvariant();
        driver.HomeBase = Text.Trimmed(request.HomeBase);
        if (request.Status is { } status && !onTrip) driver.Status = status;
        audit.Record("driver.updated", nameof(Driver), id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.Id == id && d.DeletedAt == null, ct)
            ?? throw new NotFoundException("Driver", id);
        if (await OnOpenTripAsync(id, ct))
            throw new BusinessRuleException("driver.on_trip", "This driver is assigned to an active trip.");
        driver.DeletedAt = clock.UtcNow;
        audit.Record("driver.deleted", nameof(Driver), id);
        await db.SaveChangesAsync(ct);
    }

    private Task<bool> OnOpenTripAsync(Guid id, CancellationToken ct) =>
        db.Trips.AnyAsync(t => t.DriverId == id && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct);

    private async Task EnsurePartnerAsync(Guid partnerId, CancellationToken ct)
    {
        if (!await db.LogisticsPartners.AnyAsync(p => p.Id == partnerId && p.DeletedAt == null, ct))
            throw RequestValidationException.For(nameof(DriverRequest.LogisticsPartnerId), "Select an existing logistics partner.");
    }

    private sealed record Row(Guid Id, string FullName, string PhoneNumber, string? LicenseNumber, string? HomeBase,
        decimal Rating, int CompletedTrips, DriverStatus Status, Guid PartnerId, string PartnerName, string? Assignment);

    private IQueryable<Row> Project(IQueryable<Driver> q) => q.Select(d => new Row(
        d.Id, d.FullName, d.PhoneNumber, d.LicenseNumber, d.HomeBase, d.Rating, d.CompletedTrips, d.Status,
        d.LogisticsPartnerId, d.LogisticsPartner!.CompanyName,
        db.Trips.Where(t => t.DriverId == d.Id && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status))
            .Select(t => t.Vehicle!.FleetNumber + " · " + t.OriginLabel + " → " + t.DestinationLabel).FirstOrDefault()));

    private static DriverDto Map(Row r) => new(r.Id, r.FullName, Text.Initials(r.FullName), r.PhoneNumber, r.LicenseNumber, r.HomeBase,
        r.Rating, r.CompletedTrips, r.Status.ToString(), r.PartnerId, r.PartnerName, r.Assignment);
}
