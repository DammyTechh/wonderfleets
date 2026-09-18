using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WonderFleet.Application.Common.Exceptions;

namespace WonderFleet.Api.Setup;

/// Turns domain and application exceptions into RFC 7807 problem details.
/// Unexpected exceptions never leak their message to the client.
internal sealed class AppExceptionHandler(IProblemDetailsService problemDetails, ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail, code, errors) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogInformation("{Code} on {Method} {Path}: {Detail}", code, context.Request.Method, context.Request.Path, detail);

        context.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://docs.wonderfleet.app/errors/{code}",
            Instance = $"{context.Request.Method} {context.Request.Path}",
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (errors is not null) problem.Extensions["errors"] = errors;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static (int Status, string Title, string Detail, string Code, IDictionary<string, string[]>? Errors) Map(Exception exception) =>
        exception switch
        {
            RequestValidationException v => (StatusCodes.Status400BadRequest, "Validation failed", v.Message, v.Code, v.Errors),
            UnauthorizedException u => (StatusCodes.Status401Unauthorized, "Unauthorized", u.Message, u.Code, null),
            ForbiddenException f => (StatusCodes.Status403Forbidden, "Forbidden", f.Message, f.Code, null),
            NotFoundException n => (StatusCodes.Status404NotFound, "Not found", n.Message, n.Code, null),
            ConflictException c => (StatusCodes.Status409Conflict, "Conflict", c.Message, c.Code, null),
            BusinessRuleException b => (StatusCodes.Status422UnprocessableEntity, "Request cannot be completed", b.Message, b.Code, null),
            ExternalServiceException e => (StatusCodes.Status502BadGateway, "Upstream service error", e.Message, e.Code, null),
            Domain.Exceptions.DomainException d => (StatusCodes.Status422UnprocessableEntity, "Request cannot be completed", d.Message, d.Code, null),
            OperationCanceledException => (StatusCodesExtra.Status499ClientClosedRequest, "Request cancelled", "The request was cancelled.", "cancelled", null),
            BadHttpRequestException bad => (StatusCodes.Status400BadRequest, "Invalid request", bad.Message, "invalid_request", null),
            _ => (StatusCodes.Status500InternalServerError, "Server error",
                  "Something went wrong on our side. Please try again; if it persists, contact support with the trace id.", "server_error", null),
        };
}

internal static class StatusCodesExtra
{
    public const int Status499ClientClosedRequest = 499;
}
