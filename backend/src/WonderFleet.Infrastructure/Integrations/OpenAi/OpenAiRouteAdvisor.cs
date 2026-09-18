using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Infrastructure.Integrations.OpenAi;

public sealed class OpenAiOptions
{
    public const string Section = "OpenAi";
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://api.openai.com";
    public string Model { get; set; } = "gpt-4.1-mini";
    /// Set for reasoning models (minimal | low | medium | high); leave empty otherwise.
    public string? ReasoningEffort { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// Ranks the Google route alternatives against the forecast along each route using the
/// OpenAI Responses API with a strict JSON schema. Returns null on any failure: the caller
/// then falls back to the deterministic heuristic, so route AI is never a hard dependency.
internal sealed class OpenAiRouteAdvisor(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiOptions> options,
    ILogger<OpenAiRouteAdvisor> logger) : IRouteAdvisor
{
    private const string SystemPrompt =
        "You are a cold-chain logistics adviser for WonderFleet, monitoring agricultural produce moving by road in Nigeria. " +
        "Given route alternatives and the forecast temperature sampled along each one, choose the route that best protects the produce, " +
        "trading travel time against heat exposure. Recommend a later departure only when it clearly reduces heat exposure. " +
        "Be concrete and brief; give practical driver guidance for hot-climate haulage. Never invent routes or numbers that were not provided.";

    public async Task<RouteAdvice?> AdviseAsync(RouteAdviceRequest request, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured || request.Routes.Count == 0) return null;

        var payload = new JsonObject
        {
            ["model"] = o.Model,
            ["max_output_tokens"] = 1200,
            ["store"] = false,
            ["input"] = new JsonArray(
                Message("system", SystemPrompt),
                Message("user", BuildPrompt(request))),
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "route_advice",
                    ["strict"] = true,
                    ["schema"] = Schema(request.Routes.Count),
                },
            },
        };
        if (!string.IsNullOrWhiteSpace(o.ReasoningEffort))
            payload["reasoning"] = new JsonObject { ["effort"] = o.ReasoningEffort };

        var http = httpClientFactory.CreateClient(HttpClients.OpenAi);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/v1/responses")
        {
            Content = JsonContent.Create(payload),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);

        using var response = await http.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenAI returned {Status}: {Body}", (int)response.StatusCode, body.Length > 300 ? body[..300] : body);
            return null;
        }

        var text = ExtractOutputText(body);
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var shift = root.GetProperty("departureShiftMinutes").GetInt32();
            return new RouteAdvice(
                root.GetProperty("selectedRouteIndex").GetInt32(),
                shift == 0 ? null : request.RequestedDeparture.AddMinutes(Math.Clamp(shift, -240, 1440)),
                root.GetProperty("heatRiskLevel").GetString() ?? "Moderate",
                (decimal)root.GetProperty("estimatedSpoilageReductionPct").GetDouble(),
                root.GetProperty("summary").GetString() ?? "",
                root.GetProperty("driverTips").EnumerateArray().Select(t => t.GetString() ?? "").Where(t => t.Length > 0).ToList(),
                o.Model);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "OpenAI returned malformed advice JSON");
            return null;
        }
    }

    private static JsonObject Message(string role, string content) => new()
    {
        ["role"] = role,
        ["content"] = content,
    };

    internal static string BuildPrompt(RouteAdviceRequest request)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"Origin: {request.Origin}\nDestination: {request.Destination}\n");
        sb.Append(CultureInfo.InvariantCulture, $"Planned departure (UTC): {request.RequestedDeparture:yyyy-MM-dd HH:mm}\n");
        sb.Append(CultureInfo.InvariantCulture, $"Produce: {(request.Produce.Count == 0 ? "unspecified" : string.Join(", ", request.Produce))}\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"Maximum safe cargo temperature: {(request.MaxSafeTemperature is { } m ? m.ToString("0.#", CultureInfo.InvariantCulture) + " C" : "unspecified")}\n\n");

        foreach (var context in request.Routes)
        {
            var route = context.Route;
            sb.Append(CultureInfo.InvariantCulture,
                $"Route index {route.Index}: {route.Description}; {route.DistanceKm:0.#} km; {route.DurationMinutes} minutes driving");
            if (route.Warnings.Count > 0) sb.Append(CultureInfo.InvariantCulture, $"; warnings: {string.Join("; ", route.Warnings)}");
            sb.Append('\n');
            if (context.WeatherSamples.Count == 0)
            {
                sb.Append("  forecast: unavailable\n");
                continue;
            }
            foreach (var sample in context.WeatherSamples)
                sb.Append(CultureInfo.InvariantCulture,
                    $"  at {sample.Time:HH:mm} UTC: {sample.TemperatureC:0.#} C, {sample.RelativeHumidity:0} % RH, {sample.Condition}\n");
        }

        sb.Append("\nReturn the chosen route index, a departure shift in minutes (0 to keep the planned departure), ");
        sb.Append("the heat risk level, an estimated spoilage reduction percentage (0-60, conservative), ");
        sb.Append("a two-sentence summary and up to four short driver tips.");
        return sb.ToString();
    }

    private static JsonObject Schema(int routeCount) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("selectedRouteIndex", "departureShiftMinutes", "heatRiskLevel", "estimatedSpoilageReductionPct", "summary", "driverTips"),
        ["properties"] = new JsonObject
        {
            ["selectedRouteIndex"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = Math.Max(0, routeCount - 1) },
            ["departureShiftMinutes"] = new JsonObject { ["type"] = "integer", ["minimum"] = -240, ["maximum"] = 1440 },
            ["heatRiskLevel"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Low", "Moderate", "High") },
            ["estimatedSpoilageReductionPct"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 60 },
            ["summary"] = new JsonObject { ["type"] = "string" },
            ["driverTips"] = new JsonObject
            {
                ["type"] = "array",
                ["maxItems"] = 4,
                ["items"] = new JsonObject { ["type"] = "string" },
            },
        },
    };

    /// Responses API: concatenate every output_text part of the assistant message.
    internal static string ExtractOutputText(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("output", out var output)) return "";

        var builder = new System.Text.StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text"
                    && part.TryGetProperty("text", out var text))
                    builder.Append(text.GetString());
            }
        }
        return builder.ToString();
    }
}
