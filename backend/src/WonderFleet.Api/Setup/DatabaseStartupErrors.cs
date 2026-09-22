using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace WonderFleet.Api.Setup;

/// Turns the database failures people actually hit on first run into instructions.
/// Returns null for anything unrecognised, so real bugs still surface with a stack trace.
internal static class DatabaseStartupErrors
{
    public static string? Explain(Exception exception, IServiceProvider services)
    {
        var target = Describe(services);

        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            switch (ex)
            {
                case PostgresException { SqlState: PostgresErrorCodes.InvalidPassword }:
                    return $$"""
                        PostgreSQL at {{target}} rejected the password.

                        Postgres only sets its password the first time its data volume is created, so this
                        almost always means an OLDER volume or a DIFFERENT Postgres is answering on that port.

                        Fix (from the repository root):
                          1. docker compose -f deploy/docker-compose.yml down -v
                             (-v removes the volume holding the old password; this deletes LOCAL data only)
                          2. docker compose -f deploy/docker-compose.yml up -d db
                          3. Run the API again.

                        If it still fails, something else owns the port. Check with:
                          Get-NetTCPConnection -LocalPort {{Port(services)}} -State Listen | ForEach-Object { Get-Process -Id $_.OwningProcess }
                        then pick a free port, set DB_PORT and the Port= in ConnectionStrings__Default in .env,
                        and start the database with: docker compose --env-file .env -f deploy/docker-compose.yml up -d db
                        """;

                case PostgresException { SqlState: PostgresErrorCodes.InvalidCatalogName }:
                    return $"""
                        PostgreSQL at {target} is running but has no database by that name.
                        The container only creates "wonderfleet" when its volume is new; recreate it with
                        (this deletes LOCAL data only):
                          docker compose -f deploy/docker-compose.yml down -v
                          docker compose -f deploy/docker-compose.yml up -d db
                        """;

                case SocketException:
                case NpgsqlException { InnerException: SocketException }:
                    return $"""
                        Nothing is accepting connections at {target}.
                        Start the database (from the repository root) and wait for "healthy":
                          docker compose -f deploy/docker-compose.yml up -d db
                          docker compose -f deploy/docker-compose.yml ps
                        """;

                case TimeoutException:
                case NpgsqlException { InnerException: TimeoutException }:
                    return $"""
                        Timed out connecting to {target}. The database may still be starting — wait a few
                        seconds and run the API again. Check its status with:
                          docker compose -f deploy/docker-compose.yml ps
                        """;
            }
        }
        return null;
    }

    /// Host, port, database and user — never the password.
    private static string Describe(IServiceProvider services)
    {
        try
        {
            var csb = new NpgsqlConnectionStringBuilder(services.GetRequiredService<NpgsqlDataSource>().ConnectionString);
            return $"{csb.Host}:{csb.Port} (database \"{csb.Database}\", user \"{csb.Username}\")";
        }
        catch (Exception)
        {
            return "the configured address";
        }
    }

    private static int Port(IServiceProvider services)
    {
        try
        {
            return new NpgsqlConnectionStringBuilder(services.GetRequiredService<NpgsqlDataSource>().ConnectionString).Port;
        }
        catch (Exception)
        {
            return 5433;
        }
    }
}
