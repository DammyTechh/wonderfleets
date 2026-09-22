using WonderFleet.Api.Setup;
using WonderFleet.Application.Common.Models;
using WonderFleet.Application.Features.Devices;
using WonderFleet.Application.Features.Fleet;
using WonderFleet.Application.Features.Partners;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Api.Endpoints;

internal static class PartnerEndpoints
{
    public static void MapPartnerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/logistics-partners", "Logistics partners");

        group.MapGet("/", (ILogisticsPartnerService service, CancellationToken ct,
                string? search, PartnerStatus? status, int page = 1, int pageSize = 10) =>
            service.ListAsync(new PartnerListQuery { Search = search, Status = status, Page = page, PageSize = pageSize }, ct));

        group.MapGet("/export", async (ILogisticsPartnerService service, CancellationToken ct, string? search, PartnerStatus? status) =>
            (await service.ExportAsync(new PartnerListQuery { Search = search, Status = status }, ct)).ToResult());

        group.MapGet("/{id:guid}", (Guid id, ILogisticsPartnerService service, CancellationToken ct) => service.GetAsync(id, ct));

        group.MapPost("/", async (LogisticsPartnerRequest request, ILogisticsPartnerService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"{EndpointRegistration.BasePath}/logistics-partners/{created.Id}", created);
            })
            .Validate<LogisticsPartnerRequest>();

        group.MapPut("/{id:guid}", (Guid id, LogisticsPartnerRequest request, ILogisticsPartnerService service, CancellationToken ct) =>
            service.UpdateAsync(id, request, ct)).Validate<LogisticsPartnerRequest>();

        group.MapPost("/{id:guid}/status", async (Guid id, ChangePartnerStatusRequest request, ILogisticsPartnerService service, CancellationToken ct) =>
        {
            await service.ChangeStatusAsync(id, request, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, ILogisticsPartnerService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/photo", async (Guid id, IFormFile file, ILogisticsPartnerService service, CancellationToken ct) =>
            Results.Ok(new { photoUrl = await service.UploadPhotoAsync(id, file.ToUploadedFile(), ct) })).DisableAntiforgery();

        // Compliance & documentation (CAC certificate, insurance policy, driver list, vehicle licence)
        group.MapPost("/{id:guid}/documents", async (Guid id, IFormFile file, PartnerDocumentType documentType,
                ILogisticsPartnerService service, CancellationToken ct, DateOnly? expiresOn) =>
                Results.Ok(await service.UploadDocumentAsync(
                    id, new UploadPartnerDocumentRequest(documentType, expiresOn), file.ToUploadedFile(), ct)))
            .DisableAntiforgery();

        group.MapPost("/{id:guid}/documents/{documentId:guid}/verify", (Guid id, Guid documentId,
                VerifyDocumentRequest request, ILogisticsPartnerService service, CancellationToken ct) =>
            service.VerifyDocumentAsync(id, documentId, request, ct));

        group.MapGet("/{id:guid}/documents/{documentId:guid}/download",
            async (Guid id, Guid documentId, ILogisticsPartnerService service, CancellationToken ct) =>
            {
                var file = await service.DownloadDocumentAsync(id, documentId, ct);
                return Results.File(file.Content, file.ContentType, file.FileName);
            });

        group.MapDelete("/{id:guid}/documents/{documentId:guid}", async (Guid id, Guid documentId,
            ILogisticsPartnerService service, CancellationToken ct) =>
        {
            await service.DeleteDocumentAsync(id, documentId, ct);
            return Results.NoContent();
        });
    }

    public static void MapProcessorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/agro-processors", "Agro-processors");

        group.MapGet("/", (IAgroProcessorService service, CancellationToken ct,
                string? search, PartnerStatus? status, int page = 1, int pageSize = 10) =>
            service.ListAsync(new PartnerListQuery { Search = search, Status = status, Page = page, PageSize = pageSize }, ct));

        group.MapGet("/export", async (IAgroProcessorService service, CancellationToken ct, string? search, PartnerStatus? status) =>
            (await service.ExportAsync(new PartnerListQuery { Search = search, Status = status }, ct)).ToResult());

        group.MapGet("/{id:guid}", (Guid id, IAgroProcessorService service, CancellationToken ct) => service.GetAsync(id, ct));

        group.MapPost("/", async (AgroProcessorRequest request, IAgroProcessorService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"{EndpointRegistration.BasePath}/agro-processors/{created.Id}", created);
            })
            .Validate<AgroProcessorRequest>();

        group.MapPut("/{id:guid}", (Guid id, AgroProcessorRequest request, IAgroProcessorService service, CancellationToken ct) =>
            service.UpdateAsync(id, request, ct)).Validate<AgroProcessorRequest>();

        group.MapPost("/{id:guid}/status", async (Guid id, ChangePartnerStatusRequest request, IAgroProcessorService service, CancellationToken ct) =>
        {
            await service.ChangeStatusAsync(id, request, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IAgroProcessorService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/photo", async (Guid id, IFormFile file, IAgroProcessorService service, CancellationToken ct) =>
            Results.Ok(new { photoUrl = await service.UploadPhotoAsync(id, file.ToUploadedFile(), ct) })).DisableAntiforgery();
    }

    public static void MapDriverEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/drivers", "Drivers");

        group.MapGet("/", (IDriverService service, CancellationToken ct,
                string? search, Guid? partnerId, DriverStatus? status, bool availableOnly = false, int page = 1, int pageSize = 10) =>
            service.ListAsync(new DriverListQuery
            {
                Search = search, PartnerId = partnerId, Status = status, AvailableOnly = availableOnly, Page = page, PageSize = pageSize,
            }, ct));

        group.MapGet("/{id:guid}", (Guid id, IDriverService service, CancellationToken ct) => service.GetAsync(id, ct));

        group.MapPost("/", async (DriverRequest request, IDriverService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"{EndpointRegistration.BasePath}/drivers/{created.Id}", created);
            })
            .Validate<DriverRequest>();

        group.MapPut("/{id:guid}", (Guid id, DriverRequest request, IDriverService service, CancellationToken ct) =>
            service.UpdateAsync(id, request, ct)).Validate<DriverRequest>();

        group.MapDelete("/{id:guid}", async (Guid id, IDriverService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }

    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/devices", "Devices");

        group.MapGet("/", (IDeviceService service, CancellationToken ct,
                string? search, bool? online, bool availableOnly = false, int page = 1, int pageSize = 20) =>
            service.ListAsync(new DeviceListQuery
            {
                Search = search, Online = online, AvailableOnly = availableOnly, Page = page, PageSize = pageSize,
            }, ct));

        // Keys seen in Firebase that are not registered yet (helps onboarding new hardware).
        group.MapGet("/unknown-keys", (IDeviceService service) => Results.Ok(service.GetUnknownKeys()));

        // Polling state plus unregistered keys: why device data is, or is not, arriving.
        group.MapGet("/firebase-status", (IDeviceService service) => Results.Ok(service.GetFirebaseStatus()));

        group.MapGet("/{id:guid}", (Guid id, IDeviceService service, CancellationToken ct) => service.GetAsync(id, ct));

        group.MapPost("/", async (RegisterDeviceRequest request, IDeviceService service, CancellationToken ct) =>
            {
                var created = await service.RegisterAsync(request, ct);
                return Results.Created($"{EndpointRegistration.BasePath}/devices/{created.Id}", created);
            })
            .Validate<RegisterDeviceRequest>();

        group.MapPut("/{id:guid}", (Guid id, UpdateDeviceRequest request, IDeviceService service, CancellationToken ct) =>
            service.UpdateAsync(id, request, ct)).Validate<UpdateDeviceRequest>();

        group.MapPost("/{id:guid}/sync-thresholds", async (Guid id, IDeviceService service, CancellationToken ct) =>
        {
            await service.SyncThresholdsAsync(id, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IDeviceService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }

    public static void MapProduceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.AdminGroup("/produce-types", "Produce");

        group.MapGet("/", (IProduceService service, CancellationToken ct) => service.ListAsync(ct));

        group.MapPost("/", async (ProduceTypeRequest request, IProduceService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"{EndpointRegistration.BasePath}/produce-types/{created.Id}", created);
            })
            .Validate<ProduceTypeRequest>();

        group.MapPut("/{id:guid}", (Guid id, ProduceTypeRequest request, IProduceService service, CancellationToken ct) =>
            service.UpdateAsync(id, request, ct)).Validate<ProduceTypeRequest>();
    }
}
