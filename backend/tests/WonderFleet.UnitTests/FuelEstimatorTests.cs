using WonderFleet.Domain.Enums;
using WonderFleet.Domain.Services;
using Xunit;

namespace WonderFleet.UnitTests;

public sealed class FuelEstimatorTests
{
    private static FuelInputs Lagos_To_Kano(
        bool refrigerated = false, int? freeFlow = 720, double? averageC = 31, double? peakC = 36,
        double idleHours = 2, double roughShare = 0.25, decimal payload = 5m) =>
        new(FuelType.Diesel, CapacityTonnes: 7m, PayloadTonnes: payload, DistanceKm: 1000, DurationMinutes: 900,
            FreeFlowDurationMinutes: freeFlow, AverageAmbientC: averageC, PeakAmbientC: peakC,
            CargoMaxTemperature: 8m, Refrigerated: refrigerated, IdleHours: idleHours,
            RoughRoadShare: roughShare, BaselineLitresPer100Km: null, RainExpected: false);

    [Fact]
    public void A_seven_tonne_diesel_truck_uses_a_realistic_amount()
    {
        var plan = FuelEstimator.Estimate(Lagos_To_Kano());

        // ~18 L/100 km baseline over 1,000 km, plus load, traffic, road and heat allowances.
        Assert.InRange(plan.TotalLitres, 200m, 320m);
        Assert.InRange(plan.EffectiveLitresPer100Km, 20m, 32m);
    }

    [Fact]
    public void The_components_add_up_to_the_total()
    {
        var plan = FuelEstimator.Estimate(Lagos_To_Kano(refrigerated: true));

        var sum = plan.Components.Sum(component => component.Litres);
        Assert.InRange(Math.Abs(sum - plan.TotalLitres), 0m, 0.05m);
    }

    [Fact]
    public void The_dispatch_figure_carries_a_margin_over_the_estimate()
    {
        var plan = FuelEstimator.Estimate(Lagos_To_Kano());

        Assert.True(plan.RecommendedLitres > plan.TotalLitres);
        Assert.True(plan.RecommendedLitres <= plan.TotalLitres * 1.2m);
        Assert.Equal(0m, plan.RecommendedLitres % 5m); // rounded to a number a depot can actually issue
    }

    [Fact]
    public void Traffic_increases_consumption()
    {
        var freeFlowing = FuelEstimator.Estimate(Lagos_To_Kano(freeFlow: 900));
        var congested = FuelEstimator.Estimate(Lagos_To_Kano(freeFlow: 600));

        Assert.True(congested.TotalLitres > freeFlowing.TotalLitres);
    }

    [Fact]
    public void A_heavier_load_costs_more_fuel_than_a_light_one()
    {
        var light = FuelEstimator.Estimate(Lagos_To_Kano(payload: 1m));
        var full = FuelEstimator.Estimate(Lagos_To_Kano(payload: 7m));

        Assert.True(full.TotalLitres > light.TotalLitres);
    }

    [Fact]
    public void Heat_adds_fuel_but_is_capped()
    {
        var mild = FuelEstimator.Estimate(Lagos_To_Kano(averageC: 24, peakC: 26));
        var hot = FuelEstimator.Estimate(Lagos_To_Kano(averageC: 38, peakC: 42));
        var absurd = FuelEstimator.Estimate(Lagos_To_Kano(averageC: 55, peakC: 60));

        Assert.True(hot.TotalLitres > mild.TotalLitres);
        Assert.True((absurd.TotalLitres - hot.TotalLitres) / hot.TotalLitres < 0.1m);
    }

    [Fact]
    public void A_reefer_burns_fuel_while_parked_as_well_as_while_driving()
    {
        var dry = FuelEstimator.Estimate(Lagos_To_Kano());
        var chilled = FuelEstimator.Estimate(Lagos_To_Kano(refrigerated: true));
        var chilledWithLongWait = FuelEstimator.Estimate(Lagos_To_Kano(refrigerated: true, idleHours: 8));

        Assert.Equal(0m, dry.ReeferLitres);
        Assert.True(chilled.ReeferLitres > 0);
        Assert.True(chilledWithLongWait.ReeferLitres > chilled.ReeferLitres);
    }

    [Fact]
    public void Confidence_reflects_the_data_that_was_available()
    {
        Assert.Equal("High", FuelEstimator.Estimate(Lagos_To_Kano()).Confidence);
        Assert.Equal("Medium", FuelEstimator.Estimate(Lagos_To_Kano(freeFlow: null)).Confidence);
        Assert.Equal("Low", FuelEstimator.Estimate(Lagos_To_Kano(freeFlow: null, averageC: null, peakC: null)).Confidence);
    }

    [Fact]
    public void Missing_data_still_produces_a_usable_number_with_stated_assumptions()
    {
        var plan = FuelEstimator.Estimate(Lagos_To_Kano(freeFlow: null, averageC: null, peakC: null));

        Assert.True(plan.TotalLitres > 0);
        Assert.Contains(plan.Assumptions, assumption => assumption.Contains("traffic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Petrol_is_costed_and_carbonised_differently_from_diesel()
    {
        var diesel = FuelEstimator.BaselineLitresPer100Km(FuelType.Diesel, 3m);
        var petrol = FuelEstimator.BaselineLitresPer100Km(FuelType.Petrol, 3m);

        Assert.True(petrol > diesel);
        Assert.Equal(FuelEstimator.PetrolCo2PerLitre, FuelEstimator.Co2PerLitre(FuelType.Petrol));
        Assert.Equal(FuelEstimator.DieselCo2PerLitre, FuelEstimator.Co2PerLitre(FuelType.Diesel));
    }

    [Fact]
    public void Cost_uses_the_price_that_was_supplied()
    {
        Assert.Equal(115_000m, FuelEstimator.Cost(100m, 1150m));
    }
}
