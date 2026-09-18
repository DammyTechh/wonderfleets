using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace WonderFleet.Infrastructure.Persistence;

/// Applies db/migrations/V{n}__{name}.sql in order, exactly once, inside a transaction guarded by an
/// advisory lock (safe when several instances start together). Applied files are checksummed:
/// editing an applied migration stops the app instead of silently diverging.
public sealed partial class SqlMigrationRunner(
    NpgsqlDataSource dataSource,
    IOptions<DatabaseOptions> options,
    ILogger<SqlMigrationRunner> logger)
{
    private const long LockKey = 0x57464D4947; // "WFMIG"

    [GeneratedRegex(@"^V(?<version>\d+)__(?<name>[A-Za-z0-9_]+)\.sql$")]
    private static partial Regex FilePattern();

    public async Task RunAsync(CancellationToken ct)
    {
        var dir = options.Value.MigrationsPath ?? Path.Combine(AppContext.BaseDirectory, "db", "migrations");
        if (!Directory.Exists(dir))
            throw new InvalidOperationException($"Migrations folder not found: {dir}");

        var files = Directory.GetFiles(dir, "*.sql")
            .Select(f => (Path: f, Match: FilePattern().Match(Path.GetFileName(f))))
            .Where(x => x.Match.Success)
            .Select(x => (x.Path, Version: int.Parse(x.Match.Groups["version"].Value), Name: x.Match.Groups["name"].Value))
            .OrderBy(x => x.Version)
            .ToList();
        if (files.Select(f => f.Version).Distinct().Count() != files.Count)
            throw new InvalidOperationException("Duplicate migration version numbers found.");

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await Exec(conn, tx, "SELECT pg_advisory_xact_lock(@k)", ct, ("k", LockKey));

        // Supabase and some managed providers install extensions (pg_trgm) outside public, so
        // operator classes such as gin_trgm_ops need the extensions schema on the search path.
        await Exec(conn, tx, "SET LOCAL search_path TO public, extensions", ct);
        await Exec(conn, tx, """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version    integer PRIMARY KEY,
                name       varchar(200) NOT NULL,
                checksum   varchar(64)  NOT NULL,
                applied_at timestamptz  NOT NULL DEFAULT now()
            )
            """, ct);

        var applied = new Dictionary<int, string>();
        await using (var cmd = new NpgsqlCommand("SELECT version, checksum FROM schema_migrations", conn, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct)) applied[reader.GetInt32(0)] = reader.GetString(1);
        }

        var count = 0;
        foreach (var (path, version, name) in files)
        {
            var sql = await File.ReadAllTextAsync(path, ct);
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql.ReplaceLineEndings("\n"))));

            if (applied.TryGetValue(version, out var existing))
            {
                if (!string.Equals(existing, checksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Migration V{version} ({name}) was modified after being applied. Add a new migration instead of editing an applied one.");
                continue;
            }

            logger.LogInformation("Applying migration V{Version} {Name}", version, name);
            await using (var cmd = new NpgsqlCommand(sql, conn, tx) { CommandTimeout = 600 })
                await cmd.ExecuteNonQueryAsync(ct);
            await Exec(conn, tx, "INSERT INTO schema_migrations (version, name, checksum) VALUES (@v, @n, @c)", ct,
                ("v", version), ("n", name), ("c", checksum));
            count++;
        }

        await tx.CommitAsync(ct);
        logger.LogInformation("Database schema is up to date ({Applied} applied now, {Total} total)", count, files.Count);
    }

    private static async Task Exec(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct, params (string Name, object Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
