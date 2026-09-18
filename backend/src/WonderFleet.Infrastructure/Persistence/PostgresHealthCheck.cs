using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace WonderFleet.Infrastructure.Persistence;

/// Readiness probe: opens a pooled connection and runs a trivial query.
internal sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            command.CommandTimeout = 5;
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", ex);
        }
    }
}
