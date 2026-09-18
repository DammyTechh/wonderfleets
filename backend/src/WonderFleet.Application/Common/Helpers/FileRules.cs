using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Application.Common.Helpers;

/// Upload guard: size limits, allow-listed types, and magic-byte sniffing (the declared content type is never trusted).
public static class FileRules
{
    public const long MaxImageBytes = 2 * 1024 * 1024;
    public const long MaxDocumentBytes = 10 * 1024 * 1024;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46];

    public static (string Extension, string ContentType) EnsureImage(UploadedFile file) =>
        Ensure(file, MaxImageBytes, allowPdf: false);

    public static (string Extension, string ContentType) EnsureDocument(UploadedFile file) =>
        Ensure(file, MaxDocumentBytes, allowPdf: true);

    private static (string, string) Ensure(UploadedFile file, long maxBytes, bool allowPdf)
    {
        if (file.Length <= 0)
            throw RequestValidationException.For("file", "The file is empty.");
        if (file.Length > maxBytes)
            throw RequestValidationException.For("file", $"The file exceeds {maxBytes / 1024 / 1024}MB.");

        var header = new byte[8];
        using (var stream = file.OpenReadStream())
        {
            var read = 0;
            while (read < header.Length)
            {
                var n = stream.Read(header, read, header.Length - read);
                if (n == 0) break;
                read += n;
            }
        }

        if (StartsWith(header, Png)) return (".png", "image/png");
        if (StartsWith(header, Jpeg)) return (".jpg", "image/jpeg");
        if (allowPdf && StartsWith(header, Pdf)) return (".pdf", "application/pdf");

        throw RequestValidationException.For("file", allowPdf
            ? "Only PDF, PNG or JPG files are allowed."
            : "Only PNG or JPG images are allowed.");
    }

    private static bool StartsWith(byte[] data, byte[] prefix) =>
        data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    public static string SafeFileName(string name)
    {
        var cleaned = new string(Path.GetFileName(name).Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());
        return cleaned.Length > 120 ? cleaned[^120..] : cleaned;
    }
}
