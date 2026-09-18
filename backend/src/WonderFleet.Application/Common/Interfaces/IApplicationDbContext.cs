using Microsoft.EntityFrameworkCore;
using WonderFleet.Domain.Entities;

namespace WonderFleet.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<AdminUser> AdminUsers { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<LogisticsPartner> LogisticsPartners { get; }
    DbSet<PartnerTruckType> PartnerTruckTypes { get; }
    DbSet<PartnerCorridor> PartnerCorridors { get; }
    DbSet<PartnerDocument> PartnerDocuments { get; }
    DbSet<AgroProcessor> AgroProcessors { get; }
    DbSet<AgroProcessorContact> AgroProcessorContacts { get; }
    DbSet<Driver> Drivers { get; }
    DbSet<Vehicle> Vehicles { get; }
    DbSet<Device> Devices { get; }
    DbSet<ProduceType> ProduceTypes { get; }
    DbSet<Trip> Trips { get; }
    DbSet<TripProduce> TripProduce { get; }
    DbSet<SensorReading> SensorReadings { get; }
    DbSet<AlertRule> AlertRules { get; }
    DbSet<NotificationChannelSetting> NotificationChannelSettings { get; }
    DbSet<Alert> Alerts { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<NotificationDelivery> NotificationDeliveries { get; }
    DbSet<ShareLink> ShareLinks { get; }
    DbSet<ShareLinkTrip> ShareLinkTrips { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<RouteRecommendation> RouteRecommendations { get; }
    DbSet<FuelPrice> FuelPrices { get; }
    DbSet<FuelEstimate> FuelEstimates { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// Human-readable codes backed by PostgreSQL sequences.
public interface ICodeGenerator
{
    Task<string> NextAdminCodeAsync(CancellationToken ct);
    Task<string> NextPartnerCodeAsync(CancellationToken ct);
    Task<string> NextProcessorCodeAsync(CancellationToken ct);
    Task<string> NextVehicleCodeAsync(CancellationToken ct);
    Task<string> PeekVehicleCodeAsync(CancellationToken ct);
    Task<string> NextTripCodeAsync(CancellationToken ct);
}
