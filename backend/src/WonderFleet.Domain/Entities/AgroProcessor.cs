using WonderFleet.Domain.Common;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Domain.Entities;

/// Owner of the produce being transported.
public class AgroProcessor : AuditableEntity, ISoftDeletable
{
    public string ProcessorCode { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Address { get; set; } = default!;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PhotoPath { get; set; }
    public PartnerStatus Status { get; set; } = PartnerStatus.Pending;
    public DateTimeOffset? DeletedAt { get; set; }

    public List<AgroProcessorContact> Contacts { get; } = [];

    public AgroProcessorContact? PrimaryContact =>
        Contacts.FirstOrDefault(c => c.IsPrimary) ?? Contacts.FirstOrDefault();
}

public class AgroProcessorContact : Entity
{
    public Guid AgroProcessorId { get; set; }
    public string FullName { get; set; } = default!;
    public string PhoneNumber { get; set; } = default!;
    public string Email { get; set; } = default!;
    public bool IsPrimary { get; set; }
}
