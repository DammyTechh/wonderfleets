using FluentValidation;

namespace WonderFleet.Application.Common.Helpers;

public static class ValidationRules
{
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(10).WithMessage("Password must be at least 10 characters.")
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a symbol.");

    public static IRuleBuilderOptions<T, string> PhoneNumber<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Must(Phone.IsValid).WithMessage("Enter a valid phone number, e.g. +234 803 123 4567.");

    public static IRuleBuilderOptions<T, string> EmailAddressStrict<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(254).EmailAddress().WithMessage("Enter a valid email address.");

    public static IRuleBuilderOptions<T, string> Name<T>(this IRuleBuilder<T, string> rule, int max = 150) =>
        rule.NotEmpty().MaximumLength(max);

    public static IRuleBuilderOptions<T, double?> Latitude<T>(this IRuleBuilder<T, double?> rule) =>
        rule.InclusiveBetween(-90, 90);

    public static IRuleBuilderOptions<T, double?> Longitude<T>(this IRuleBuilder<T, double?> rule) =>
        rule.InclusiveBetween(-180, 180);
}
