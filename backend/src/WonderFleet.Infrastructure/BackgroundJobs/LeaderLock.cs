using Npgsql;

namespace WonderFleet.Infrastructure.BackgroundJobs;

/// PostgreSQL advisory lock so only one instance runs a periodic job at a time
/// (Render can run several web instances; double-polling would double-count telemetry).
internal sealed class LeaderLock(NpgsqlDataSource dataSource)
{
    public const long TelemetryPolling = 0x57465F5450;   // "WF_TP"
    public const long Monitoring = 0x57465F4D4F;         // "WF_MO"

    public async Task<bool> RunExclusiveAsync(long key, Func<CancellationToken, Task> work, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var command = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@key)", connection, transaction))
        {
            command.Parameters.AddWithValue("key", key);
            if (await command.ExecuteScalarAsync(ct) is not true) return false;
        }

        try
        {
            await work(ct);
        }
        finally
        {
            await transaction.CommitAsync(ct);
        }
        return true;
    }
}
