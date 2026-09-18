using Npgsql;
using NpgsqlTypes;
using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Infrastructure.Persistence;

/// Aggregate queries for analytics and reports. Hand-written SQL keeps them index-friendly
/// (BRIN on recorded_at, trip/time composite indexes) and avoids loading raw telemetry into memory.
internal sealed class AnalyticsReadStore(NpgsqlDataSource dataSource) : IAnalyticsReadStore
{
    private static readonly HashSet<string> Buckets = ["hour", "day", "week", "month"];

    internal const string AveragesSql = """
        SELECT date_trunc(@bucket, r.recorded_at AT TIME ZONE 'Africa/Lagos') AT TIME ZONE 'Africa/Lagos' AS bucket,
               avg(r.temperature)   AS avg_temperature,
               avg(r.humidity)      AS avg_humidity,
               avg(t.max_temperature) AS avg_max_temperature,
               count(*)::int        AS readings
        FROM sensor_readings r
        JOIN trips t ON t.id = r.trip_id
        WHERE r.recorded_at >= @from AND r.recorded_at < @to
          AND (@device::uuid IS NULL OR r.device_id = @device::uuid)
        GROUP BY 1
        ORDER BY 1
        """;

    internal const string ComplianceSql = """
        SELECT count(*)::int AS total,
               count(*) FILTER (WHERE r.temperature BETWEEN t.min_temperature AND t.max_temperature)::int AS temp_ok,
               count(*) FILTER (WHERE r.humidity BETWEEN t.min_humidity AND t.max_humidity)::int AS hum_ok,
               count(*) FILTER (WHERE (r.temperature NOT BETWEEN t.min_temperature AND t.max_temperature
                                       OR r.humidity NOT BETWEEN t.min_humidity AND t.max_humidity)
                                  AND NOT (r.temperature > t.max_temperature + 3 OR r.temperature < t.min_temperature - 3
                                        OR r.humidity > t.max_humidity + 10 OR r.humidity < t.min_humidity - 10))::int AS warning,
               count(*) FILTER (WHERE r.temperature > t.max_temperature + 3 OR r.temperature < t.min_temperature - 3
                                   OR r.humidity > t.max_humidity + 10 OR r.humidity < t.min_humidity - 10)::int AS critical
        FROM sensor_readings r
        JOIN trips t ON t.id = r.trip_id
        WHERE r.recorded_at >= @from AND r.recorded_at < @to
          AND r.temperature IS NOT NULL AND r.humidity IS NOT NULL
          AND (@device::uuid IS NULL OR r.device_id = @device::uuid)
        """;

    internal const string TripsByPartnerSql = """
        SELECT p.id, p.company_name, p.partner_code, count(t.id)::int AS trips
        FROM trips t
        JOIN logistics_partners p ON p.id = t.logistics_partner_id
        WHERE t.loading_time >= @from AND t.loading_time < @to AND t.status <> 'Cancelled'
        GROUP BY p.id, p.company_name, p.partner_code
        ORDER BY trips DESC, p.company_name
        LIMIT @take
        """;

    internal const string AlertActivitySql = """
        SELECT (a.triggered_at AT TIME ZONE 'Africa/Lagos')::date AS day,
               CASE WHEN a.alert_type IN ('TemperatureBreach','LowTemperature') THEN 'Temp'
                    WHEN a.alert_type IN ('HighHumidity','LowHumidity') THEN 'Hum'
                    WHEN a.alert_type IN ('Stoppage','Delay') THEN 'Route'
                    ELSE 'Device' END AS category,
               count(*)::int AS alerts,
               CASE max(CASE a.severity WHEN 'Critical' THEN 3 WHEN 'Warning' THEN 2 ELSE 1 END)
                    WHEN 3 THEN 'Critical' WHEN 2 THEN 'Warning' ELSE 'Informational' END AS worst
        FROM alerts a
        WHERE a.triggered_at >= @from AND a.triggered_at < @to
        GROUP BY 1, 2
        ORDER BY 1, 2
        """;

    /// Share of 5-minute slots with at least one active reading while a device was on a moving trip.
    internal const string UptimeSql = """
        WITH active AS (
            SELECT t.id,
                   greatest(t.started_at, @from) AS s,
                   least(coalesce(t.completed_at, t.cancelled_at, now()), @to) AS e
            FROM trips t
            WHERE t.device_id IS NOT NULL AND t.started_at IS NOT NULL
              AND t.started_at < @to AND coalesce(t.completed_at, t.cancelled_at, now()) > @from
              AND (@device::uuid IS NULL OR t.device_id = @device::uuid)
        ), expected AS (
            SELECT coalesce(sum(greatest(0, floor(extract(epoch FROM (e - s)) / 300))), 0)::float8 AS n FROM active
        ), seen AS (
            SELECT count(DISTINCT (r.trip_id, date_bin('5 minutes', r.recorded_at, timestamptz '2000-01-01')))::float8 AS n
            FROM sensor_readings r
            JOIN active a ON a.id = r.trip_id
            WHERE r.recorded_at >= a.s AND r.recorded_at < a.e AND r.is_active
        )
        SELECT CASE WHEN expected.n > 0 THEN least(100, seen.n / expected.n * 100) ELSE 100 END
        FROM expected, seen
        """;

    internal const string DailyStatsSql = """
        SELECT (r.recorded_at AT TIME ZONE 'Africa/Lagos')::date AS day,
               count(*)::int AS readings,
               avg(r.temperature), min(r.temperature), max(r.temperature),
               avg(r.humidity), min(r.humidity), max(r.humidity),
               count(*) FILTER (WHERE r.temperature NOT BETWEEN t.min_temperature AND t.max_temperature)::int AS temp_breaches,
               count(*) FILTER (WHERE r.humidity NOT BETWEEN t.min_humidity AND t.max_humidity)::int AS hum_breaches
        FROM sensor_readings r
        JOIN trips t ON t.id = r.trip_id
        WHERE r.recorded_at >= @from AND r.recorded_at < @to
        GROUP BY 1
        ORDER BY 1
        """;

    internal const string TripComplianceSql = """
        SELECT t.id, t.trip_code, v.fleet_number, t.origin_label || ' → ' || t.destination_label AS route,
               p.company_name, ap.name,
               count(r.id)::int AS readings,
               coalesce(round(100.0 * count(r.id) FILTER (WHERE r.temperature BETWEEN t.min_temperature AND t.max_temperature)
                              / nullif(count(r.id), 0), 1), 0) AS temp_pct,
               coalesce(round(100.0 * count(r.id) FILTER (WHERE r.humidity BETWEEN t.min_humidity AND t.max_humidity)
                              / nullif(count(r.id), 0), 1), 0) AS hum_pct,
               (SELECT count(*) FROM alerts a WHERE a.trip_id = t.id)::int AS alerts,
               (SELECT count(*) FROM alerts a WHERE a.trip_id = t.id AND a.severity = 'Critical')::int AS critical
        FROM trips t
        JOIN vehicles v ON v.id = t.vehicle_id
        JOIN logistics_partners p ON p.id = t.logistics_partner_id
        JOIN agro_processors ap ON ap.id = t.agro_processor_id
        LEFT JOIN sensor_readings r ON r.trip_id = t.id
             AND r.recorded_at >= @from AND r.recorded_at < @to
             AND r.temperature IS NOT NULL AND r.humidity IS NOT NULL
        WHERE t.started_at IS NOT NULL AND t.started_at < @to
          AND coalesce(t.completed_at, t.cancelled_at, 'infinity'::timestamptz) >= @from
        GROUP BY t.id, v.fleet_number, p.company_name, ap.name
        ORDER BY t.started_at
        LIMIT 5000
        """;

    public async Task<IReadOnlyList<TimeBucketAverage>> GetAveragesAsync(DateTimeOffset from, DateTimeOffset to, string bucket, Guid? deviceId, CancellationToken ct)
    {
        if (!Buckets.Contains(bucket)) throw new ArgumentOutOfRangeException(nameof(bucket));
        await using var cmd = Command(AveragesSql, from, to);
        cmd.Parameters.AddWithValue("bucket", bucket);
        AddDevice(cmd, deviceId);
        var list = new List<TimeBucketAverage>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new TimeBucketAverage(r.GetFieldValue<DateTimeOffset>(0), Dec(r, 1), Dec(r, 2), Dec(r, 3), r.GetInt32(4)));
        return list;
    }

    public async Task<ComplianceCounts> GetComplianceAsync(DateTimeOffset from, DateTimeOffset to, Guid? deviceId, CancellationToken ct)
    {
        await using var cmd = Command(ComplianceSql, from, to);
        AddDevice(cmd, deviceId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new ComplianceCounts(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4))
            : new ComplianceCounts(0, 0, 0, 0, 0);
    }

    public async Task<IReadOnlyList<PartnerTripCount>> GetTripsByPartnerAsync(DateTimeOffset from, DateTimeOffset to, int top, CancellationToken ct)
    {
        await using var cmd = Command(TripsByPartnerSql, from, to);
        cmd.Parameters.AddWithValue("take", Math.Clamp(top, 1, 50));
        var list = new List<PartnerTripCount>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new PartnerTripCount(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));
        return list;
    }

    public async Task<IReadOnlyList<AlertActivityCell>> GetAlertActivityAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var cmd = Command(AlertActivitySql, from, to);
        var list = new List<AlertActivityCell>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new AlertActivityCell(r.GetFieldValue<DateOnly>(0), r.GetString(1), r.GetInt32(2), r.GetString(3)));
        return list;
    }

    public async Task<double> GetSensorUptimePercentAsync(DateTimeOffset from, DateTimeOffset to, Guid? deviceId, CancellationToken ct)
    {
        await using var cmd = Command(UptimeSql, from, to);
        AddDevice(cmd, deviceId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is double d ? Math.Round(d, 1) : 100;
    }

    public async Task<IReadOnlyList<DailyConditionStats>> GetDailyStatsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var cmd = Command(DailyStatsSql, from, to);
        var list = new List<DailyConditionStats>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new DailyConditionStats(r.GetFieldValue<DateOnly>(0), r.GetInt32(1),
                Dec(r, 2), Dec(r, 3), Dec(r, 4), Dec(r, 5), Dec(r, 6), Dec(r, 7), r.GetInt32(8), r.GetInt32(9)));
        return list;
    }

    public async Task<IReadOnlyList<TripCompliance>> GetTripComplianceAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var cmd = Command(TripComplianceSql, from, to);
        var list = new List<TripCompliance>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new TripCompliance(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                r.GetInt32(6), r.GetDecimal(7), r.GetDecimal(8), r.GetInt32(9), r.GetInt32(10)));
        return list;
    }

    private NpgsqlCommand Command(string sql, DateTimeOffset from, DateTimeOffset to)
    {
        var cmd = dataSource.CreateCommand(sql);
        cmd.CommandTimeout = 60;
        cmd.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = from.ToUniversalTime() });
        cmd.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = to.ToUniversalTime() });
        return cmd;
    }

    private static void AddDevice(NpgsqlCommand cmd, Guid? deviceId) =>
        cmd.Parameters.Add(new NpgsqlParameter("device", NpgsqlDbType.Uuid) { Value = deviceId.HasValue ? deviceId.Value : DBNull.Value });

    private static decimal? Dec(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : Math.Round(r.GetDecimal(i), 2);
}
