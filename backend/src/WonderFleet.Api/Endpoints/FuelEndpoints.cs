using WonderFleet.Api.Setup;
using WonderFleet.Application.Features.Fuel;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Api.Endpoints;

internal static class FuelEndpoints
{
    public static void MapFuelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/fuel", "Fuel planning");

        // Wizard preview: how much fuel to send out with this truck, before the trip exists.
        group.MapPost("/estimate", (FuelEstimateRequest request, IFuelService service, CancellationToken ct) =>
            service.EstimateAsync(request, ct)).Validate<FuelEstimateRequest>();

        group.MapGet("/prices", (IFuelService service, CancellationToken ct) => service.GetPricesAsync(ct));

        group.MapPut("/prices/{fuelType}", (FuelType fuelType, UpdateFuelPriceRequest request, IFuelService service, CancellationToken ct) =>
            service.UpdatePriceAsync(fuelType, request, ct)).Validate<UpdateFuelPriceRequest>();

        var trips = app.AdminGroup("/trips", "Fuel planning");

        trips.MapGet("/{id:guid}/fuel", (Guid id, IFuelService service, CancellationToken ct) =>
            service.GetTripFuelAsync(id, ct));

        // Re-plans against live traffic and forecast, and stores the result.
        trips.MapPost("/{id:guid}/fuel/estimate", (Guid id, IFuelService service, CancellationToken ct) =>
            service.EstimateForTripAsync(id, persist: true, ct));

        trips.MapPost("/{id:guid}/fuel/actual", (Guid id, RecordFuelRequest request, IFuelService service, CancellationToken ct) =>
            service.RecordActualAsync(id, request, ct)).Validate<RecordFuelRequest>();
    }
}
