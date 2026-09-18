using WonderFleet.Domain.Common;
using WonderFleet.Domain.Enums;
using WonderFleet.Domain.Exceptions;
using WonderFleet.Domain.Services;

namespace WonderFleet.Domain.Entities;

/// A shipment: produce owned by an agro-processor, moved by a logistics partner's vehicle, watched by a device.
public class Trip : AuditableEntity
{
    /// Movement below this distance between two fixes counts as "not moving" for stoppage detection.
    public const double MovementThresholdMeters = 75;

    public string TripCode { get; set; } = default!;
    public Guid VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public Guid? DriverId { get; set; }
    public Driver? Driver { get; set; }
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }
    public Guid LogisticsPartnerId { get; set; }
    public LogisticsPartner? LogisticsPartner { get; set; }
    public Guid AgroProcessorId { get; set; }
    public AgroProcessor? AgroProcessor { get; set; }

    public TripStatus Status { get; private set; } = TripStatus.Scheduled;
    public SensorStatus SensorStatus { get; set; } = SensorStatus.Offline;

    public decimal EstimatedWeightTonnes { get; set; }
    public PackagingType? PackagingType { get; set; }
    public int? UnitCount { get; set; }
    public string? AdditionalNotes { get; set; }

    public string PickupAddress { get; set; } = default!;
    public string OriginLabel { get; set; } = default!;
    public double? PickupLatitude { get; set; }
    public double? PickupLongitude { get; set; }
    public string DestinationAddress { get; set; } = default!;
    public string DestinationLabel { get; set; } = default!;
    public double? DestinationLatitude { get; set; }
    public double? DestinationLongitude { get; set; }

    public DateTimeOffset LoadingTime { get; set; }
    public DateTimeOffset ExpectedArrival { get; set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public decimal MinTemperature { get; private set; }
    public decimal MaxTemperature { get; private set; }
    public decimal MinHumidity { get; private set; }
    public decimal MaxHumidity { get; private set; }

    public double? LastLatitude { get; private set; }
    public double? LastLongitude { get; private set; }
    public double? LastSpeedKmh { get; private set; }
    public DateTimeOffset? LastPositionAt { get; private set; }
    public DateTimeOffset? LastMovedAt { get; private set; }
    public double DistanceTravelledKm { get; private set; }
    public double? PlannedDistanceKm { get; set; }
    public decimal Co2EmissionKg { get; set; }

    // ---- fuel planning (what dispatch sends out) and reconciliation (what was really burnt)
    public decimal? PlannedFuelLitres { get; set; }
    public decimal? PlannedFuelCost { get; set; }
    public decimal? ActualFuelLitres { get; set; }
    public decimal? ActualFuelCost { get; set; }
    public DateTimeOffset? FuelRecordedAt { get; set; }
    public decimal? LastTemperature { get; private set; }
    public decimal? LastHumidity { get; private set; }

    public Guid? CreatedByAdminId { get; set; }
    public uint Version { get; set; }

    public List<TripProduce> Produce { get; } = [];

    public bool IsEnded => Status is TripStatus.Completed or TripStatus.Cancelled;
    public bool IsMoving => Status is TripStatus.InTransit or TripStatus.Delayed or TripStatus.Stopped;
    public CargoThresholds Thresholds => new(MinTemperature, MaxTemperature, MinHumidity, MaxHumidity);

    public void SetThresholds(CargoThresholds thresholds)
    {
        thresholds.EnsureValid();
        MinTemperature = thresholds.MinTemperature;
        MaxTemperature = thresholds.MaxTemperature;
        MinHumidity = thresholds.MinHumidity;
        MaxHumidity = thresholds.MaxHumidity;
    }

    public void Start(DateTimeOffset now)
    {
        if (Status != TripStatus.Scheduled)
            throw new DomainException("trip.invalid_transition", $"A {Status} trip cannot be started.");
        Status = TripStatus.InTransit;
        StartedAt = now;
        LastMovedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        if (!IsMoving)
            throw new DomainException("trip.invalid_transition", $"A {Status} trip cannot be completed.");
        Status = TripStatus.Completed;
        CompletedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (IsEnded)
            throw new DomainException("trip.invalid_transition", "The trip has already ended.");
        Status = TripStatus.Cancelled;
        CancelledAt = now;
    }

    /// Applies a telemetry fix. Returns the distance moved (km) since the previous fix.
    public double ApplyTelemetry(decimal? temperature, decimal? humidity, double? lat, double? lng, DateTimeOffset at)
    {
        if (temperature.HasValue) LastTemperature = temperature;
        if (humidity.HasValue) LastHumidity = humidity;
        if (lat is null || lng is null) return 0;

        double movedKm = 0;
        if (LastLatitude.HasValue && LastLongitude.HasValue && LastPositionAt.HasValue)
        {
            var meters = GeoMath.HaversineMeters(LastLatitude.Value, LastLongitude.Value, lat.Value, lng.Value);
            var hours = (at - LastPositionAt.Value).TotalHours;
            LastSpeedKmh = hours > 0 ? Math.Round(meters / 1000 / hours, 1) : LastSpeedKmh;
            if (meters >= MovementThresholdMeters)
            {
                movedKm = meters / 1000;
                LastMovedAt = at;
            }
        }
        else
        {
            LastMovedAt ??= at;
        }

        if (IsMoving) DistanceTravelledKm += movedKm;
        LastLatitude = lat;
        LastLongitude = lng;
        LastPositionAt = at;

        // A stopped truck that moves again resumes transit; delays are recalculated by the monitor.
        if (Status == TripStatus.Stopped && movedKm > 0) Status = TripStatus.InTransit;
        return movedKm;
    }

    /// Records what the trip actually burnt. A real fuel figure gives a better CO2 number
    /// than any per-kilometre average, so emissions are recomputed from it.
    public void RecordFuelActuals(decimal litres, decimal? cost, FuelType fuelType, DateTimeOffset now)
    {
        if (litres <= 0) throw new DomainException("fuel.invalid", "Fuel used must be greater than zero.");
        if (litres > 5000) throw new DomainException("fuel.invalid", "That fuel figure looks wrong. Enter the litres for this trip only.");
        if (cost is < 0) throw new DomainException("fuel.invalid", "Fuel cost cannot be negative.");

        ActualFuelLitres = litres;
        ActualFuelCost = cost;
        FuelRecordedAt = now;
        Co2EmissionKg = EmissionCalculator.EstimateFromFuel(litres, fuelType);
    }

    public void MarkStopped()
    {
        if (Status is TripStatus.InTransit or TripStatus.Delayed) Status = TripStatus.Stopped;
    }

    /// Moves the ETA. A delayed trip whose new ETA is still ahead goes back to in-transit.
    public void Reschedule(DateTimeOffset expectedArrival, DateTimeOffset now)
    {
        if (IsEnded)
            throw new DomainException("trip.invalid_transition", "An ended trip cannot be rescheduled.");
        if (expectedArrival <= LoadingTime)
            throw new DomainException("trip.schedule", "Expected arrival must be after loading time.");
        ExpectedArrival = expectedArrival;
        if (Status == TripStatus.Delayed && expectedArrival > now) Status = TripStatus.InTransit;
    }

    public void MarkDelayed(DateTimeOffset now)
    {
        if (Status == TripStatus.InTransit && now > ExpectedArrival) Status = TripStatus.Delayed;
    }
}

public class TripProduce
{
    public Guid TripId { get; set; }
    public Guid ProduceTypeId { get; set; }
    public ProduceType? ProduceType { get; set; }
}

public readonly record struct CargoThresholds(decimal MinTemperature, decimal MaxTemperature, decimal MinHumidity, decimal MaxHumidity)
{
    public void EnsureValid()
    {
        if (MinTemperature >= MaxTemperature)
            throw new DomainException("thresholds.temperature", "Minimum temperature must be lower than maximum temperature.");
        if (MinHumidity >= MaxHumidity)
            throw new DomainException("thresholds.humidity", "Minimum humidity must be lower than maximum humidity.");
        if (MinTemperature < -30 || MaxTemperature > 80)
            throw new DomainException("thresholds.temperature", "Temperature range must be within -30°C and 80°C.");
        if (MinHumidity < 0 || MaxHumidity > 100)
            throw new DomainException("thresholds.humidity", "Humidity range must be within 0% and 100%.");
    }
}
