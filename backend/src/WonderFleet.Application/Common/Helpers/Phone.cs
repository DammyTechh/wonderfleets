using System.Text.RegularExpressions;

namespace WonderFleet.Application.Common.Helpers;

public static partial class Phone
{
    [GeneratedRegex(@"^\+?[0-9]{10,15}$")]
    private static partial Regex GenericPattern();

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && GenericPattern().IsMatch(Clean(value));

    /// Normalises Nigerian numbers (0803…, 234803…, +234 803…) to E.164; other valid numbers pass through.
    public static string ToE164(string value)
    {
        var digits = Clean(value);
        if (digits.StartsWith('+')) return digits;
        if (digits.StartsWith("234", StringComparison.Ordinal)) return "+" + digits;
        if (digits.StartsWith('0') && digits.Length == 11) return "+234" + digits[1..];
        return "+" + digits;
    }

    private static string Clean(string value) =>
        new(value.Where(c => char.IsDigit(c) || c == '+').ToArray());
}

public static class Text
{
    public static string Initials(string? name) =>
        string.Concat((name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    public static string Route(string origin, string destination) => $"{origin} → {destination}";

    public static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
