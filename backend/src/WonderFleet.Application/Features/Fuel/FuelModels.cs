using FluentValidation;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Fuel;

/// Ad-hoc estimate, used by the Add a Fleet wizard before the trip exists.
public sealed record FuelEstimateRequest(
    Guid? VehicleId,
    FuelType? FuelType,
    decimal? CapacityTonnes,
    decimal? PayloadTonnes,
    string? Origin,
    string? Destination,
    double? DistanceKm,
    DateTimeOffset? Departure,
    bool? Refrigerated,
    decimal? CargoMaxTemperature,
    decimal? BaselineLitresPer100Km,
    double? RoughRoadShare,
    double? IdleHours);

public sealed record FuelComponentDto(string Key, string Label, decimal Litres, decimal Percent, string Detail);

public sealed record FuelEstimateDto(
    Guid? Id,
    Guid? TripId,
    string FuelType,
    double DistanceKm,
    int DurationMinutes,
    int? FreeFlowMinutes,
    double TrafficRatio,
    string TrafficLabel,
    double? AvgAmbientC,
    double? PeakAmbientC,
    bool Refrigerated,
    decimal BaseLitres,
    decimal DriveLitres,
    decimal ReeferLitres,
    decimal IdleLitres,
    decimal TotalLitres,
    decimal RecommendedLitres,
    decimal LitresPer100Km,
    decimal Co2Kg,
    decimal PricePerLitre,
    decimal EstimatedCost,
    decimal RecommendedCost,
    string Currency,
    string Confidence,
    decimal? TankFills,
    IReadOnlyList<FuelComponentDto> Components,
    IReadOnlyList<string> Assumptions,
    DateTimeOffset CreatedAt);

public sealed record RecordFuelRequest(decimal Litres, decimal? Cost, string? Note);

public sealed record TripFuelSummaryDto(
    Guid TripId,
    string TripCode,
    string FuelType,
    decimal? PlannedLitres,
    decimal? PlannedCost,
    decimal? ActualLitres,
    decimal? ActualCost,
    decimal? VarianceLitres,
    decimal? VariancePercent,
    decimal Co2Kg,
    string Currency,
    DateTimeOffset? RecordedAt,
    FuelEstimateDto? Estimate);

public sealed record FuelPriceDto(string FuelType, decimal PricePerLitre, string Currency, string? Source, DateTimeOffset UpdatedAt);

public sealed record UpdateFuelPriceRequest(decimal PricePerLitre, string? Source);

public sealed class FuelEstimateRequestValidator : AbstractValidator<FuelEstimateRequest>
{
    public FuelEstimateRequestValidator()
    {
        RuleFor(x => x).Must(x => x.VehicleId.HasValue || x.CapacityTonnes.HasValue)
            .WithName("CapacityTonnes").WithMessage("Choose a vehicle or give its capacity.");
        RuleFor(x => x).Must(x => x.DistanceKm.HasValue
                                  || (!string.IsNullOrWhiteSpace(x.Origin) && !string.IsNullOrWhiteSpace(x.Destination)))
            .WithName("Origin").WithMessage("Give both pickup and destination, or a distance in kilometres.");
        RuleFor(x => x.CapacityTonnes).InclusiveBetween(0.5m, 60m).When(x => x.CapacityTonnes.HasValue);
        RuleFor(x => x.PayloadTonnes).InclusiveBetween(0m, 60m).When(x => x.PayloadTonnes.HasValue);
        RuleFor(x => x.DistanceKm).InclusiveBetween(1, 5000).When(x => x.DistanceKm.HasValue);
        RuleFor(x => x.Origin).MaximumLength(300);
        RuleFor(x => x.Destination).MaximumLength(300);
        RuleFor(x => x.CargoMaxTemperature).InclusiveBetween(-30m, 60m).When(x => x.CargoMaxTemperature.HasValue);
        RuleFor(x => x.BaselineLitresPer100Km).InclusiveBetween(3m, 120m).When(x => x.BaselineLitresPer100Km.HasValue);
        RuleFor(x => x.RoughRoadShare).InclusiveBetween(0, 1).When(x => x.RoughRoadShare.HasValue);
        RuleFor(x => x.IdleHours).InclusiveBetween(0, 48).When(x => x.IdleHours.HasValue);
    }
}

public sealed class RecordFuelRequestValidator : AbstractValidator<RecordFuelRequest>
{
    public RecordFuelRequestValidator()
    {
        RuleFor(x => x.Litres).GreaterThan(0m).LessThanOrEqualTo(5000m);
        RuleFor(x => x.Cost).GreaterThanOrEqualTo(0m).When(x => x.Cost.HasValue);
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

public sealed class UpdateFuelPriceRequestValidator : AbstractValidator<UpdateFuelPriceRequest>
{
    public UpdateFuelPriceRequestValidator()
    {
        RuleFor(x => x.PricePerLitre).GreaterThan(0m).LessThanOrEqualTo(100_000m);
        RuleFor(x => x.Source).MaximumLength(200);
    }
}

internal static class FuelText
{
    public static string TrafficLabel(double ratio) => ratio switch
    {
        < 1.05 => "Free-flowing",
        < 1.2 => "Light traffic",
        < 1.45 => "Moderate traffic",
        < 1.8 => "Heavy traffic",
        _ => "Severe congestion",
    };

    public static string Money(decimal amount, string currency) =>
        currency == "NGN" ? $"₦{amount:N0}" : $"{amount:N2} {currency}";

    public static string? Trimmed(string? value) => Text.Trimmed(value);
}
