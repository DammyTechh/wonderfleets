using WonderFleet.Domain.Common;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Domain.Entities;

/// Transport company. Moves produce but does not own it.
public class LogisticsPartner : AuditableEntity, ISoftDeletable
{
    public string PartnerCode { get; set; } = default!;
    public string CompanyName { get; set; } = default!;
    public string ContactPerson { get; set; } = default!;
    public string CacNumber { get; set; } = default!;
    public string PhoneNumber { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? OfficeAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PhotoPath { get; set; }
    public int FleetSize { get; set; }
    public int DriverPoolSize { get; set; }
    public int YearsOfOperation { get; set; }
    public AvailabilityStatus AvailabilityStatus { get; set; } = AvailabilityStatus.AvailableNow;
    public InsuranceCoverageType? InsuranceCoverageType { get; set; }
    public DateOnly? InsuranceExpiryDate { get; set; }
    public PartnerStatus Status { get; set; } = PartnerStatus.Pending;
    public DateTimeOffset? OnboardedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public List<PartnerTruckType> TruckTypes { get; } = [];
    public List<PartnerCorridor> Corridors { get; } = [];
    public List<PartnerDocument> Documents { get; } = [];
    public List<Driver> Drivers { get; } = [];
    public List<Vehicle> Vehicles { get; } = [];
}

public class PartnerTruckType : Entity
{
    public Guid LogisticsPartnerId { get; set; }
    public string TruckType { get; set; } = default!;
    public decimal MaxTonnage { get; set; }
    public int Quantity { get; set; }
}

public class PartnerCorridor : Entity
{
    public Guid LogisticsPartnerId { get; set; }
    public string Name { get; set; } = default!;
}

public class PartnerDocument : Entity
{
    public Guid LogisticsPartnerId { get; set; }
    public PartnerDocumentType DocumentType { get; set; }
    public string FileName { get; set; } = default!;
    public string StoragePath { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public bool IsVerified { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}
