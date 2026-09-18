using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Options;
using WonderFleet.Application.Features.Notifications;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Fleet;

/// Pushes cargo limits to the device node (settings/{key}) so the unit can alarm locally even without GSM.
internal sealed class ThresholdPublisher(
    IDeviceCloudGateway cloud,
    IApplicationDbContext db,
    INotificationComposer notify,
    ILogger<ThresholdPublisher> logger)
{
    public async Task<bool> TryPushAsync(Device device, CargoThresholds thresholds, string tripCode, CancellationToken ct)
    {
        try
        {
            await cloud.PushThresholdsAsync(device.FirebaseKey, thresholds, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not push thresholds for trip {TripCode} to device {Serial}", tripCode, device.Serial);
            notify.InApp(NotificationCategory.Hardware, "Device limits not synced",
                $"Temperature/humidity limits for {tripCode} could not be written to device {device.Serial}. Use \"Sync limits\" on the device once it is online.",
                nameof(Device), device.Id, requiresReview: true);
            await db.SaveChangesAsync(ct);
            return false;
        }
    }
}

/// Stakeholder emails for dispatch and delivery.
internal sealed class TripNotifications(INotificationComposer notify, IOptions<AppOptions> app)
{
    public void Dispatched(Trip trip)
    {
        var model = BaseModel(trip);
        model["ExpectedArrival"] = Wat(trip.ExpectedArrival);
        model["StartedAt"] = Wat(trip.StartedAt ?? trip.LoadingTime);

        if (trip.AgroProcessor?.PrimaryContact is { } contact)
        {
            notify.Email(contact.Email, EmailTemplates.TripDispatched, With(model, contact.FullName));
        }
        if (trip.LogisticsPartner is { } partner)
        {
            notify.Email(partner.Email, EmailTemplates.TripDispatched, With(model, partner.ContactPerson));
        }
        notify.InApp(NotificationCategory.Partner, "Shipment dispatched",
            $"{trip.TripCode} left {trip.OriginLabel} for {trip.DestinationLabel} on {trip.Vehicle?.FleetNumber}.",
            nameof(Trip), trip.Id, trip.LogisticsPartnerId);
    }

    public void Delivered(Trip trip, DateTimeOffset now)
    {
        var onTime = now <= trip.ExpectedArrival;
        var model = BaseModel(trip);
        model["DeliveredAt"] = Wat(now);
        model["OnTime"] = onTime ? "on time" : $"{Math.Max(1, (int)Math.Round((now - trip.ExpectedArrival).TotalHours))} hour(s) late";
        model["Distance"] = $"{trip.DistanceTravelledKm:0.#} km";
        model["Co2"] = $"{trip.Co2EmissionKg:0.#} kg";

        if (trip.AgroProcessor?.PrimaryContact is { } contact)
            notify.Email(contact.Email, EmailTemplates.TripDelivered, With(model, contact.FullName));
        if (trip.LogisticsPartner is { } partner)
            notify.Email(partner.Email, EmailTemplates.TripDelivered, With(model, partner.ContactPerson));

        notify.InApp(NotificationCategory.Partner, onTime ? "Shipment delivered on time" : "Shipment delivered late",
            $"Shipment {trip.TripCode} was delivered to {trip.DestinationLabel} by {trip.LogisticsPartner?.CompanyName ?? "the carrier"}.",
            nameof(Trip), trip.Id, trip.LogisticsPartnerId);
    }

    private Dictionary<string, string?> BaseModel(Trip trip) => new()
    {
        ["TripCode"] = trip.TripCode,
        ["FleetNumber"] = trip.Vehicle?.FleetNumber,
        ["VehicleCode"] = trip.Vehicle?.VehicleCode,
        ["Route"] = Text.Route(trip.OriginLabel, trip.DestinationLabel),
        ["Produce"] = string.Join(", ", trip.Produce.Select(p => p.ProduceType?.Name).OfType<string>()),
        ["Weight"] = $"{trip.EstimatedWeightTonnes:0.##} t",
        ["PartnerName"] = trip.LogisticsPartner?.CompanyName,
        ["ProcessorName"] = trip.AgroProcessor?.Name,
        ["DriverName"] = trip.Driver?.FullName ?? "To be confirmed",
        ["TemperatureRange"] = $"{trip.MinTemperature:0.#}–{trip.MaxTemperature:0.#} °C",
        ["HumidityRange"] = $"{trip.MinHumidity:0}–{trip.MaxHumidity:0} % RH",
        ["ActionUrl"] = null,
        ["DashboardUrl"] = app.Value.FrontendBaseUrl,
    };

    private static Dictionary<string, string?> With(Dictionary<string, string?> model, string name) => new(model) { ["Name"] = name };

    internal static string Wat(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(1)).ToString("dd MMM yyyy, HH:mm") + " WAT";
}
