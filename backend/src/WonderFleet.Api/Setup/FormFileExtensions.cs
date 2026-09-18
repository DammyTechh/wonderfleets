using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Api.Setup;

internal static class FormFileExtensions
{
    /// Bridges ASP.NET's IFormFile to the Application layer's transport-agnostic upload model.
    public static UploadedFile ToUploadedFile(this IFormFile file) =>
        new(file.FileName, file.ContentType, file.Length, file.OpenReadStream);
}
