using WonderFleet.Domain.Common;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Domain.Entities;

/// Pump price per fuel, maintained by an administrator. Prices in Nigeria move often,
/// so every estimate stores the price it used rather than recomputing historic costs.
public class FuelPrice
{
    public FuelType FuelType { get; set; }
    public decimal PricePerLitre { get; set; }
    public string Currency { get; set; } = "NGN";
    public string? Source { get; set; }
    public Guid? UpdatedByAdminId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// A stored fuel plan for a shipment: what was estimated, from which inputs, at which price.
/// Kept as history so dispatch decisions remain auditable after prices or routes change.
public class FuelEstimate : Entity
{
    public Guid? TripId { get; set; }
    public Guid? VehicleId { get; set; }
    public FuelType FuelType { get; set; }

    public double DistanceKm { get; set; }
    public int DurationMinutes { get; set; }
    public int? FreeFlowMinutes { get; set; }
    public double TrafficRatio { get; set; } = 1;
    public double? AvgAmbientC { get; set; }
    public double? PeakAmbientC { get; set; }
    public bool Refrigerated { get; set; }

    public decimal BaseLitres { get; set; }
    public decimal DriveLitres { get; set; }
    public decimal ReeferLitres { get; set; }
    public decimal IdleLitres { get; set; }
    public decimal TotalLitres { get; set; }
    public decimal RecommendedLitres { get; set; }
    public decimal LitresPer100Km { get; set; }
    public decimal Co2Kg { get; set; }

    public decimal PricePerLitre { get; set; }
    public decimal EstimatedCost { get; set; }
    public string Currency { get; set; } = "NGN";

    public string Confidence { get; set; } = "Medium";
    /// Component breakdown and assumptions, exactly as shown to the operator.
    public string BreakdownJson { get; set; } = "[]";
    public string AssumptionsJson { get; set; } = "[]";

    public Guid? CreatedByAdminId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
