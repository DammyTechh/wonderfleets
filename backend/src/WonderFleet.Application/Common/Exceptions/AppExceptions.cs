namespace WonderFleet.Application.Common.Exceptions;

public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class NotFoundException(string entity, object key)
    : AppException("not_found", $"{entity} '{key}' was not found.");

public sealed class ConflictException(string message, string code = "conflict") : AppException(code, message);

public sealed class ForbiddenException(string message = "You do not have access to this resource.")
    : AppException("forbidden", message);

public sealed class UnauthorizedException(string message = "Authentication failed.", string code = "unauthorized")
    : AppException(code, message);

public sealed class BusinessRuleException(string code, string message) : AppException(code, message);

public sealed class ExternalServiceException(string service, string message)
    : AppException("external_service_error", $"{service}: {message}");

public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : AppException("validation_failed", "One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public static RequestValidationException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
