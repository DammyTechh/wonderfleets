using System.Text;
using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Domain.Entities;

namespace WonderFleet.Infrastructure.Persistence;

/// EF Core unit of work. The schema is owned by db/migrations/*.sql (EF migrations are not used),
/// so this model only has to match table/column names and types.
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LogisticsPartner> LogisticsPartners => Set<LogisticsPartner>();
    public DbSet<PartnerTruckType> PartnerTruckTypes => Set<PartnerTruckType>();
    public DbSet<PartnerCorridor> PartnerCorridors => Set<PartnerCorridor>();
    public DbSet<PartnerDocument> PartnerDocuments => Set<PartnerDocument>();
    public DbSet<AgroProcessor> AgroProcessors => Set<AgroProcessor>();
    public DbSet<AgroProcessorContact> AgroProcessorContacts => Set<AgroProcessorContact>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<ProduceType> ProduceTypes => Set<ProduceType>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripProduce> TripProduce => Set<TripProduce>();
    public DbSet<SensorReading> SensorReadings => Set<SensorReading>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<NotificationChannelSetting> NotificationChannelSettings => Set<NotificationChannelSetting>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();
    public DbSet<ShareLinkTrip> ShareLinkTrips => Set<ShareLinkTrip>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RouteRecommendation> RouteRecommendations => Set<RouteRecommendation>();
    public DbSet<FuelPrice> FuelPrices => Set<FuelPrice>();
    public DbSet<FuelEstimate> FuelEstimates => Set<FuelEstimate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                // Anything a configuration named explicitly (xmin, jsonb payloads, awkward
                // abbreviations) keeps that name: only defaults are snake_cased.
                if (!string.Equals(property.GetColumnName(), property.Name, StringComparison.Ordinal)) continue;

                property.SetColumnName(ToSnakeCase(property.Name));

                // Enums are stored as text (matching the CHECK constraints in the migration).
                var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clr.IsEnum) property.SetProviderClrType(typeof(string));
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])
                              || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
