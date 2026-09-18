using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Domain.Services;

public sealed record ThresholdBreach(AlertType Type, AlertSeverity Severity, decimal Value, decimal Threshold);

/// Pure rule engine. Out of range = Warning; beyond the range by more than the critical margin = Critical.
/// Prototype tolerance follows the concept note: values are treated as ranges, not single points.
public static class ThresholdEvaluator
{
    public const decimal CriticalTemperatureMargin = 3m;
    public const decimal CriticalHumidityMargin = 10m;

    public static IReadOnlyList<ThresholdBreach> Evaluate(decimal? temperature, decimal? humidity, CargoThresholds t)
    {
        var breaches = new List<ThresholdBreach>(2);

        if (temperature is { } temp)
        {
            if (temp > t.MaxTemperature)
                breaches.Add(new(AlertType.TemperatureBreach, Grade(temp - t.MaxTemperature, CriticalTemperatureMargin), temp, t.MaxTemperature));
            else if (temp < t.MinTemperature)
                breaches.Add(new(AlertType.LowTemperature, Grade(t.MinTemperature - temp, CriticalTemperatureMargin), temp, t.MinTemperature));
        }

        if (humidity is { } hum)
        {
            if (hum > t.MaxHumidity)
                breaches.Add(new(AlertType.HighHumidity, Grade(hum - t.MaxHumidity, CriticalHumidityMargin), hum, t.MaxHumidity));
            else if (hum < t.MinHumidity)
                breaches.Add(new(AlertType.LowHumidity, Grade(t.MinHumidity - hum, CriticalHumidityMargin), hum, t.MinHumidity));
        }

        return breaches;
    }

    public static SensorStatus ToSensorStatus(IReadOnlyList<ThresholdBreach> breaches) =>
        breaches.Count == 0 ? SensorStatus.Normal
        : breaches.Any(b => b.Severity == AlertSeverity.Critical) ? SensorStatus.Critical
        : SensorStatus.Warning;

    private static AlertSeverity Grade(decimal overshoot, decimal criticalMargin) =>
        overshoot > criticalMargin ? AlertSeverity.Critical : AlertSeverity.Warning;
}
