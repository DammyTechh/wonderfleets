using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;
using WonderFleet.Domain.Services;
using Xunit;

namespace WonderFleet.UnitTests;

public sealed class ThresholdEvaluatorTests
{
    private static readonly CargoThresholds Limits = new(2, 8, 40, 75);

    private static (IReadOnlyList<ThresholdBreach> Breaches, SensorStatus Status) Evaluate(decimal? temperature, decimal? humidity)
    {
        var breaches = ThresholdEvaluator.Evaluate(temperature, humidity, Limits);
        return (breaches, ThresholdEvaluator.ToSensorStatus(breaches));
    }

    [Fact]
    public void Readings_inside_the_range_are_normal()
    {
        var result = Evaluate(6m, 60m);

        Assert.Equal(SensorStatus.Normal, result.Status);
        Assert.Empty(result.Breaches);
    }

    [Fact]
    public void A_small_overshoot_is_a_warning()
    {
        var result = Evaluate(9.5m, 60m);

        Assert.Equal(SensorStatus.Warning, result.Status);
        Assert.Contains(result.Breaches, b => b.Type == AlertType.TemperatureBreach && b.Severity == AlertSeverity.Warning);
    }

    [Fact]
    public void Beyond_the_critical_margin_is_critical()
    {
        // 3 C above the limit is the critical margin for temperature.
        var result = Evaluate(11.5m, 60m);

        Assert.Equal(SensorStatus.Critical, result.Status);
        Assert.Contains(result.Breaches, b => b.Type == AlertType.TemperatureBreach && b.Severity == AlertSeverity.Critical);
    }

    [Fact]
    public void Humidity_uses_its_own_critical_margin()
    {
        var warning = Evaluate(6m, 80m);
        var critical = Evaluate(6m, 90m);

        Assert.Equal(SensorStatus.Warning, warning.Status);
        Assert.Equal(SensorStatus.Critical, critical.Status);
        Assert.Contains(critical.Breaches, b => b.Type == AlertType.HighHumidity);
    }

    [Fact]
    public void Cold_and_dry_breaches_are_reported_separately()
    {
        var result = Evaluate(0.5m, 30m);

        Assert.Contains(result.Breaches, b => b.Type == AlertType.LowTemperature);
        Assert.Contains(result.Breaches, b => b.Type == AlertType.LowHumidity);
    }

    [Fact]
    public void Missing_readings_produce_no_breach()
    {
        var result = Evaluate(null, null);

        Assert.Empty(result.Breaches);
        Assert.Equal(SensorStatus.Normal, result.Status);
    }
}

public sealed class GeoMathTests
{
    [Fact]
    public void Lagos_to_Kano_is_roughly_eight_hundred_kilometres()
    {
        var km = GeoMath.HaversineMeters(6.5244, 3.3792, 12.0022, 8.5920) / 1000;

        Assert.InRange(km, 820, 870);
    }

    [Theory]
    [InlineData(0, 0)]        // null island: the firmware default
    [InlineData(95, 10)]      // impossible latitude
    [InlineData(10, 200)]     // impossible longitude
    public void Invalid_fixes_are_rejected(double latitude, double longitude)
    {
        Assert.False(GeoMath.IsValidFix(latitude, longitude));
    }

    [Fact]
    public void A_real_Lagos_fix_is_accepted()
    {
        Assert.True(GeoMath.IsValidFix(6.5244, 3.3792));
    }
}

public sealed class EmissionAndSpoilageTests
{
    [Fact]
    public void Emissions_scale_with_distance_and_capacity()
    {
        var small = EmissionCalculator.EstimateKg(100, 3);
        var large = EmissionCalculator.EstimateKg(100, 30);

        Assert.True(large > small);
        Assert.True(small > 0);
    }

    [Fact]
    public void Spoilage_rises_with_critical_readings()
    {
        var clean = SpoilageEstimator.EstimatePercent(1000, warningReadings: 0, criticalReadings: 0);
        var mixed = SpoilageEstimator.EstimatePercent(1000, warningReadings: 100, criticalReadings: 10);
        var bad = SpoilageEstimator.EstimatePercent(1000, warningReadings: 100, criticalReadings: 300);

        Assert.Equal(0m, clean);
        Assert.True(mixed > clean);
        Assert.True(bad > mixed);
    }
}
