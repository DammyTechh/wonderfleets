using WonderFleet.Domain.Enums;

namespace WonderFleet.Domain.Services;

/// Everything the model needs about one journey. Unknowns are nullable and lower the confidence
/// rather than blocking the estimate — dispatch still needs a number.
public sealed record FuelInputs(
    FuelType FuelType,
    decimal CapacityTonnes,
    decimal PayloadTonnes,
    double DistanceKm,
    int DurationMinutes,
    int? FreeFlowDurationMinutes,
    double? AverageAmbientC,
    double? PeakAmbientC,
    decimal? CargoMaxTemperature,
    bool Refrigerated,
    double IdleHours,
    double RoughRoadShare,
    decimal? BaselineLitresPer100Km,
    bool RainExpected);

public sealed record FuelComponent(string Key, string Label, decimal Litres, string Detail);

public sealed record FuelPlan(
    decimal BaseLitres,
    decimal DriveLitres,
    decimal ReeferLitres,
    decimal IdleLitres,
    decimal TotalLitres,
    decimal RecommendedLitres,
    decimal EffectiveLitresPer100Km,
    decimal Co2Kg,
    double TrafficRatio,
    string Confidence,
    IReadOnlyList<FuelComponent> Components,
    IReadOnlyList<string> Assumptions);

/// Physically-motivated fuel model for Nigerian agri-haulage.
///
/// Consumption is built up from a baseline for the vehicle class and then adjusted by the
/// things that actually move the needle on these corridors: how heavily the truck is loaded,
/// how much of the journey is stop-go traffic, how rough the road is, how hot it is (cab air
/// conditioning and engine cooling), rain, and — for chilled produce — the reefer unit, which
/// burns fuel per hour rather than per kilometre and keeps burning it while the truck is parked.
///
/// Every factor is decomposed into litres so dispatch can see exactly where the fuel goes.
public static class FuelEstimator
{
    // Litres of fuel per kg of CO2, tank-to-wheel (IPCC road-transport defaults).
    public const decimal DieselCo2PerLitre = 2.68m;
    public const decimal PetrolCo2PerLitre = 2.31m;

    /// Extra fuel loaded so a driver is never stranded by traffic or a detour.
    public const decimal DispatchMargin = 0.10m;

    private const double MaxTrafficRatio = 2.2;
    private const double TrafficSensitivity = 0.45;   // stop-go penalty per unit of delay ratio
    private const double PayloadSensitivity = 0.22;   // empty → fully laden
    private const double RoughRoadPenalty = 0.15;     // fully unpaved versus good asphalt
    private const double HeatPenaltyPerDegree = 0.012;
    private const double MaxHeatPenalty = 0.10;
    private const double RainPenalty = 0.03;

    /// Litres per 100 km for a laden-but-unadjusted vehicle of this class.
    ///
    /// Consumption rises with size but flattens out: a 30 t articulated truck does not burn four
    /// times what a 7 t rigid burns. The curve below tracks published haulage figures — roughly
    /// 15 L/100 km for a light van, 18 for a 7 t rigid, 26 for a 20 t truck and 30 for a 30 t artic.
    public static decimal BaselineLitresPer100Km(FuelType fuelType, decimal capacityTonnes)
    {
        var capacity = Math.Clamp(capacityTonnes, 0.5m, 60m);
        var baseline = capacity switch
        {
            <= 3.5m => 10m + 1.4m * capacity,                 // light van
            <= 10m => 9m + 1.25m * capacity,                  // rigid truck
            _ => 21.5m + 0.42m * (capacity - 10m),            // heavy rigid and articulated
        };
        // Petrol engines burn more fuel for the same transport work.
        if (fuelType == FuelType.Petrol) baseline *= capacity <= 3.5m ? 1.12m : 1.15m;
        return Math.Round(baseline, 2);
    }

    public static decimal Co2PerLitre(FuelType fuelType) =>
        fuelType == FuelType.Petrol ? PetrolCo2PerLitre : DieselCo2PerLitre;

    public static FuelPlan Estimate(FuelInputs input)
    {
        var assumptions = new List<string>();
        var distance = Math.Max(0, input.DistanceKm);
        var capacity = Math.Max(0.5m, input.CapacityTonnes);

        var baselinePer100 = input.BaselineLitresPer100Km ?? BaselineLitresPer100Km(input.FuelType, capacity);
        if (input.BaselineLitresPer100Km is null)
            assumptions.Add($"Baseline {baselinePer100:0.#} L/100 km assumed for a {capacity:0.#} t {input.FuelType.ToString().ToLowerInvariant()} vehicle. Record the vehicle's real figure for a tighter estimate.");

        var baseLitres = (decimal)distance / 100m * baselinePer100;

        // ---- multiplicative factors, decomposed into litres so each one is visible
        var loadRatio = Math.Clamp((double)(input.PayloadTonnes / capacity), 0, 1);
        var payloadFactor = 1 + PayloadSensitivity * loadRatio;

        var trafficRatio = input.FreeFlowDurationMinutes is > 0 && input.DurationMinutes > 0
            ? Math.Clamp(input.DurationMinutes / (double)input.FreeFlowDurationMinutes.Value, 1, MaxTrafficRatio)
            : 1;
        var trafficFactor = 1 + TrafficSensitivity * (trafficRatio - 1);
        if (input.FreeFlowDurationMinutes is null or <= 0)
            assumptions.Add("No live traffic data was available, so free-flowing roads were assumed.");

        var roughShare = Math.Clamp(input.RoughRoadShare, 0, 1);
        var roadFactor = 1 + RoughRoadPenalty * roughShare;

        var ambient = input.AverageAmbientC;
        var heatFactor = ambient is { } temp && temp > 28
            ? 1 + Math.Min(MaxHeatPenalty, HeatPenaltyPerDegree * (temp - 28))
            : 1;
        if (ambient is null) assumptions.Add("No forecast was available, so no heat allowance was added.");

        var rainFactor = input.RainExpected ? 1 + RainPenalty : 1;

        var payloadLitres = baseLitres * (decimal)(payloadFactor - 1);
        var afterPayload = baseLitres + payloadLitres;
        var trafficLitres = afterPayload * (decimal)(trafficFactor - 1);
        var afterTraffic = afterPayload + trafficLitres;
        var roadLitres = afterTraffic * (decimal)(roadFactor - 1);
        var afterRoad = afterTraffic + roadLitres;
        var heatLitres = afterRoad * (decimal)(heatFactor - 1);
        var afterHeat = afterRoad + heatLitres;
        var rainLitres = afterHeat * (decimal)(rainFactor - 1);
        var driveLitres = afterHeat + rainLitres;

        // ---- hourly burners: the reefer unit and idling
        var drivingHours = input.DurationMinutes > 0 ? input.DurationMinutes / 60d : distance / 45d;
        var idleHours = Math.Max(0, input.IdleHours);

        var reeferLitres = 0m;
        if (input.Refrigerated)
        {
            var setPoint = input.CargoMaxTemperature ?? 8m;
            var outside = (decimal)(input.PeakAmbientC ?? input.AverageAmbientC ?? 32);
            var lift = Math.Clamp(outside - setPoint, 0m, 35m);
            if (lift > 0)
            {
                // Reefer burn grows with the temperature lift it has to hold and with box size.
                var litresPerHour = 1.2m + 0.05m * lift + 0.04m * capacity;
                reeferLitres = litresPerHour * (decimal)(drivingHours + idleHours);
                assumptions.Add($"Reefer holding {lift:0.#} °C below ambient for {drivingHours + idleHours:0.#} h at {litresPerHour:0.0} L/h.");
            }
        }

        var idleLitresPerHour = 0.6m + 0.045m * capacity;
        var idleLitres = idleLitresPerHour * (decimal)idleHours;

        var total = driveLitres + reeferLitres + idleLitres;
        var recommended = RoundUpTo5(total * (1 + DispatchMargin));

        var confidence = (input.AverageAmbientC, input.FreeFlowDurationMinutes) switch
        {
            (not null, > 0) => "High",
            (null, null or <= 0) => "Low",
            _ => "Medium",
        };

        var components = new List<FuelComponent>
        {
            new("base", "Base consumption", Round(baseLitres), $"{distance:0} km at {baselinePer100:0.#} L/100 km"),
            new("payload", "Payload", Round(payloadLitres), $"{input.PayloadTonnes:0.#} t of {capacity:0.#} t capacity"),
            new("traffic", "Traffic", Round(trafficLitres), trafficRatio > 1.01 ? $"{(trafficRatio - 1) * 100:0}% slower than free-flowing" : "Free-flowing"),
            new("road", "Road condition", Round(roadLitres), $"{roughShare * 100:0}% of the route on rough surface"),
            new("heat", "Heat (cab and engine)", Round(heatLitres), ambient is { } a ? $"Average {a:0.#} °C along the route" : "No forecast"),
            new("rain", "Rain", Round(rainLitres), input.RainExpected ? "Wet roads expected" : "Dry"),
            new("reefer", "Cooling unit", Round(reeferLitres), input.Refrigerated ? "Runs while driving and while parked" : "Not a chilled load"),
            new("idle", "Idling and loading", Round(idleLitres), $"{idleHours:0.#} h at {idleLitresPerHour:0.0} L/h"),
        };

        assumptions.Add($"A {DispatchMargin * 100:0}% margin is added to the dispatch figure for detours and queues.");

        return new FuelPlan(
            Round(baseLitres), Round(driveLitres), Round(reeferLitres), Round(idleLitres), Round(total), recommended,
            distance > 0 ? Math.Round(total / (decimal)distance * 100m, 2) : 0,
            Math.Round(total * Co2PerLitre(input.FuelType), 2),
            Math.Round(trafficRatio, 2),
            confidence,
            components.Where(component => component.Litres > 0 || component.Key == "base").ToList(),
            assumptions);
    }

    public static decimal Cost(decimal litres, decimal pricePerLitre) => Math.Round(litres * pricePerLitre, 2);

    private static decimal Round(decimal value) => Math.Round(value, 2);

    private static decimal RoundUpTo5(decimal value) => Math.Ceiling(value / 5m) * 5m;
}
