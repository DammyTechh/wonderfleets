namespace WonderFleet.Domain.Services;

/// Tank-to-wheel CO2 estimate for diesel trucks. Factors are prototype defaults (kg CO2 per km)
/// and should be replaced with fuel-log data when available.
public static class EmissionCalculator
{
    public static decimal FactorForCapacity(decimal capacityTonnes) => capacityTonnes switch
    {
        <= 3.5m => 0.35m,
        <= 7.5m => 0.62m,
        <= 12m => 0.82m,
        <= 20m => 0.95m,
        _ => 1.10m,
    };

    public static decimal EstimateKg(double distanceKm, decimal capacityTonnes) =>
        Math.Round((decimal)distanceKm * FactorForCapacity(capacityTonnes), 2);

    /// Preferred once fuel is known: burning a litre of fuel emits a fixed amount of CO2,
    /// so a fuel log is a far better emission figure than a per-kilometre average.
    public static decimal EstimateFromFuel(decimal litres, Enums.FuelType fuelType) =>
        Math.Round(litres * FuelEstimator.Co2PerLitre(fuelType), 2);
}

/// Heuristic spoilage exposure: share of monitored time spent outside the safe range,
/// weighted double when critical. Presented in the UI as an estimate.
public static class SpoilageEstimator
{
    public static decimal EstimatePercent(int totalReadings, int warningReadings, int criticalReadings)
    {
        if (totalReadings <= 0) return 0;
        var weighted = warningReadings * 0.5m + criticalReadings * 1.0m;
        return Math.Round(Math.Min(100m, weighted / totalReadings * 10m), 1);
    }
}
