using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Api.Setup;

internal static class ReportFileExtensions
{
    public static IResult ToResult(this ReportFile file) =>
        Results.File(file.Content, file.ContentType, file.FileName);
}
