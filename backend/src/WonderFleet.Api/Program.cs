using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Serilog;
using WonderFleet.Api.Endpoints;
using WonderFleet.Api.Setup;
using WonderFleet.Application;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Options;
using WonderFleet.Infrastructure;
using WonderFleet.Infrastructure.Persistence;
using WonderFleet.Infrastructure.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// Render terminates TLS at the edge: trust the forwarded headers so scheme/IP are correct.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, HttpCurrentActor>();
builder.Services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, ConfigureJwtKeys>();
builder.Services.AddWonderFleetAuth();

builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();
builder.Services.AddResponseCompression(options => options.EnableForHttps = false);
builder.Services.AddDatabaseHealthCheck();

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

var appOptions = builder.Configuration.GetSection(AppOptions.Section).Get<AppOptions>() ?? new AppOptions();
builder.Services.AddCors(options => options.AddPolicy("wonderfleet", policy => policy
    .WithOrigins(appOptions.AllowedOrigins.Length > 0 ? appOptions.AllowedOrigins : [appOptions.FrontendBaseUrl])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithExposedHeaders("Content-Disposition")));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // Credential stuffing and link-guessing protection.
    options.AddPolicy(RateLimitPolicies.Sensitive, context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
        }));

    options.AddPolicy(RateLimitPolicies.Reports, context =>
        RateLimitPartition.GetConcurrencyLimiter(ClientKey(context), _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 2,
            QueueLimit = 2,
        }));

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            code = "rate_limited",
            title = "Too many requests",
            detail = "Please slow down and try again shortly.",
        }, ct);
    };

    static string ClientKey(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
            ? "user:" + (context.User.FindFirst("sub")?.Value ?? "anonymous")
            : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
});

var app = builder.Build();

await app.Services.InitialiseDatabaseAsync();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, exception) =>
    exception is not null || context.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : context.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
    : Serilog.Events.LogEventLevel.Information);

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Cross-Origin-Opener-Policy"] = "same-origin";
    headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    if (!app.Environment.IsDevelopment())
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    await next();
});

app.UseResponseCompression();
app.UseCors("wonderfleet");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Swagger UI reads the hand-written OpenAPI documents from docs/swagger (never generated from code).
var swaggerRoot = Path.Combine(AppContext.BaseDirectory, "docs", "swagger");
if (Directory.Exists(swaggerRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(swaggerRoot),
        RequestPath = "/openapi",
        ServeUnknownFileTypes = true,
        DefaultContentType = "application/yaml",
    });
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/openapi.yaml", "WonderFleet API v1");
        options.RoutePrefix = "docs";
        options.DocumentTitle = "WonderFleet API — OfeminiAgricTech";
        options.DisplayRequestDuration();
    });
}

app.MapWonderFleetEndpoints();
app.MapHub<FleetHub>("/hubs/fleet").RequireAuthorization(AuthPolicies.Realtime);

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous();

app.MapGet("/", () => Results.Ok(new
{
    name = "WonderFleet API",
    vendor = "Ofemini Global Limited (OfeminiAgricTech)",
    documentation = "/docs",
})).AllowAnonymous().ExcludeFromDescription();

await app.RunAsync();

internal static class RateLimitPolicies
{
    public const string Sensitive = "sensitive";
    public const string Reports = "reports";
}

internal static class StartupExtensions
{
    /// Applies the SQL migrations and seeds the single administrator before the app serves traffic.
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        if (options.RunMigrationsOnStartup)
            await provider.GetRequiredService<SqlMigrationRunner>().RunAsync(CancellationToken.None);

        await provider.GetRequiredService<DatabaseSeeder>().SeedAsync(CancellationToken.None);
    }
}

/// Exposed so integration tests can build the same pipeline.
public partial class Program;
