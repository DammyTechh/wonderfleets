using FluentValidation;
using WonderFleet.Application.Common.Exceptions;

namespace WonderFleet.Api.Setup;

/// Runs the FluentValidation validator registered for the first request-body argument.
internal sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var model = context.Arguments.OfType<T>().FirstOrDefault();
        if (model is null)
            throw new RequestValidationException(new Dictionary<string, string[]> { ["body"] = ["A request body is required."] });

        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (validator is not null)
        {
            var result = await validator.ValidateAsync(model, context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
                throw new RequestValidationException(errors);
            }
        }
        return await next(context);
    }
}

internal static class ValidationFilterExtensions
{
    /// Adds request validation and documents the 400 response shape.
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
