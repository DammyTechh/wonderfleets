using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";
    /// Mount a Render persistent disk here in production.
    public string RootPath { get; set; } = "storage";
}

/// Disk storage with random names, per-category folders and strict path containment.
internal sealed partial class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex CategoryPattern();

    [GeneratedRegex(@"^\.[a-z0-9]{1,6}$")]
    private static partial Regex ExtensionPattern();

    private string Root => Path.GetFullPath(options.Value.RootPath);

    public async Task<string> SaveAsync(Stream content, string category, string extension, CancellationToken ct)
    {
        if (!CategoryPattern().IsMatch(category)) throw new ArgumentException("Invalid storage category.", nameof(category));
        if (!ExtensionPattern().IsMatch(extension)) throw new ArgumentException("Invalid extension.", nameof(extension));

        var now = DateTime.UtcNow;
        var relative = $"{category}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        await using var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, ct);
        return relative;
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct)
    {
        var full = Resolve(storagePath);
        Stream? stream = File.Exists(full)
            ? new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        var full = Resolve(storagePath);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    private string Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new UnauthorizedAccessException("Invalid storage path.");
        var root = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Invalid storage path.");
        return full;
    }
}
