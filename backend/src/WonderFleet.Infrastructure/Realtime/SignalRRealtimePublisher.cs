using Microsoft.AspNetCore.SignalR;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Infrastructure.Realtime;

/// Fan-out of telemetry, alerts and notifications. Portal payloads are redacted per audience:
/// the goods owner sees cargo conditions, the transporter sees position and status only.
internal sealed class SignalRRealtimePublisher(IHubContext<FleetHub> hub) : IRealtimePublisher
{
    public async Task PublishTelemetryAsync(TelemetryEvent evt, CancellationToken ct)
    {
        await hub.Clients.Group(FleetHub.AdminsGroup).SendAsync("telemetry", evt, ct);
        if (evt.TripId is not { } tripId) return;

        await hub.Clients.Group(FleetHub.TripGroup(tripId, ShareAudience.AgroProcessor)).SendAsync("telemetry", evt, ct);

        // Transporter view: no temperature/humidity.
        var transporterView = evt with { Temperature = null, Humidity = null };
        await hub.Clients.Group(FleetHub.TripGroup(tripId, ShareAudience.LogisticsPartner)).SendAsync("telemetry", transporterView, ct);
    }

    public async Task PublishAlertAsync(AlertEvent evt, CancellationToken ct)
    {
        await hub.Clients.Group(FleetHub.AdminsGroup).SendAsync("alert", evt, ct);
        if (evt.TripId is not { } tripId) return;

        await hub.Clients.Group(FleetHub.TripGroup(tripId, ShareAudience.AgroProcessor)).SendAsync("alert", evt, ct);

        // Transporter gets the fact of an alert without cargo readings in the message body.
        var transporterView = evt with { Message = "Condition alert raised. The WonderFleet team has been notified." };
        await hub.Clients.Group(FleetHub.TripGroup(tripId, ShareAudience.LogisticsPartner)).SendAsync("alert", transporterView, ct);
    }

    public Task PublishNotificationAsync(Guid notificationId, string title, string category, CancellationToken ct) =>
        hub.Clients.Group(FleetHub.AdminsGroup).SendAsync("notification", new { id = notificationId, title, category }, ct);
}
