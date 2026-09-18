using WonderFleet.Domain.Common;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Domain.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public ActorType ActorType { get; set; }
    public string? ActorId { get; set; }
    public string Action { get; set; } = default!;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// Output of "WonderFleet Route AI": Google Routes alternatives + weather, ranked by OpenAI.
public class RouteRecommendation : Entity
{
    public Guid? TripId { get; set; }
    public string Origin { get; set; } = default!;
    public string Destination { get; set; } = default!;
    public DateTimeOffset RequestedDeparture { get; set; }
    public DateTimeOffset? RecommendedDeparture { get; set; }
    public int SelectedRouteIndex { get; set; }
    public string Summary { get; set; } = default!;
    public string HeatRiskLevel { get; set; } = default!;
    public double DistanceKm { get; set; }
    public int DurationMinutes { get; set; }
    public string? EncodedPolyline { get; set; }
    public decimal EstimatedSpoilageReductionPct { get; set; }
    public string RoutesJson { get; set; } = "[]";
    public string AdviceJson { get; set; } = "{}";
    public string Model { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
}
