namespace WonderFleet.Infrastructure.Integrations;

/// Named HttpClient registrations (see AddInfrastructure). Each gets its own timeout and resilience handler.
internal static class HttpClients
{
    public const string Google = "wonderfleet-google";
    public const string OpenMeteo = "wonderfleet-open-meteo";
    public const string Firebase = "wonderfleet-firebase";
    public const string OpenAi = "wonderfleet-openai";
}
