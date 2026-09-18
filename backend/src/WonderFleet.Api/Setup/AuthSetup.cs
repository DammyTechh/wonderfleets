using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Features.ShareLinks;
using WonderFleet.Domain.Enums;
using WonderFleet.Infrastructure.Persistence;
using WonderFleet.Infrastructure.Security;

namespace WonderFleet.Api.Setup;

internal static class AuthSchemes
{
    public const string Admin = "Admin";
    public const string Portal = "Portal";
}

internal static class AuthPolicies
{
    public const string Admin = "admin";
    public const string SuperAdmin = "super-admin";
    public const string Portal = "portal";
    public const string PortalLogistics = "portal-logistics";
    public const string PortalAgro = "portal-agro";
    public const string Realtime = "realtime";
}

internal static class AuthSetup
{
    public static IServiceCollection AddWonderFleetAuth(this IServiceCollection services)
    {
        services.AddAuthentication(AuthSchemes.Admin)
            .AddJwtBearer(AuthSchemes.Admin, options => Configure(options, isAdmin: true))
            .AddJwtBearer(AuthSchemes.Portal, options => Configure(options, isAdmin: false));

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Admin, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Admin)
                .RequireAuthenticatedUser()
                .RequireClaim(TokenClaims.TokenType, TokenClaims.AdminType))
            .AddPolicy(AuthPolicies.SuperAdmin, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Admin)
                .RequireAuthenticatedUser()
                .RequireClaim(TokenClaims.TokenType, TokenClaims.AdminType)
                .RequireRole(nameof(AdminRole.SuperAdmin)))
            .AddPolicy(AuthPolicies.Portal, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Portal)
                .RequireAuthenticatedUser()
                .RequireClaim(TokenClaims.TokenType, TokenClaims.PortalType))
            .AddPolicy(AuthPolicies.PortalLogistics, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Portal)
                .RequireAuthenticatedUser()
                .RequireClaim(TokenClaims.Audience, nameof(ShareAudience.LogisticsPartner)))
            .AddPolicy(AuthPolicies.PortalAgro, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Portal)
                .RequireAuthenticatedUser()
                .RequireClaim(TokenClaims.Audience, nameof(ShareAudience.AgroProcessor)))
            .AddPolicy(AuthPolicies.Realtime, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Admin, AuthSchemes.Portal)
                .RequireAuthenticatedUser());

        return services;
    }

    private static void Configure(JwtBearerOptions options, bool isAdmin)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = ClaimTypes.Role,
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // SignalR cannot set headers on the WebSocket handshake.
                if (context.Request.Path.StartsWithSegments("/hubs")
                    && context.Request.Query.TryGetValue("access_token", out var token))
                    context.Token = token;
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var services = context.HttpContext.RequestServices;
                var principal = context.Principal;
                var subject = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (!Guid.TryParse(subject, out var id))
                {
                    context.Fail("Invalid subject.");
                    return;
                }

                if (isAdmin)
                {
                    // A password change or a forced sign-out rotates the security stamp: old tokens die immediately.
                    var stamp = principal!.FindFirst(TokenClaims.SecurityStamp)?.Value;
                    var db = services.GetRequiredService<ApplicationDbContext>();
                    var current = await db.AdminUsers.AsNoTracking()
                        .Where(a => a.Id == id && a.IsActive)
                        .Select(a => a.SecurityStamp)
                        .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
                    if (current is null || !string.Equals(current, stamp, StringComparison.Ordinal))
                        context.Fail("The session is no longer valid.");
                }
                else
                {
                    // The share link may have been revoked, or its trip may have ended, since the token was issued.
                    var links = services.GetRequiredService<IShareLinkService>();
                    if (!await links.IsActiveAsync(id, context.HttpContext.RequestAborted))
                        context.Fail("This tracking link has expired.");
                }
            },
        };
    }

    /// Signing keys and issuer/audience come from the resolved key ring (ephemeral in Development).
    public static void BindKeys(JwtBearerOptions options, KeyRing keys, JwtOptions jwt, bool isAdmin)
    {
        options.TokenValidationParameters.ValidIssuer = jwt.Issuer;
        options.TokenValidationParameters.ValidAudience = isAdmin ? jwt.AdminAudience : jwt.PortalAudience;
        options.TokenValidationParameters.IssuerSigningKey =
            new SymmetricSecurityKey(isAdmin ? keys.AdminSigningKey : keys.PortalSigningKey);
    }
}

/// Applies the key ring after options binding (keys are only known once DI is built).
internal sealed class ConfigureJwtKeys(KeyRing keys, Microsoft.Extensions.Options.IOptions<JwtOptions> jwt)
    : Microsoft.Extensions.Options.IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        if (name is AuthSchemes.Admin or AuthSchemes.Portal)
            AuthSetup.BindKeys(options, keys, jwt.Value, name == AuthSchemes.Admin);
    }
}
