using Npgsql;

namespace WonderFleet.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// Npgsql connection string, or a postgres:// URL (Render and Supabase both hand out URLs).
    public string? ConnectionString { get; set; }

    public bool RunMigrationsOnStartup { get; set; } = true;
    public string? MigrationsPath { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// Managed Postgres (Render, Supabase) requires TLS. Only applied when the connection string does not say.
    public string SslMode { get; set; } = "Prefer";

    /// Schemas searched for types and operator classes. Supabase installs extensions such as
    /// pg_trgm into the "extensions" schema, so the trigram indexes need it on the path.
    /// Naming a schema that does not exist is harmless.
    public string SearchPath { get; set; } = "public, extensions";

    /// Set when connecting through a transaction-mode pooler (Supabase's Supavisor on port 6543,
    /// PgBouncer). Those poolers hand a different backend to each transaction, so server-side
    /// prepared statements and session reset must be switched off.
    public bool TransactionPooling { get; set; }

    /// Keep well under the provider's connection limit; the API also opens its own pooled
    /// connections for analytics, migrations and the background workers.
    public int MaxPoolSize { get; set; } = 20;

    public string ApplicationName { get; set; } = "wonderfleet-api";

    /// Produces the effective Npgsql connection string from either input form plus these options.
    public static string Build(string raw, DatabaseOptions options)
    {
        var builder = raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                      || raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            ? FromUrl(raw)
            : new NpgsqlConnectionStringBuilder(raw);

        // An explicit setting in the connection string always wins over configuration.
        if (!builder.ContainsKey("SSL Mode"))
        {
            var sslMode = Enum.TryParse<SslMode>(options.SslMode, true, out var parsed) ? parsed : Npgsql.SslMode.Prefer;
            builder.SslMode = sslMode;
        }

        if (!string.IsNullOrWhiteSpace(options.SearchPath) && !builder.ContainsKey("Search Path"))
            builder.SearchPath = options.SearchPath;

        if (options.TransactionPooling)
        {
            builder.NoResetOnClose = true;  // DISCARD ALL is not available through a transaction pooler
            builder.MaxAutoPrepare = 0;     // prepared statements do not survive the backend switch
        }

        if (options.MaxPoolSize > 0 && !builder.ContainsKey("Maximum Pool Size"))
            builder.MaxPoolSize = options.MaxPoolSize;
        if (!builder.ContainsKey("Application Name"))
            builder.ApplicationName = options.ApplicationName;

        return builder.ConnectionString;
    }

    /// Kept for callers that only need the URL conversion.
    public static string Normalize(string raw, string sslMode) =>
        Build(raw, new DatabaseOptions { SslMode = sslMode });

    private static NpgsqlConnectionStringBuilder FromUrl(string url)
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };
    }
}
