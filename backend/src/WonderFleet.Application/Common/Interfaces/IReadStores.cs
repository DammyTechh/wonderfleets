using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Common.Interfaces;

public sealed record TimeBucketAverage(DateTimeOffset Bucket, decimal? AvgTemperature, decimal? AvgHumidity, decimal? AvgMaxTemperature, int Readings);
public sealed record ComplianceCounts(int Total, int TemperatureInRange, int HumidityInRange, int Warning, int Critical);
public sealed record PartnerTripCount(Guid PartnerId, string PartnerName, string PartnerCode, int Trips);
public sealed record AlertActivityCell(DateOnly Day, string Category, int Count, string WorstSeverity);
public sealed record DailyConditionStats(
    DateOnly Day, int Readings, decimal? AvgTemperature, decimal? MinTemperature, decimal? MaxTemperature,
    decimal? AvgHumidity, decimal? MinHumidity, decimal? MaxHumidity, int TemperatureBreaches, int HumidityBreaches);
public sealed record TripCompliance(
    Guid TripId, string TripCode, string FleetNumber, string Route, string PartnerName, string ProcessorName,
    int Readings, decimal TemperatureCompliancePct, decimal HumidityCompliancePct, int Alerts, int CriticalAlerts);

/// Heavy aggregate queries implemented with hand-tuned SQL in Infrastructure.
public interface IAnalyticsReadStore
{
    Task<IReadOnlyList<TimeBucketAverage>> GetAveragesAsync(DateTimeOffset from, DateTimeOffset to, string bucket, Guid? deviceId, CancellationToken ct);
    Task<ComplianceCounts> GetComplianceAsync(DateTimeOffset from, DateTimeOffset to, Guid? deviceId, CancellationToken ct);
    Task<IReadOnlyList<PartnerTripCount>> GetTripsByPartnerAsync(DateTimeOffset from, DateTimeOffset to, int top, CancellationToken ct);
    Task<IReadOnlyList<AlertActivityCell>> GetAlertActivityAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<double> GetSensorUptimePercentAsync(DateTimeOffset from, DateTimeOffset to, Guid? deviceId, CancellationToken ct);
    Task<IReadOnlyList<DailyConditionStats>> GetDailyStatsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<IReadOnlyList<TripCompliance>> GetTripComplianceAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed record ReportTable(string Title, string Subtitle, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<KeyValuePair<string, string>> Highlights);

public sealed record ReportFile(byte[] Content, string ContentType, string FileName);

public interface IReportRenderer
{
    ReportFile RenderPdf(ReportTable table, string fileName);
    ReportFile RenderCsv(ReportTable table, string fileName);
}
