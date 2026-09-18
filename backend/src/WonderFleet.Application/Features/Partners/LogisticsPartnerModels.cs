using FluentValidation;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Models;
using WonderFleet.Application.Features.Tracking;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Partners;

public sealed record TruckTypeInput(string TruckType, decimal MaxTonnage, int Quantity);

/// Mirrors the four "Add a Partner" tabs: Company Info, Fleet Details, Operations & Availability, Compliance.
public sealed record LogisticsPartnerRequest(
    string CompanyName,
    string ContactPerson,
    string CacNumber,
    string PhoneNumber,
    string Email,
    int FleetSize,
    int DriverPoolSize,
    int YearsOfOperation,
    IReadOnlyList<TruckTypeInput>? TruckTypes,
    IReadOnlyList<string>? Corridors,
    AvailabilityStatus AvailabilityStatus,
    InsuranceCoverageType? InsuranceCoverageType,
    DateOnly? InsuranceExpiryDate,
    string? OfficeAddress,
    string? City,
    string? State);

public sealed record PartnerListQuery : PageQuery
{
    public PartnerStatus? Status { get; init; }
}

public sealed record ChangePartnerStatusRequest(PartnerStatus Status, string? Reason);

public sealed record LogisticsPartnerListItemDto(
    Guid Id, string PartnerCode, string CompanyName, string Initials, string? PhotoUrl,
    string Email, string PhoneNumber, int FleetSize, int RegisteredVehicles, string Location, string Status);

public sealed record PartnerContactCardDto(string ContactPerson, string Location, string PhoneNumber, string Email);

public sealed record PartnerStatsDto(int FleetSize, int CompletedTrips, int ActiveShipments, int ActiveShipmentsThisWeek);

public sealed record CompanyInfoDto(
    string CompanyName, string CacNumber, string ContactPerson, string PhoneNumber, string Email,
    string? OfficeAddress, int YearsOfOperation, int DriverPoolSize, string AvailabilityStatus,
    string? InsuranceCoverageType, DateOnly? InsuranceExpiryDate);

public sealed record PartnerDocumentDto(
    Guid Id, string DocumentType, string FileName, string ContentType, long SizeBytes,
    bool IsVerified, DateOnly? ExpiresOn, DateTimeOffset UploadedAt, string DownloadUrl);

public sealed record TruckTypeDto(Guid Id, string TruckType, decimal MaxTonnage, int Quantity);

public sealed record RecentShipmentDto(Guid TripId, string TripCode, string FleetNumber, string Route, string Status, DateTimeOffset Date);

public sealed record LogisticsPartnerProfileDto(
    Guid Id, string PartnerCode, string CompanyName, string Initials, string? PhotoUrl, string Status,
    DateTimeOffset? OnboardedAt, PartnerContactCardDto Contact, PartnerStatsDto Stats, CompanyInfoDto Company,
    IReadOnlyList<string> Corridors, IReadOnlyList<PartnerDocumentDto> Documents, DriverPoolDto DriverPool,
    IReadOnlyList<TruckTypeDto> FleetComposition, IReadOnlyList<RecentShipmentDto> RecentShipments,
    string? City, string? State);

public sealed record UploadPartnerDocumentRequest(PartnerDocumentType DocumentType, DateOnly? ExpiresOn);

public sealed record VerifyDocumentRequest(bool IsVerified, DateOnly? ExpiresOn);

public sealed record FileDownload(Stream Content, string ContentType, string FileName);

public sealed class LogisticsPartnerRequestValidator : AbstractValidator<LogisticsPartnerRequest>
{
    public LogisticsPartnerRequestValidator()
    {
        RuleFor(x => x.CompanyName).Name(200);
        RuleFor(x => x.ContactPerson).Name();
        RuleFor(x => x.CacNumber).NotEmpty().MaximumLength(30)
            .Matches("^(RC|BN|IT|LP|LLP)?[- ]?[0-9]{4,10}$").WithMessage("Enter a valid CAC number, e.g. RC-1849213.");
        RuleFor(x => x.PhoneNumber).PhoneNumber();
        RuleFor(x => x.Email).EmailAddressStrict();
        RuleFor(x => x.FleetSize).InclusiveBetween(0, 10_000);
        RuleFor(x => x.DriverPoolSize).InclusiveBetween(0, 50_000);
        RuleFor(x => x.YearsOfOperation).InclusiveBetween(0, 150);
        RuleFor(x => x.AvailabilityStatus).IsInEnum();
        RuleFor(x => x.InsuranceCoverageType).IsInEnum().When(x => x.InsuranceCoverageType.HasValue);
        RuleFor(x => x.OfficeAddress).MaximumLength(300);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.TruckTypes).Must(t => t is null || t.Count <= 20).WithMessage("At most 20 truck types.");
        RuleForEach(x => x.TruckTypes).ChildRules(t =>
        {
            t.RuleFor(y => y.TruckType).NotEmpty().MaximumLength(60);
            t.RuleFor(y => y.MaxTonnage).GreaterThan(0).LessThanOrEqualTo(100);
            t.RuleFor(y => y.Quantity).InclusiveBetween(1, 10_000);
        });
        RuleFor(x => x.Corridors).Must(c => c is null || c.Count <= 30).WithMessage("At most 30 corridors.");
        RuleForEach(x => x.Corridors).NotEmpty().MaximumLength(120);
    }
}

public sealed class ChangePartnerStatusRequestValidator : AbstractValidator<ChangePartnerStatusRequest>
{
    public ChangePartnerStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Reason).MaximumLength(300);
    }
}

public sealed class UploadPartnerDocumentRequestValidator : AbstractValidator<UploadPartnerDocumentRequest>
{
    public UploadPartnerDocumentRequestValidator() => RuleFor(x => x.DocumentType).IsInEnum();
}
