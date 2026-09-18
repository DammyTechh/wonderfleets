using WonderFleet.Domain.Enums;

namespace WonderFleet.Domain.Entities;

/// Append-only time-series row. Uses a bigint identity key for insert speed.
public class SensorReading
{
    public long Id { get; set; }
    public Guid DeviceId { get; set; }
    public Guid? TripId { get; set; }
    public Guid? VehicleId { get; set; }
    public decimal? Temperature { get; set; }
    public decimal? Humidity { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? SpeedKmh { get; set; }
    public int? BatteryLevel { get; set; }
    public bool IsActive { get; set; }
    public TelemetrySource Source { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
