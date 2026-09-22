using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Models;
using WonderFleet.Application.Features.Telemetry;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Devices;

public sealed record RegisterDeviceRequest(
    string Serial, string FirebaseKey, DeviceKind Kind, Guid? ParentDeviceId, Guid? VehicleId, string? FirmwareVersion);

public sealed record UpdateDeviceRequest(DeviceKind Kind, Guid? ParentDeviceId, Guid? VehicleId, string? FirmwareVersion);

public sealed record DeviceListQuery : PageQuery
{
    public bool? Online { get; init; }
    /// Only master units that are not on an open trip (the wizard's "Select Device" list).
    public bool AvailableOnly { get; init; }
}

public sealed record DeviceDto(
    Guid Id, string Serial, string FirebaseKey, string Kind, Guid? ParentDeviceId, string? ParentSerial,
    Guid? VehicleId, string? VehicleCode, string? FleetNumber, string? FirmwareVersion, int? BatteryLevel,
    bool IsOnline, DateTimeOffset? LastSeenAt, DateTimeOffset? LastChangedAt, decimal? LastTemperature, decimal? LastHumidity,
    double? LastLatitude, double? LastLongitude, Guid? CurrentTripId, string? CurrentTripCode, string? CurrentRoute, string SensorStatus);

public sealed record UnknownDeviceKeyDto(string FirebaseKey, DateTimeOffset LastSeenAt);

/// Everything needed to explain why device data is or is not arriving.
public sealed record FirebaseStatusDto(
    bool PollingActive, string? DisabledReason, DateTimeOffset? LastPollAt, DateTimeOffset? LastSuccessfulPollAt,
    string? LastError, int NodesInLastPoll, IReadOnlyList<UnknownDeviceKeyDto> UnregisteredKeys);

public sealed class RegisterDeviceRequestValidator : AbstractValidator<RegisterDeviceRequest>
{
    public RegisterDeviceRequestValidator()
    {
        RuleFor(x => x.Serial).NotEmpty().MaximumLength(40).Matches("^[A-Za-z0-9-]+$")
            .WithMessage("Serial may contain letters, digits and dashes (e.g. S-108).");
        RuleFor(x => x.FirebaseKey).NotEmpty().MaximumLength(100).Must(DeviceKeys.IsValid)
            .WithMessage("Firebase key may contain letters, digits, '-' and '_' and cannot be a reserved node (test, settings, vehicles).");
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.ParentDeviceId).NotEmpty().When(x => x.Kind == DeviceKind.SubUnit)
            .WithMessage("A sub-unit must be paired with a master unit.");
        RuleFor(x => x.FirmwareVersion).MaximumLength(40);
    }
}

public sealed class UpdateDeviceRequestValidator : AbstractValidator<UpdateDeviceRequest>
{
    public UpdateDeviceRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.ParentDeviceId).NotEmpty().When(x => x.Kind == DeviceKind.SubUnit);
        RuleFor(x => x.FirmwareVersion).MaximumLength(40);
    }
}

public static partial class DeviceKeys
{
    /// Nodes in the RTDB root that are not devices. "test" holds the hardware engineer's scratch data and must never be ingested.
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test", "settings", "vehicles" };

    [GeneratedRegex("^[A-Za-z0-9_-]{1,100}$")]
    private static partial Regex KeyPattern();

    public static bool IsValid(string? key) => !string.IsNullOrWhiteSpace(key) && KeyPattern().IsMatch(key) && !Reserved.Contains(key);
}

public interface IDeviceService
{
    Task<PagedResult<DeviceDto>> ListAsync(DeviceListQuery query, CancellationToken ct);
    Task<DeviceDto> GetAsync(Guid id, CancellationToken ct);
    Task<IdResponse> RegisterAsync(RegisterDeviceRequest request, CancellationToken ct);
    Task<DeviceDto> UpdateAsync(Guid id, UpdateDeviceRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    IReadOnlyList<UnknownDeviceKeyDto> GetUnknownKeys();
    FirebaseStatusDto GetFirebaseStatus();
    Task SyncThresholdsAsync(Guid id, CancellationToken ct);
}

internal sealed class DeviceService(
    IApplicationDbContext db,
    IDeviceCloudGateway cloud,
    IAuditLogger audit,
    TelemetrySyncState sync,
    ILogger<DeviceService> logger) : IDeviceService
{
    public async Task<PagedResult<DeviceDto>> ListAsync(DeviceListQuery query, CancellationToken ct)
    {
        var q = db.Devices.AsNoTracking();
        if (query.Online is { } online) q = q.Where(d => d.IsOnline == online);
        if (query.AvailableOnly)
            q = q.Where(d => d.Kind == DeviceKind.Master
                             && !db.Trips.Any(t => t.DeviceId == d.Id && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status)));
        if (query.NormalizedSearch is { } term)
            q = q.Where(d => d.Serial.ToLower().Contains(term) || d.FirebaseKey.ToLower().Contains(term)
                             || (d.Vehicle != null && (d.Vehicle.FleetNumber.ToLower().Contains(term) || d.Vehicle.VehicleCode.ToLower().Contains(term))));

        var total = await q.CountAsync(ct);
        var rows = await Project(q.OrderBy(d => d.Serial).Skip(query.Skip).Take(query.PageSize)).ToListAsync(ct);
        return new PagedResult<DeviceDto>(rows, query.Page, query.PageSize, total);
    }

    public async Task<DeviceDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.Devices.AsNoTracking().Where(d => d.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Device", id);

    public async Task<IdResponse> RegisterAsync(RegisterDeviceRequest request, CancellationToken ct)
    {
        var serial = request.Serial.Trim().ToUpperInvariant();
        var key = request.FirebaseKey.Trim();
        if (await db.Devices.AnyAsync(d => d.Serial == serial, ct))
            throw new ConflictException("A device with this serial already exists.", "device.duplicate_serial");
        if (await db.Devices.AnyAsync(d => d.FirebaseKey == key, ct))
            throw new ConflictException("Another device already uses this Firebase key.", "device.duplicate_key");

        var device = new Device { Serial = serial, FirebaseKey = key };
        await ApplyAsync(device, request.Kind, request.ParentDeviceId, request.VehicleId, request.FirmwareVersion, ct);
        db.Devices.Add(device);
        audit.Record("device.registered", nameof(Device), device.Id, new { serial, key });
        await db.SaveChangesAsync(ct);
        sync.KnownDevice(key);
        return new IdResponse(device.Id, serial);
    }

    public async Task<DeviceDto> UpdateAsync(Guid id, UpdateDeviceRequest request, CancellationToken ct)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Device", id);
        var openTrip = await db.Trips.FirstOrDefaultAsync(t => t.DeviceId == id && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct);
        if (openTrip is not null && request.VehicleId != openTrip.VehicleId)
            throw new BusinessRuleException("device.on_trip", "This device is monitoring an active trip; complete the trip before moving it.");

        await ApplyAsync(device, request.Kind, request.ParentDeviceId, request.VehicleId, request.FirmwareVersion, ct);
        audit.Record("device.updated", nameof(Device), id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Device", id);
        if (await db.Trips.AnyAsync(t => t.DeviceId == id, ct) || await db.SensorReadings.AnyAsync(r => r.DeviceId == id, ct))
            throw new BusinessRuleException("device.has_history",
                "This device has trip or telemetry history and cannot be deleted. Unassign it from its vehicle instead.");
        foreach (var child in await db.Devices.Where(d => d.ParentDeviceId == id).ToListAsync(ct)) child.ParentDeviceId = null;
        db.Devices.Remove(device);
        audit.Record("device.deleted", nameof(Device), id, new { device.Serial });
        await db.SaveChangesAsync(ct);
    }

    public IReadOnlyList<UnknownDeviceKeyDto> GetUnknownKeys() =>
        sync.UnknownDeviceKeys.Where(k => !DeviceKeys.Reserved.Contains(k.Key))
            .OrderByDescending(k => k.Value).Select(k => new UnknownDeviceKeyDto(k.Key, k.Value)).ToList();

    public FirebaseStatusDto GetFirebaseStatus() => new(
        sync.PollingStarted, sync.DisabledReason, sync.LastPollAt, sync.LastSuccessfulPollAt,
        sync.LastError, sync.LastPollNodeCount, GetUnknownKeys());

    public async Task SyncThresholdsAsync(Guid id, CancellationToken ct)
    {
        var device = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Device", id);
        var trip = await db.Trips.AsNoTracking()
            .FirstOrDefaultAsync(t => t.DeviceId == id && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct)
            ?? throw new BusinessRuleException("device.no_trip", "This device is not assigned to an active trip.");
        try
        {
            await cloud.PushThresholdsAsync(device.FirebaseKey, trip.Thresholds, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Threshold push failed for device {Serial}", device.Serial);
            throw new ExternalServiceException("Firebase", "Could not update the device limits. Try again shortly.");
        }
        audit.Record("device.thresholds_pushed", nameof(Device), id, new { trip.TripCode });
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Device device, DeviceKind kind, Guid? parentId, Guid? vehicleId, string? firmware, CancellationToken ct)
    {
        if (kind == DeviceKind.SubUnit)
        {
            if (parentId == device.Id)
                throw RequestValidationException.For("ParentDeviceId", "A device cannot be its own master.");
            var parent = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == parentId, ct)
                ?? throw RequestValidationException.For("ParentDeviceId", "Master unit not found.");
            if (parent.Kind != DeviceKind.Master)
                throw RequestValidationException.For("ParentDeviceId", "Sub-units can only be paired with a master unit.");
            // A sub-unit rides in the same truck as its master.
            vehicleId ??= parent.VehicleId;
        }
        if (vehicleId is { } vid && !await db.Vehicles.AnyAsync(v => v.Id == vid && v.DeletedAt == null, ct))
            throw RequestValidationException.For("VehicleId", "Vehicle not found.");

        device.Kind = kind;
        device.ParentDeviceId = kind == DeviceKind.SubUnit ? parentId : null;
        device.VehicleId = vehicleId;
        device.FirmwareVersion = Text.Trimmed(firmware);
    }

    private IQueryable<DeviceDto> Project(IQueryable<Device> q) => q.Select(d => new
    {
        d,
        Parent = db.Devices.Where(p => p.Id == d.ParentDeviceId).Select(p => p.Serial).FirstOrDefault(),
        Trip = db.Trips.Where(t => t.DeviceId == d.Id && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status))
            .Select(t => new { t.Id, t.TripCode, t.OriginLabel, t.DestinationLabel, t.SensorStatus }).FirstOrDefault(),
    })
    .Select(x => new DeviceDto(
        x.d.Id, x.d.Serial, x.d.FirebaseKey, x.d.Kind.ToString(), x.d.ParentDeviceId, x.Parent,
        x.d.VehicleId, x.d.Vehicle != null ? x.d.Vehicle.VehicleCode : null, x.d.Vehicle != null ? x.d.Vehicle.FleetNumber : null,
        x.d.FirmwareVersion, x.d.BatteryLevel, x.d.IsOnline, x.d.LastSeenAt, x.d.LastChangedAt,
        x.d.LastTemperature, x.d.LastHumidity, x.d.LastLatitude, x.d.LastLongitude,
        x.Trip != null ? (Guid?)x.Trip.Id : null, x.Trip != null ? x.Trip.TripCode : null,
        x.Trip != null ? x.Trip.OriginLabel + " → " + x.Trip.DestinationLabel : null,
        x.Trip != null ? x.Trip.SensorStatus.ToString() : (x.d.IsOnline ? "Normal" : "Offline")));
}
