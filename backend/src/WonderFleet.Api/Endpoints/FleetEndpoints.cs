using WonderFleet.Api.Setup;
using WonderFleet.Application.Features.Fleet;
using WonderFleet.Application.Features.ShareLinks;
using WonderFleet.Application.Features.Tracking;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Api.Endpoints;

internal static class FleetEndpoints
{
    public static void MapFleetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/fleet", "Fleet management");

        group.MapGet("/", (IFleetService service, CancellationToken ct,
                string? search, Guid? partnerId, SensorStatus? status, TripStatus? tripStatus, int page = 1, int pageSize = 10) =>
            service.ListAsync(new FleetListQuery
            {
                Search = search, PartnerId = partnerId, Status = status, TripStatus = tripStatus, Page = page, PageSize = pageSize,
            }, ct));

        // "Add a Fleet" wizard support: the vehicle code shown on the first tab.
        group.MapGet("/next-vehicle-code", (IFleetService service, CancellationToken ct) => service.NextVehicleCodeAsync(ct));

        // Suggested temperature/humidity limits derived from the selected produce.
        group.MapGet("/suggested-thresholds", (IFleetService service, CancellationToken ct, Guid[]? produceTypeId) =>
            service.SuggestThresholdsAsync(produceTypeId ?? [], ct));

        group.MapPost("/", async (CreateFleetRequest request, IFleetService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"{EndpointRegistration.BasePath}/trips/{created.TripId}", created);
            })
            .Validate<CreateFleetRequest>();

        group.MapGet("/vehicles/{vehicleId:guid}", (Guid vehicleId, IFleetService service, CancellationToken ct) =>
            service.GetVehicleAsync(vehicleId, ct));

        group.MapPut("/vehicles/{vehicleId:guid}", (Guid vehicleId, UpdateVehicleRequest request, IFleetService service, CancellationToken ct) =>
            service.UpdateVehicleAsync(vehicleId, request, ct)).Validate<UpdateVehicleRequest>();

        group.MapDelete("/vehicles/{vehicleId:guid}", async (Guid vehicleId, IFleetService service, CancellationToken ct) =>
        {
            await service.DeleteVehicleAsync(vehicleId, ct);
            return Results.NoContent();
        });
    }

    public static void MapTripEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/trips", "Shipments");

        group.MapGet("/", (ITripService service, CancellationToken ct,
                string? search, TripStatus? status, Guid? partnerId, Guid? agroProcessorId, Guid? vehicleId,
                DateTimeOffset? from, DateTimeOffset? to, bool openOnly = false, int page = 1, int pageSize = 10) =>
            service.ListAsync(new TripListQuery
            {
                Search = search, Status = status, PartnerId = partnerId, AgroProcessorId = agroProcessorId, VehicleId = vehicleId,
                From = from, To = to, OpenOnly = openOnly, Page = page, PageSize = pageSize,
            }, ct));

        group.MapGet("/{id:guid}", (Guid id, ITripService service, CancellationToken ct) => service.GetAsync(id, ct));

        group.MapPost("/{id:guid}/start", (Guid id, ITripService service, CancellationToken ct) => service.StartAsync(id, ct));

        group.MapPost("/{id:guid}/complete", (Guid id, ITripService service, CancellationToken ct) => service.CompleteAsync(id, ct));

        group.MapPost("/{id:guid}/cancel", (Guid id, CancelTripRequest request, ITripService service, CancellationToken ct) =>
            service.CancelAsync(id, request, ct)).Validate<CancelTripRequest>();

        group.MapPut("/{id:guid}/thresholds", (Guid id, UpdateThresholdsRequest request, ITripService service, CancellationToken ct) =>
            service.UpdateThresholdsAsync(id, request, ct)).Validate<UpdateThresholdsRequest>();

        group.MapPut("/{id:guid}/assignment", (Guid id, AssignTripResourcesRequest request, ITripService service, CancellationToken ct) =>
            service.AssignAsync(id, request, ct)).Validate<AssignTripResourcesRequest>();
    }

    public static void MapTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/tracking", "Live tracking");

        group.MapGet("/live", (ITrackingService service, CancellationToken ct) => service.GetLiveAsync(ct));

        group.MapGet("/trips/{tripId:guid}", (Guid tripId, ITrackingService service, CancellationToken ct, int hours = 12) =>
            service.GetTripTrackAsync(tripId, hours, ct));
    }

    public static void MapShareLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/share-links", "Partner access");

        // "Generate tracking link": a temporary link that expires after the trip. No login required.
        group.MapPost("/", async (CreateShareLinkRequest request, IShareLinkService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"{EndpointRegistration.BasePath}/share-links/{created.Id}", created);
            })
            .Validate<CreateShareLinkRequest>();

        group.MapGet("/", (IShareLinkService service, CancellationToken ct,
                Guid? partnerId, ShareAudience? audience, bool activeOnly = false) =>
            service.ListAsync(new ShareLinkQuery(partnerId, audience, activeOnly), ct));

        group.MapPost("/{id:guid}/revoke", async (Guid id, RevokeShareLinkRequest? request, IShareLinkService service, CancellationToken ct) =>
        {
            await service.RevokeAsync(id, request?.Reason, ct);
            return Results.NoContent();
        });
    }
}
