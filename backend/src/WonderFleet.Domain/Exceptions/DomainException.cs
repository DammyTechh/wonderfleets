namespace WonderFleet.Domain.Exceptions;

/// Thrown when a business invariant is violated. Mapped to HTTP 422 by the API.
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
