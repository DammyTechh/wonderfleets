namespace WonderFleet.Application.Features.Tracking;

public sealed record StatusCountsDto(int Live, int InTransit, int Stopped, int Delay);

public sealed record DeviceSummaryDto(int Normal, int Warning, int Critical, int Offline);

public sealed record RecentPositionDto(string VehicleCode, string Route, double? SpeedKmh, DateTimeOffset RecordedAt);

public sealed record GpsMonitorDto(int ActiveSignals, DateTimeOffset ServerTimeUtc, string LocalTime, string TimeZone, IReadOnlyList<RecentPositionDto> RecentPositions);

public sealed record MapMarkerDto(
    Guid TripId, string TripCode, string FleetNumber, string VehicleCode, double Latitude, double Longitude,
    string TripStatus, string SensorStatus, string? DriverName, string Route,
    decimal? Temperature, decimal? Humidity, double? SpeedKmh, DateTimeOffset? LastPositionAt);

public sealed record AlertTriggerDto(Guid Id, string Title, string Severity, string? VehicleCode, string? DeviceSerial, string Route, DateTimeOffset At);

public sealed record FleetClimateDto(decimal? AvgTemperature, string TemperatureState, decimal? AvgHumidity, string HumidityState);

public sealed record LiveTrackingDto(
    IReadOnlyList<MapMarkerDto> Markers,
    StatusCountsDto StatusCounts,
    DeviceSummaryDto DeviceSummary,
    FleetClimateDto Climate,
    IReadOnlyList<AlertTriggerDto> AlertTriggers,
    GpsMonitorDto GpsMonitor,
    SyncStatusDto Sync);

public sealed record SyncStatusDto(bool AllTransmitting, int OnlineDevices, int ExpectedDevices, DateTimeOffset? LastSyncAt, int SecondsSinceSync, string Message);

public sealed record DriverPoolItemDto(Guid Id, string FullName, string Initials, string? Assignment, string Status);

public sealed record DriverPoolDto(int OnTrip, int Available, int OffDuty, IReadOnlyList<DriverPoolItemDto> Drivers);
