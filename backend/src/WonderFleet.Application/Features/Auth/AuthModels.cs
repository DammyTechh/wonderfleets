using FluentValidation;

namespace WonderFleet.Application.Features.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record AdminProfileDto(
    Guid Id, string AdminCode, string FullName, string Email, string? PhoneNumber, string? Address,
    string? PhotoUrl, string Role, bool MustChangePassword, DateTimeOffset? LastLoginAt);

public sealed record AuthResult(
    string AccessToken, DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken, DateTimeOffset RefreshTokenExpiresAt,
    AdminProfileDto Admin);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254).EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}
