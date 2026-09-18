using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using WonderFleet.Api.Setup;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Models;
using WonderFleet.Application.Common.Options;
using WonderFleet.Application.Features.Portal;
using WonderFleet.Application.Features.Telemetry;
using WonderFleet.Domain.Enums;
using WonderFleet.Infrastructure.Security;

namespace WonderFleet.Api.Endpoints;

internal static class PublicEndpoints
{
    private static readonly System.Text.Json.JsonSerializerOptions WebhookJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    /// Recipients of a share link never sign in: they exchange the link token for a short-lived portal session.
    public static void MapPortalEndpoints(this IEndpointRouteBuilder app)
    {
        var portal = app.MapGroup("/portal").WithTags("Partner portal");

        portal.MapPost("/session", (StartPortalSessionRequest request, IPortalService service, CancellationToken ct) =>
                service.StartSessionAsync(request.Token, ct))
            .Validate<StartPortalSessionRequest>()
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Sensitive);

        var session = portal.MapGroup("/").RequireAuthorization(AuthPolicies.Portal);
        session.MapGet("/header", (IPortalService service, CancellationToken ct) => service.GetHeaderAsync(ct));

        // Transporter dashboard: fleet management + live tracking, no cargo climate data.
        var logistics = app.PortalGroup("/portal/logistics", AuthPolicies.PortalLogistics, "Partner portal");
        logistics.MapGet("/vehicles", (IPortalService service, CancellationToken ct, int page = 1, int pageSize = 10) =>
            service.GetLogisticsVehiclesAsync(new PageQuery { Page = page, PageSize = pageSize }, ct));
        logistics.MapGet("/tracking", (IPortalService service, CancellationToken ct) => service.GetLogisticsTrackingAsync(ct));

        // Goods-owner dashboard: live tracking of their produce with conditions.
        var agro = app.PortalGroup("/portal/agro", AuthPolicies.PortalAgro, "Partner portal");
        agro.MapGet("/shipments", (IPortalService service, CancellationToken ct) => service.GetAgroShipmentsAsync(ct));
        agro.MapGet("/tracking", (IPortalService service, CancellationToken ct) => service.GetAgroTrackingAsync(ct));
        agro.MapGet("/shipments/{tripId:guid}/readings", (Guid tripId, IPortalService service, CancellationToken ct, int hours = 12) =>
            service.GetAgroReadingsAsync(tripId, hours, ct));
    }

    /// Signed, expiring URLs for stored images and logos (no storage paths are ever exposed).
    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/media", async (string p, long e, string s, MediaUrlSigner signer, IFileStorage storage, CancellationToken ct) =>
            {
                if (!signer.Verify(p, e, s)) throw new ForbiddenException("This media link has expired.");

                var stream = await storage.OpenReadAsync(p, ct);
                if (stream is null) return Results.NotFound();

                var contentType = Path.GetExtension(p).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".webp" => "image/webp",
                    ".pdf" => "application/pdf",
                    _ => "application/octet-stream",
                };
                return Results.Stream(stream, contentType, enableRangeProcessing: true);
            })
            .AllowAnonymous()
            .WithTags("Media")
            .CacheOutputNothing();
    }

    /// Optional HTTPS push path for the hardware (alternative to Firebase polling), signed with HMAC-SHA256.
    public static void MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/telemetry", async (HttpContext http, ITelemetryIngestionService ingestion,
                IOptions<TelemetryOptions> options, ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                var secret = options.Value.WebhookSecret;
                if (string.IsNullOrWhiteSpace(secret)) return Results.NotFound();

                http.Request.EnableBuffering();
                using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync(ct);
                http.Request.Body.Position = 0;

                if (!VerifySignature(http.Request.Headers["X-WonderFleet-Signature"].ToString(), body, secret))
                {
                    loggerFactory.CreateLogger("Telemetry").LogWarning("Rejected telemetry webhook with an invalid signature");
                    return Results.Unauthorized();
                }

                var payload = System.Text.Json.JsonSerializer.Deserialize<TelemetryWebhookPayload>(body, WebhookJson);
                if (payload?.FirebaseKey is null) return Results.BadRequest(new { code = "invalid_payload" });

                var outcome = await ingestion.IngestAsync(new DeviceSnapshot(
                    payload.FirebaseKey, payload.Temperature, payload.Humidity, payload.Latitude, payload.Longitude,
                    payload.IsActive ?? true, payload.BatteryLevel, payload.RecordedAt), TelemetrySource.Webhook, ct);

                return Results.Ok(new { accepted = true, outcome = outcome.ToString() });
            })
            .AllowAnonymous()
            .WithTags("Telemetry");
    }

    private static bool VerifySignature(string header, string body, string secret)
    {
        var provided = header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase) ? header[7..] : header;
        if (provided.Length == 0) return false;
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(provided.Trim().ToLowerInvariant()));
    }

    private static RouteHandlerBuilder CacheOutputNothing(this RouteHandlerBuilder builder) =>
        builder.WithMetadata(new ResponseCacheMetadata());
}

public sealed record TelemetryWebhookPayload(
    string? FirebaseKey, decimal? Temperature, decimal? Humidity, double? Latitude, double? Longitude,
    bool? IsActive, int? BatteryLevel, DateTimeOffset? RecordedAt);

/// Marker so media responses are never cached by shared proxies (URLs are per-user signed).
internal sealed class ResponseCacheMetadata;
