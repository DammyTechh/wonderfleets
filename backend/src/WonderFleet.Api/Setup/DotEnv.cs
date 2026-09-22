namespace WonderFleet.Api.Setup;

/// Loads the repository-root .env into environment variables before the host starts,
/// so `dotnet run` and `docker compose` are configured from the same file.
///
/// Rules, chosen so this can never do harm:
///   * Real environment variables always win; a .env value never overwrites one.
///   * Blank values are skipped, so `Google__ApiKey=` means "not set" rather than
///     overriding appsettings with an empty string.
///   * Only the first '=' splits key from value, so connection strings survive intact.
///   * A missing .env is fine; everything then comes from appsettings.
internal static class DotEnv
{
    public static string? Load()
    {
        var path = Find();
        if (path is null) return null;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();

            var split = line.IndexOf('=');
            if (split <= 0) continue;

            var key = line[..split].Trim();
            var value = line[(split + 1)..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];

            if (value.Length == 0) continue;
            if (Environment.GetEnvironmentVariable(key) is not null) continue;
            Environment.SetEnvironmentVariable(key, value);
        }
        return path;
    }

    /// Walks up from the working directory, then from the build output, to the first .env.
    /// Stops at the repository root (the folder holding deploy/) so a stray .env
    /// elsewhere on the machine is never picked up.
    private static string? Find()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                if (File.Exists(candidate)) return candidate;
                if (Directory.Exists(Path.Combine(dir.FullName, "deploy"))) break;
            }
        }
        return null;
    }
}
