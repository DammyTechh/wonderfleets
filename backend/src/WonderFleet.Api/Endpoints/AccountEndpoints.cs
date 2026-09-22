using WonderFleet.Api.Setup;
using WonderFleet.Application.Features.Auth;
using WonderFleet.Application.Features.Dashboard;
using WonderFleet.Application.Features.Profile;

namespace WonderFleet.Api.Endpoints;

internal static class AccountEndpoints
{
    private const string RefreshCookie = "wf_rt";
    private const string RefreshCookiePath = $"{EndpointRegistration.BasePath}/auth";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Authentication").AllowAnonymous();

        // There is no administrator registration endpoint by design: the admin is seeded.
        group.MapPost("/login", async (LoginRequest request, IAuthService auth, HttpContext http, CancellationToken ct) =>
            {
                var result = await auth.LoginAsync(request, ct);
                SetRefreshCookie(http, result.RefreshToken, result.RefreshTokenExpiresAt);
                return Results.Ok(result);
            })
            .Validate<LoginRequest>()
            .RequireRateLimiting(RateLimitPolicies.Sensitive);

        group.MapPost("/refresh", async (RefreshRequest? request, IAuthService auth, HttpContext http, CancellationToken ct) =>
            {
                var token = request?.RefreshToken ?? http.Request.Cookies[RefreshCookie];
                var result = await auth.RefreshAsync(token ?? string.Empty, ct);
                SetRefreshCookie(http, result.RefreshToken, result.RefreshTokenExpiresAt);
                return Results.Ok(result);
            })
            .RequireRateLimiting(RateLimitPolicies.Sensitive);

        group.MapPost("/logout", async (RefreshRequest? request, IAuthService auth, HttpContext http, CancellationToken ct) =>
        {
            await auth.LogoutAsync(request?.RefreshToken ?? http.Request.Cookies[RefreshCookie], ct);
            http.Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = RefreshCookiePath });
            return Results.NoContent();
        });

        group.MapGet("/me", (IAuthService auth, CancellationToken ct) => auth.GetCurrentAsync(ct))
            .RequireAuthorization(AuthPolicies.Admin);
    }

    public static void MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/profile", "Profile");

        group.MapGet("/", (IProfileService profile, CancellationToken ct) => profile.GetAsync(ct));

        group.MapPut("/", (UpdateProfileRequest request, IProfileService profile, CancellationToken ct) =>
            profile.UpdateAsync(request, ct)).Validate<UpdateProfileRequest>();

        group.MapPost("/password", async (ChangePasswordRequest request, IProfileService profile, HttpContext http, CancellationToken ct) =>
            {
                await profile.ChangePasswordAsync(request, ct);
                http.Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = RefreshCookiePath });
                return Results.NoContent();
            })
            .Validate<ChangePasswordRequest>()
            .RequireRateLimiting(RateLimitPolicies.Sensitive);

        group.MapPost("/photo", async (IFormFile file, IProfileService profile, CancellationToken ct) =>
                Results.Ok(await profile.UploadPhotoAsync(file.ToUploadedFile(), ct)))
            .DisableAntiforgery();
    }

    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.AdminGroup("/dashboard", "Dashboard")
            .MapGet("/", (IDashboardService dashboard, CancellationToken ct) => dashboard.GetAsync(ct));
    }

    /// HttpOnly refresh cookie for browsers; the token is also returned for non-browser clients.
    ///
    /// Browsers reject SameSite=None unless the cookie is also Secure, and plain-http
    /// localhost cannot be Secure. Locally the Vite proxy makes the API same-origin, so
    /// Lax is correct there. Deployed, the frontend and API are separate origins over
    /// HTTPS, which is what None + Secure is for.
    private static void SetRefreshCookie(HttpContext http, string token, DateTimeOffset expiresAt)
    {
        var host = http.Request.Host.Host;
        var local = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1";
        http.Response.Cookies.Append(RefreshCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !local,
            SameSite = local ? SameSiteMode.Lax : SameSiteMode.None,
            Expires = expiresAt,
            Path = RefreshCookiePath,
        });
    }
}
