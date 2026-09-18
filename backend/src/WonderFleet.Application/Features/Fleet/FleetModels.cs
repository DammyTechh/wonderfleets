using FluentValidation;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Models;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Fleet;

// ------------------------------------------------------------------ "Add a Fleet" wizard
public sealed record VehicleInput(
    string FleetNumber, string LicenseNumber, string VehicleType, decimal CapacityTonnes, int? YearOfManufacture, Guid LogisticsPartnerId,
    FuelType? FuelType = null, decimal? TankCapacityLitres = null, decimal? BaselineLitresPer100Km = null);

public sealed record LocationInput(string Address, double? Latitude, double? Longitude, string? Label);

public sealed record ThresholdInput(decimal MinTemperature, decimal MaxTemperature, decimal MinHumidity, decimal MaxHumidity);

public sealed record ShipmentInput(
    Guid AgroProcessorId,
    IReadOnlyList<Guid>? ProduceTypeIds,
    IReadOnlyList<string>? NewProduce,
    decimal EstimatedWeightTonnes,
    PackagingType? PackagingType,
    int? UnitCount,
    LocationInput Pickup,
    LocationInput Destination,
    DateTimeOffset LoadingTime,
    DateTimeOffset ExpectedArrival,
    string? AdditionalNotes);

/// Tabs: Vehicle Details → Shipment Info → Sensor & Docs → Review & Submit.
/// Either register a new vehicle (Vehicle) or reuse one (ExistingVehicleId).
public sealed record CreateFleetRequest(
    Guid? ExistingVehicleId,
    VehicleInput? Vehicle,
    ShipmentInput Shipment,
    Guid? DriverId,
    Guid? DeviceId,
    ThresholdInput? Thresholds,
    bool StartImmediately = false);

public sealed record FleetCreatedDto(
    Guid VehicleId, string VehicleCode, Guid TripId, string TripCode, string TripStatus, bool ThresholdsPushed,
    ThresholdInput Thresholds, Fuel.FuelEstimateDto? FuelPlan);

public sealed record UpdateVehicleRequest(
    string FleetNumber, string LicenseNumber, string VehicleType, decimal CapacityTonnes, int? YearOfManufacture, Guid LogisticsPartnerId,
    VehicleStatus Status, FuelType? FuelType = null, decimal? TankCapacityLitres = null, decimal? BaselineLitresPer100Km = null);

public sealed record NextVehicleCodeDto(string VehicleCode);

public sealed record SuggestedThresholdsDto(ThresholdInput? Thresholds, string Source, IReadOnlyList<string> Notes);

// ------------------------------------------------------------------ fleet table
public sealed record FleetListQuery : PageQuery
{
    public Guid? PartnerId { get; init; }
    public SensorStatus? Status { get; init; }
    public TripStatus? TripStatus { get; init; }
}

public sealed record FleetRowDto(
    Guid VehicleId, string VehicleCode, string FleetNumber, string VehicleType, decimal CapacityTonnes,
    Guid PartnerId, string PartnerName, Guid? TripId, string? TripCode, string? TripStatus,
    string? DriverName, string? DriverInitials, string? Route, decimal? Temperature, decimal? Humidity,
    string Status, string VehicleStatus);

public sealed record VehicleDetailDto(
    Guid Id, string VehicleCode, string FleetNumber, string LicenseNumber, string VehicleType, decimal CapacityTonnes,
    int? YearOfManufacture, Guid PartnerId, string PartnerName, string Status,
    string FuelType, decimal? TankCapacityLitres, decimal? BaselineLitresPer100Km,
    IReadOnlyList<VehicleDeviceDto> Devices, TripDetailDto? CurrentTrip, IReadOnlyList<TripSummaryDto> History);

public sealed record VehicleDeviceDto(Guid Id, string Serial, string Kind, bool IsOnline, int? BatteryLevel, DateTimeOffset? LastSeenAt);

// ------------------------------------------------------------------ trips
public sealed record TripListQuery : PageQuery
{
    public TripStatus? Status { get; init; }
    public bool OpenOnly { get; init; }
    public Guid? PartnerId { get; init; }
    public Guid? AgroProcessorId { get; init; }
    public Guid? VehicleId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public sealed record TripSummaryDto(
    Guid Id, string TripCode, string FleetNumber, string VehicleCode, string Route, string Status, string SensorStatus,
    string PartnerName, string ProcessorName, string? DriverName, IReadOnlyList<string> Produce,
    decimal EstimatedWeightTonnes, DateTimeOffset LoadingTime, DateTimeOffset ExpectedArrival,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, decimal? Temperature, decimal? Humidity);

public sealed record TripDetailDto(
    Guid Id, string TripCode, string Status, string SensorStatus,
    Guid VehicleId, string VehicleCode, string FleetNumber, string VehicleType, decimal CapacityTonnes,
    Guid PartnerId, string PartnerName, string PartnerPhone,
    Guid ProcessorId, string ProcessorName,
    Guid? DriverId, string? DriverName, string? DriverPhone,
    Guid? DeviceId, string? DeviceSerial, bool DeviceOnline, int? DeviceBattery,
    IReadOnlyList<ProduceTypeDto> Produce, decimal EstimatedWeightTonnes, string? PackagingType, int? UnitCount, string? AdditionalNotes,
    string PickupAddress, string OriginLabel, double? PickupLatitude, double? PickupLongitude,
    string DestinationAddress, string DestinationLabel, double? DestinationLatitude, double? DestinationLongitude,
    DateTimeOffset LoadingTime, DateTimeOffset ExpectedArrival, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, DateTimeOffset? CancelledAt,
    ThresholdInput Thresholds, decimal? Temperature, decimal? Humidity, double? Latitude, double? Longitude, double? SpeedKmh,
    DateTimeOffset? LastPositionAt, double DistanceTravelledKm, double? PlannedDistanceKm, decimal Co2EmissionKg,
    int OpenAlerts, int ActiveShareLinks, DateTimeOffset CreatedAt);

public sealed record CancelTripRequest(string Reason);

public sealed record UpdateThresholdsRequest(decimal MinTemperature, decimal MaxTemperature, decimal MinHumidity, decimal MaxHumidity);

public sealed record AssignTripResourcesRequest(Guid? DriverId, Guid? DeviceId, DateTimeOffset? ExpectedArrival);

// ------------------------------------------------------------------ validators
public sealed class VehicleInputValidator : AbstractValidator<VehicleInput>
{
    public VehicleInputValidator()
    {
        RuleFor(x => x.FleetNumber).NotEmpty().MaximumLength(30).Matches("^[A-Za-z0-9-]+$").WithMessage("Fleet number may contain letters, digits and dashes.");
        RuleFor(x => x.LicenseNumber).NotEmpty().MaximumLength(30).Matches("^[A-Za-z0-9 -]+$").WithMessage("Enter a valid plate number, e.g. LAG-234-XY.");
        RuleFor(x => x.VehicleType).NotEmpty().MaximumLength(60);
        RuleFor(x => x.CapacityTonnes).GreaterThan(0).LessThanOrEqualTo(100);
        RuleFor(x => x.YearOfManufacture).InclusiveBetween(1970, DateTime.UtcNow.Year + 1).When(x => x.YearOfManufacture.HasValue);
        RuleFor(x => x.LogisticsPartnerId).NotEmpty();
        RuleFor(x => x.FuelType).IsInEnum().When(x => x.FuelType.HasValue);
        RuleFor(x => x.TankCapacityLitres).InclusiveBetween(10m, 2000m).When(x => x.TankCapacityLitres.HasValue);
        RuleFor(x => x.BaselineLitresPer100Km).InclusiveBetween(3m, 120m).When(x => x.BaselineLitresPer100Km.HasValue);
    }
}

public sealed class LocationInputValidator : AbstractValidator<LocationInput>
{
    public LocationInputValidator()
    {
        RuleFor(x => x.Address).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Label).MaximumLength(100);
        RuleFor(x => x.Latitude).Latitude();
        RuleFor(x => x.Longitude).Longitude();
        RuleFor(x => x).Must(x => x.Latitude.HasValue == x.Longitude.HasValue).WithName("Latitude")
            .WithMessage("Provide both latitude and longitude, or neither.");
    }
}

public sealed class ThresholdInputValidator : AbstractValidator<ThresholdInput>
{
    public ThresholdInputValidator()
    {
        RuleFor(x => x.MinTemperature).InclusiveBetween(-30, 80);
        RuleFor(x => x.MaxTemperature).InclusiveBetween(-30, 80).GreaterThan(x => x.MinTemperature);
        RuleFor(x => x.MinHumidity).InclusiveBetween(0, 100);
        RuleFor(x => x.MaxHumidity).InclusiveBetween(0, 100).GreaterThan(x => x.MinHumidity);
    }
}

public sealed class CreateFleetRequestValidator : AbstractValidator<CreateFleetRequest>
{
    public CreateFleetRequestValidator()
    {
        RuleFor(x => x).Must(x => (x.ExistingVehicleId is null) != (x.Vehicle is null))
            .WithName("Vehicle").WithMessage("Register a new vehicle or pick an existing one (not both).");
        RuleFor(x => x.Vehicle!).SetValidator(new VehicleInputValidator()).When(x => x.Vehicle is not null);
        RuleFor(x => x.Thresholds!).SetValidator(new ThresholdInputValidator()).When(x => x.Thresholds is not null);
        RuleFor(x => x.Shipment).NotNull();
        RuleFor(x => x.Shipment).ChildRules(s =>
        {
            s.RuleFor(y => y.AgroProcessorId).NotEmpty().WithMessage("Select the agro-processor that owns the produce.");
            s.RuleFor(y => y).Must(y => (y.ProduceTypeIds?.Count ?? 0) + (y.NewProduce?.Count ?? 0) > 0)
                .WithName("ProduceTypeIds").WithMessage("Select at least one produce.");
            s.RuleFor(y => y.ProduceTypeIds).Must(p => p is null || p.Count <= 10);
            s.RuleFor(y => y.NewProduce).Must(p => p is null || p.Count <= 5);
            s.RuleForEach(y => y.NewProduce).NotEmpty().MaximumLength(80);
            s.RuleFor(y => y.EstimatedWeightTonnes).GreaterThan(0).LessThanOrEqualTo(100);
            s.RuleFor(y => y.PackagingType).IsInEnum().When(y => y.PackagingType.HasValue);
            s.RuleFor(y => y.UnitCount).InclusiveBetween(0, 1_000_000).When(y => y.UnitCount.HasValue);
            s.RuleFor(y => y.AdditionalNotes).MaximumLength(1000);
            s.RuleFor(y => y.Pickup).NotNull().SetValidator(new LocationInputValidator());
            s.RuleFor(y => y.Destination).NotNull().SetValidator(new LocationInputValidator());
            s.RuleFor(y => y.ExpectedArrival).GreaterThan(y => y.LoadingTime).WithMessage("Expected arrival must be after loading time.");
            s.RuleFor(y => y.LoadingTime).GreaterThan(_ => DateTimeOffset.UtcNow.AddDays(-2)).WithMessage("Loading time is too far in the past.")
                .LessThan(_ => DateTimeOffset.UtcNow.AddDays(90)).WithMessage("Loading time must be within 90 days.");
            s.RuleFor(y => y).Must(y => y.ExpectedArrival - y.LoadingTime <= TimeSpan.FromDays(14))
                .WithName("ExpectedArrival").WithMessage("A trip cannot be planned for more than 14 days.");
        });
    }
}

public sealed class UpdateVehicleRequestValidator : AbstractValidator<UpdateVehicleRequest>
{
    public UpdateVehicleRequestValidator()
    {
        RuleFor(x => new VehicleInput(x.FleetNumber, x.LicenseNumber, x.VehicleType, x.CapacityTonnes, x.YearOfManufacture,
                x.LogisticsPartnerId, x.FuelType, x.TankCapacityLitres, x.BaselineLitresPer100Km))
            .SetValidator(new VehicleInputValidator()).OverridePropertyName("Vehicle");
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class CancelTripRequestValidator : AbstractValidator<CancelTripRequest>
{
    public CancelTripRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
}

public sealed class UpdateThresholdsRequestValidator : AbstractValidator<UpdateThresholdsRequest>
{
    public UpdateThresholdsRequestValidator() =>
        RuleFor(x => new ThresholdInput(x.MinTemperature, x.MaxTemperature, x.MinHumidity, x.MaxHumidity))
            .SetValidator(new ThresholdInputValidator()).OverridePropertyName("Thresholds");
}

public sealed class AssignTripResourcesRequestValidator : AbstractValidator<AssignTripResourcesRequest>
{
    public AssignTripResourcesRequestValidator() =>
        RuleFor(x => x).Must(x => x.DriverId.HasValue || x.DeviceId.HasValue || x.ExpectedArrival.HasValue)
            .WithName("DriverId").WithMessage("Nothing to update.");
}

internal static class LocationLabels
{
    /// "12 Admiralty Way, Lekki, Lagos" → "Lagos" (prefer geocoded city, else the last meaningful address part).
    public static string From(LocationInput input, GeocodeLike? geo)
    {
        if (Text.Trimmed(input.Label) is { } label) return Cap(label);
        if (Text.Trimmed(geo?.City) is { } city) return Cap(city);
        var parts = input.Address.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !p.Equals("Nigeria", StringComparison.OrdinalIgnoreCase) && !p.Any(char.IsDigit)).ToList();
        var pick = parts.Count > 0 ? parts[^1].Replace(" State", "", StringComparison.OrdinalIgnoreCase) : input.Address;
        return Cap(pick);
    }

    private static string Cap(string s) => s.Length > 100 ? s[..100] : s;
}

internal sealed record GeocodeLike(string? City, double Latitude, double Longitude);
