using WonderFleet.Domain.Common;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Domain.Entities;

public class Driver : AuditableEntity, ISoftDeletable
{
    public Guid LogisticsPartnerId { get; set; }
    public LogisticsPartner? LogisticsPartner { get; set; }
    public string FullName { get; set; } = default!;
    public string PhoneNumber { get; set; } = default!;
    public string? LicenseNumber { get; set; }
    public string? HomeBase { get; set; }
    public decimal Rating { get; set; } = 5.0m;
    public int CompletedTrips { get; set; }
    public DriverStatus Status { get; set; } = DriverStatus.Available;
    public DateTimeOffset? DeletedAt { get; set; }
}

public class Vehicle : AuditableEntity, ISoftDeletable
{
    public string VehicleCode { get; set; } = default!;
    public string FleetNumber { get; set; } = default!;
    public string LicenseNumber { get; set; } = default!;
    public string VehicleType { get; set; } = default!;
    public decimal CapacityTonnes { get; set; }
    public int? YearOfManufacture { get; set; }
    public FuelType FuelType { get; set; } = FuelType.Diesel;
    public decimal? TankCapacityLitres { get; set; }
    /// Measured consumption from the partner's own fuel logs; overrides the modelled baseline.
    public decimal? BaselineConsumptionLPer100Km { get; set; }
    public Guid LogisticsPartnerId { get; set; }
    public LogisticsPartner? LogisticsPartner { get; set; }
    public VehicleStatus Status { get; set; } = VehicleStatus.Active;
    public DateTimeOffset? DeletedAt { get; set; }
    /// Optimistic concurrency token (mapped to PostgreSQL xmin).
    public uint Version { get; set; }
}

/// IoT unit. FirebaseKey is the node the firmware writes to (vehicles/{FirebaseKey} and settings/{FirebaseKey}).
public class Device : AuditableEntity
{
    public string Serial { get; set; } = default!;
    public string FirebaseKey { get; set; } = default!;
    public DeviceKind Kind { get; set; } = DeviceKind.Master;
    public Guid? ParentDeviceId { get; set; }
    public Guid? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string? FirmwareVersion { get; set; }
    public int? BatteryLevel { get; set; }
    public bool IsOnline { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastChangedAt { get; set; }
    public decimal? LastTemperature { get; set; }
    public decimal? LastHumidity { get; set; }
    public double? LastLatitude { get; set; }
    public double? LastLongitude { get; set; }
    /// Fingerprint of the last payload so identical polls are not stored twice.
    public string? LastPayloadHash { get; set; }

    /// Standing limits for this unit, used when it is not on a trip. A trip's own
    /// limits take over while it is open. These are what gets written to Firebase.
    public decimal? MinTemperature { get; set; }
    public decimal? MaxTemperature { get; set; }
    public decimal? MinHumidity { get; set; }
    public decimal? MaxHumidity { get; set; }
}

public class ProduceType : Entity
{
    public string Name { get; set; } = default!;
    public decimal? DefaultMinTemperature { get; set; }
    public decimal? DefaultMaxTemperature { get; set; }
    public decimal? DefaultMinHumidity { get; set; }
    public decimal? DefaultMaxHumidity { get; set; }
}
