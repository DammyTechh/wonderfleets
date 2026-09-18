using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Helpers;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Models;
using WonderFleet.Application.Features.Fuel;
using WonderFleet.Application.Features.Notifications;
using WonderFleet.Application.Features.Telemetry;
using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;

namespace WonderFleet.Application.Features.Fleet;

public interface IFleetService
{
    Task<PagedResult<FleetRowDto>> ListAsync(FleetListQuery query, CancellationToken ct);
    Task<VehicleDetailDto> GetVehicleAsync(Guid vehicleId, CancellationToken ct);
    Task<NextVehicleCodeDto> NextVehicleCodeAsync(CancellationToken ct);
    Task<SuggestedThresholdsDto> SuggestThresholdsAsync(IReadOnlyList<Guid> produceTypeIds, CancellationToken ct);
    Task<FleetCreatedDto> CreateAsync(CreateFleetRequest request, CancellationToken ct);
    Task<VehicleDetailDto> UpdateVehicleAsync(Guid vehicleId, UpdateVehicleRequest request, CancellationToken ct);
    Task DeleteVehicleAsync(Guid vehicleId, CancellationToken ct);
}

internal sealed class FleetService(
    IApplicationDbContext db,
    ICodeGenerator codes,
    ICurrentActor actor,
    IClock clock,
    IAuditLogger audit,
    IMapsService maps,
    INotificationComposer notify,
    ThresholdPublisher thresholds,
    TripNotifications tripNotifications,
    ITripService trips,
    IFuelService fuel,
    ILogger<FleetService> logger) : IFleetService
{
    public async Task<PagedResult<FleetRowDto>> ListAsync(FleetListQuery query, CancellationToken ct)
    {
        var vehicles = db.Vehicles.AsNoTracking().Where(v => v.DeletedAt == null);
        if (query.PartnerId is { } pid) vehicles = vehicles.Where(v => v.LogisticsPartnerId == pid);
        if (query.NormalizedSearch is { } term)
            vehicles = vehicles.Where(v => v.VehicleCode.ToLower().Contains(term) || v.FleetNumber.ToLower().Contains(term)
                || v.LicenseNumber.ToLower().Contains(term) || v.LogisticsPartner!.CompanyName.ToLower().Contains(term)
                || db.Trips.Any(t => t.VehicleId == v.Id && t.Driver != null && t.Driver.FullName.ToLower().Contains(term)));

        var projected = vehicles.Select(v => new
        {
            v.Id, v.VehicleCode, v.FleetNumber, v.VehicleType, v.CapacityTonnes, v.LogisticsPartnerId, v.Status, v.CreatedAt,
            PartnerName = v.LogisticsPartner!.CompanyName,
            Trip = db.Trips.Where(t => t.VehicleId == v.Id)
                .OrderByDescending(t => t.CompletedAt == null && t.CancelledAt == null)
                .ThenByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    t.Id, t.TripCode, t.Status, t.SensorStatus, t.OriginLabel, t.DestinationLabel, t.LastTemperature, t.LastHumidity,
                    Driver = t.Driver != null ? t.Driver.FullName : null,
                    Open = t.CompletedAt == null && t.CancelledAt == null,
                })
                .FirstOrDefault(),
        });

        if (query.Status is { } sensor) projected = projected.Where(x => x.Trip != null && x.Trip.Open && x.Trip.SensorStatus == sensor);
        if (query.TripStatus is { } ts) projected = projected.Where(x => x.Trip != null && x.Trip.Status == ts);

        var total = await projected.CountAsync(ct);
        var rows = await projected
            .OrderByDescending(x => x.Trip != null && x.Trip.Open)
            .ThenByDescending(x => x.CreatedAt)
            .Skip(query.Skip).Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<FleetRowDto>(rows.Select(r =>
        {
            var open = r.Trip is { Open: true };
            return new FleetRowDto(r.Id, r.VehicleCode, r.FleetNumber, r.VehicleType, r.CapacityTonnes, r.LogisticsPartnerId, r.PartnerName,
                r.Trip?.Id, r.Trip?.TripCode, r.Trip?.Status.ToString(),
                open ? r.Trip!.Driver : null, open ? Text.Initials(r.Trip!.Driver) : null,
                r.Trip is null ? null : Text.Route(r.Trip.OriginLabel, r.Trip.DestinationLabel),
                open ? r.Trip!.LastTemperature : null, open ? r.Trip!.LastHumidity : null,
                open ? r.Trip!.SensorStatus.ToString() : "Idle", r.Status.ToString());
        }).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<VehicleDetailDto> GetVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        var v = await db.Vehicles.AsNoTracking().Include(x => x.LogisticsPartner)
            .FirstOrDefaultAsync(x => x.Id == vehicleId && x.DeletedAt == null, ct)
            ?? throw new NotFoundException("Vehicle", vehicleId);

        var devices = await db.Devices.AsNoTracking().Where(d => d.VehicleId == vehicleId).OrderBy(d => d.Kind).ThenBy(d => d.Serial)
            .Select(d => new VehicleDeviceDto(d.Id, d.Serial, d.Kind.ToString(), d.IsOnline, d.BatteryLevel, d.LastSeenAt))
            .ToListAsync(ct);

        var currentId = await db.Trips.AsNoTracking()
            .Where(t => t.VehicleId == vehicleId && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status))
            .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        var current = currentId is { } cid ? await trips.GetAsync(cid, ct) : null;

        var history = await trips.ListAsync(new TripListQuery { PageSize = 20, VehicleId = vehicleId }, ct);

        return new VehicleDetailDto(v.Id, v.VehicleCode, v.FleetNumber, v.LicenseNumber, v.VehicleType, v.CapacityTonnes,
            v.YearOfManufacture, v.LogisticsPartnerId, v.LogisticsPartner!.CompanyName, v.Status.ToString(),
            v.FuelType.ToString(), v.TankCapacityLitres, v.BaselineConsumptionLPer100Km,
            devices, current, history.Items);
    }

    public async Task<NextVehicleCodeDto> NextVehicleCodeAsync(CancellationToken ct) =>
        new(await codes.PeekVehicleCodeAsync(ct));

    public async Task<SuggestedThresholdsDto> SuggestThresholdsAsync(IReadOnlyList<Guid> produceTypeIds, CancellationToken ct)
    {
        var produce = await db.ProduceTypes.AsNoTracking().Where(p => produceTypeIds.Contains(p.Id)).ToListAsync(ct);
        return Suggest(produce);
    }

    public async Task<FleetCreatedDto> CreateAsync(CreateFleetRequest request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var s = request.Shipment;

        // ---- vehicle
        Vehicle vehicle;
        var isNewVehicle = request.ExistingVehicleId is null;
        if (request.ExistingVehicleId is { } existingId)
        {
            vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == existingId && v.DeletedAt == null, ct)
                ?? throw RequestValidationException.For("ExistingVehicleId", "Vehicle not found.");
            if (vehicle.Status != VehicleStatus.Active)
                throw new BusinessRuleException("vehicle.not_active", $"{vehicle.FleetNumber} is {vehicle.Status} and cannot take a shipment.");
            if (await db.Trips.AnyAsync(t => t.VehicleId == existingId && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct))
                throw new BusinessRuleException("vehicle.busy", $"{vehicle.FleetNumber} already has a shipment in progress.");
        }
        else
        {
            var v = request.Vehicle!;
            await EnsureVehicleUniqueAsync(v.FleetNumber, v.LicenseNumber, null, ct);
            vehicle = new Vehicle
            {
                VehicleCode = await codes.NextVehicleCodeAsync(ct),
                FleetNumber = v.FleetNumber.Trim().ToUpperInvariant(),
                LicenseNumber = NormalizePlate(v.LicenseNumber),
                VehicleType = v.VehicleType.Trim(),
                CapacityTonnes = v.CapacityTonnes,
                YearOfManufacture = v.YearOfManufacture,
                LogisticsPartnerId = v.LogisticsPartnerId,
                FuelType = v.FuelType ?? FuelType.Diesel,
                TankCapacityLitres = v.TankCapacityLitres,
                BaselineConsumptionLPer100Km = v.BaselineLitresPer100Km,
            };
            db.Vehicles.Add(vehicle);
        }

        // ---- parties
        var partner = await db.LogisticsPartners.FirstOrDefaultAsync(p => p.Id == vehicle.LogisticsPartnerId && p.DeletedAt == null, ct)
            ?? throw RequestValidationException.For("Vehicle.LogisticsPartnerId", "Select an existing logistics partner.");
        if (partner.Status != PartnerStatus.Active)
            throw new BusinessRuleException("partner.not_active", $"{partner.CompanyName} is {partner.Status}. Activate the partner first.");

        var processor = await db.AgroProcessors.Include(p => p.Contacts)
            .FirstOrDefaultAsync(p => p.Id == s.AgroProcessorId && p.DeletedAt == null, ct)
            ?? throw RequestValidationException.For("Shipment.AgroProcessorId", "Select an existing agro-processor.");
        if (processor.Status != PartnerStatus.Active)
            throw new BusinessRuleException("processor.not_active", $"{processor.Name} is {processor.Status}. Activate the processor first.");

        if (s.EstimatedWeightTonnes > vehicle.CapacityTonnes)
            throw RequestValidationException.For("Shipment.EstimatedWeightTonnes",
                $"Estimated weight ({s.EstimatedWeightTonnes:0.##} t) exceeds the vehicle capacity ({vehicle.CapacityTonnes:0.##} t).");

        var driver = request.DriverId is { } did ? await LoadAssignableDriverAsync(did, partner.Id, null, ct) : null;
        var device = request.DeviceId is { } devId ? await LoadAssignableDeviceAsync(devId, null, ct) : null;

        // ---- produce (existing chips + "Add a produce")
        var produce = await ResolveProduceAsync(s.ProduceTypeIds ?? [], s.NewProduce ?? [], ct);

        // ---- thresholds: explicit, else derived from produce defaults
        var limits = request.Thresholds ?? Suggest(produce).Thresholds
            ?? throw RequestValidationException.For("Thresholds",
                "Set the temperature & humidity threshold: the selected produce has no compatible default range.");

        // ---- locations
        var pickupGeo = await LocateAsync(s.Pickup, ct);
        var destinationGeo = await LocateAsync(s.Destination, ct);

        var trip = new Trip
        {
            TripCode = await codes.NextTripCodeAsync(ct),
            VehicleId = vehicle.Id,
            Vehicle = vehicle,
            DriverId = driver?.Id,
            Driver = driver,
            DeviceId = device?.Id,
            LogisticsPartnerId = partner.Id,
            LogisticsPartner = partner,
            AgroProcessorId = processor.Id,
            AgroProcessor = processor,
            SensorStatus = SensorStatus.Offline,
            EstimatedWeightTonnes = s.EstimatedWeightTonnes,
            PackagingType = s.PackagingType,
            UnitCount = s.UnitCount,
            AdditionalNotes = Text.Trimmed(s.AdditionalNotes),
            PickupAddress = s.Pickup.Address.Trim(),
            OriginLabel = LocationLabels.From(s.Pickup, pickupGeo),
            PickupLatitude = pickupGeo?.Latitude,
            PickupLongitude = pickupGeo?.Longitude,
            DestinationAddress = s.Destination.Address.Trim(),
            DestinationLabel = LocationLabels.From(s.Destination, destinationGeo),
            DestinationLatitude = destinationGeo?.Latitude,
            DestinationLongitude = destinationGeo?.Longitude,
            LoadingTime = s.LoadingTime.ToUniversalTime(),
            ExpectedArrival = s.ExpectedArrival.ToUniversalTime(),
            CreatedByAdminId = actor.AdminId,
        };
        trip.SetThresholds(new CargoThresholds(limits.MinTemperature, limits.MaxTemperature, limits.MinHumidity, limits.MaxHumidity));
        foreach (var p in produce) trip.Produce.Add(new TripProduce { TripId = trip.Id, ProduceTypeId = p.Id, ProduceType = p });
        trip.PlannedDistanceKm = await PlannedDistanceAsync(pickupGeo, destinationGeo, trip.LoadingTime, now, ct);
        db.Trips.Add(trip);

        if (device is not null)
        {
            device.VehicleId = vehicle.Id;
            foreach (var sub in await db.Devices.Where(d => d.ParentDeviceId == device.Id).ToListAsync(ct)) sub.VehicleId = vehicle.Id;
        }

        if (request.StartImmediately)
        {
            trip.Start(now);
            if (driver is not null) driver.Status = DriverStatus.OnTrip;
            tripNotifications.Dispatched(trip);
        }

        notify.InApp(NotificationCategory.Hardware, isNewVehicle ? "New vehicle added" : "New shipment scheduled",
            isNewVehicle
                ? $"A {vehicle.CapacityTonnes:0.#}-ton {vehicle.VehicleType} {vehicle.VehicleCode} was registered to {partner.CompanyName}'s fleet{(device is null ? "." : $" and device {device.Serial} is assigned.")}"
                : $"{trip.TripCode} ({trip.OriginLabel} → {trip.DestinationLabel}) was scheduled on {vehicle.FleetNumber}.",
            nameof(Trip), trip.Id, partner.Id);
        audit.Record("fleet.created", nameof(Trip), trip.Id, new { vehicle.VehicleCode, trip.TripCode, isNewVehicle, device = device?.Serial });

        await db.SaveChangesAsync(ct);

        var pushed = device is not null && await thresholds.TryPushAsync(device, trip.Thresholds, trip.TripCode, ct);

        // Dispatch wants a fuel figure with the shipment; a planning failure must not fail the booking.
        FuelEstimateDto? fuelPlan = null;
        try
        {
            fuelPlan = await fuel.EstimateForTripAsync(trip.Id, persist: true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Fuel planning failed for {TripCode}; it can be re-run from the shipment page", trip.TripCode);
        }

        return new FleetCreatedDto(vehicle.Id, vehicle.VehicleCode, trip.Id, trip.TripCode, trip.Status.ToString(), pushed, limits, fuelPlan);
    }

    public async Task<VehicleDetailDto> UpdateVehicleAsync(Guid vehicleId, UpdateVehicleRequest request, CancellationToken ct)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId && v.DeletedAt == null, ct)
            ?? throw new NotFoundException("Vehicle", vehicleId);
        var busy = await db.Trips.AnyAsync(t => t.VehicleId == vehicleId && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct);
        if (busy && (request.LogisticsPartnerId != vehicle.LogisticsPartnerId || request.Status != VehicleStatus.Active))
            throw new BusinessRuleException("vehicle.busy", "Finish the current shipment before changing the partner or taking the vehicle out of service.");
        if (!await db.LogisticsPartners.AnyAsync(p => p.Id == request.LogisticsPartnerId && p.DeletedAt == null, ct))
            throw RequestValidationException.For("LogisticsPartnerId", "Select an existing logistics partner.");
        await EnsureVehicleUniqueAsync(request.FleetNumber, request.LicenseNumber, vehicleId, ct);

        vehicle.FleetNumber = request.FleetNumber.Trim().ToUpperInvariant();
        vehicle.LicenseNumber = NormalizePlate(request.LicenseNumber);
        vehicle.VehicleType = request.VehicleType.Trim();
        vehicle.CapacityTonnes = request.CapacityTonnes;
        vehicle.YearOfManufacture = request.YearOfManufacture;
        vehicle.LogisticsPartnerId = request.LogisticsPartnerId;
        vehicle.Status = request.Status;
        vehicle.FuelType = request.FuelType ?? vehicle.FuelType;
        vehicle.TankCapacityLitres = request.TankCapacityLitres;
        vehicle.BaselineConsumptionLPer100Km = request.BaselineLitresPer100Km;
        audit.Record("vehicle.updated", nameof(Vehicle), vehicleId);
        await db.SaveChangesAsync(ct);
        return await GetVehicleAsync(vehicleId, ct);
    }

    public async Task DeleteVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId && v.DeletedAt == null, ct)
            ?? throw new NotFoundException("Vehicle", vehicleId);
        if (await db.Trips.AnyAsync(t => t.VehicleId == vehicleId && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct))
            throw new BusinessRuleException("vehicle.busy", "This vehicle has a shipment in progress.");
        vehicle.DeletedAt = clock.UtcNow;
        vehicle.Status = VehicleStatus.Inactive;
        foreach (var d in await db.Devices.Where(d => d.VehicleId == vehicleId).ToListAsync(ct)) d.VehicleId = null;
        audit.Record("vehicle.deleted", nameof(Vehicle), vehicleId);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers (shared with TripService)

    internal async Task<Driver> LoadAssignableDriverAsync(Guid driverId, Guid partnerId, Guid? exceptTripId, CancellationToken ct) =>
        await AssignmentRules.LoadDriverAsync(db, driverId, partnerId, exceptTripId, ct);

    internal async Task<Device> LoadAssignableDeviceAsync(Guid deviceId, Guid? exceptTripId, CancellationToken ct) =>
        await AssignmentRules.LoadDeviceAsync(db, deviceId, exceptTripId, ct);

    private async Task EnsureVehicleUniqueAsync(string fleetNumber, string license, Guid? exceptId, CancellationToken ct)
    {
        var fleet = fleetNumber.Trim().ToUpperInvariant();
        var plate = NormalizePlate(license);
        var others = db.Vehicles.Where(v => v.DeletedAt == null && v.Id != exceptId);
        if (await others.AnyAsync(v => v.FleetNumber.ToUpper() == fleet, ct))
            throw new ConflictException($"Fleet number {fleet} is already registered.", "vehicle.duplicate_fleet_number");
        if (await others.AnyAsync(v => v.LicenseNumber.ToUpper() == plate, ct))
            throw new ConflictException($"Plate number {plate} is already registered.", "vehicle.duplicate_license");
    }

    private static readonly char[] PlateSeparators = [' ', '-'];

    private static string NormalizePlate(string plate) =>
        string.Join('-', plate.Trim().ToUpperInvariant().Split(PlateSeparators, StringSplitOptions.RemoveEmptyEntries));

    private async Task<List<ProduceType>> ResolveProduceAsync(IReadOnlyList<Guid> ids, IReadOnlyList<string> newNames, CancellationToken ct)
    {
        var distinctIds = ids.Distinct().ToList();
        var result = await db.ProduceTypes.Where(p => distinctIds.Contains(p.Id)).ToListAsync(ct);
        if (result.Count != distinctIds.Count)
            throw RequestValidationException.For("Shipment.ProduceTypeIds", "One or more selected produce types do not exist.");

        foreach (var raw in newNames.Select(ProduceService.Normalize).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (result.Any(p => p.Name.Equals(raw, StringComparison.OrdinalIgnoreCase))) continue;
            var lower = raw.ToLowerInvariant();
            var existing = await db.ProduceTypes.FirstOrDefaultAsync(p => p.Name.ToLower() == lower, ct);
            if (existing is null)
            {
                existing = new ProduceType { Name = raw };
                db.ProduceTypes.Add(existing);
                audit.Record("produce.created", nameof(ProduceType), existing.Id, new { name = raw });
            }
            result.Add(existing);
        }
        return result;
    }

    /// Intersection of the produce safe ranges (every item must stay safe).
    internal static SuggestedThresholdsDto Suggest(IReadOnlyCollection<ProduceType> produce)
    {
        var notes = new List<string>();
        var withTemp = produce.Where(p => p.DefaultMinTemperature.HasValue && p.DefaultMaxTemperature.HasValue).ToList();
        var withHum = produce.Where(p => p.DefaultMinHumidity.HasValue && p.DefaultMaxHumidity.HasValue).ToList();
        if (withTemp.Count == 0 || withHum.Count == 0)
            return new SuggestedThresholdsDto(null, "none", ["No default range is known for the selected produce. Enter limits manually."]);

        var minT = withTemp.Max(p => p.DefaultMinTemperature!.Value);
        var maxT = withTemp.Min(p => p.DefaultMaxTemperature!.Value);
        var minH = withHum.Max(p => p.DefaultMinHumidity!.Value);
        var maxH = withHum.Min(p => p.DefaultMaxHumidity!.Value);

        if (minT >= maxT || minH >= maxH)
            return new SuggestedThresholdsDto(null, "conflict",
                ["The selected produce need incompatible storage conditions; consider separate trucks or enter a compromise range manually."]);

        if (produce.Count > withTemp.Count) notes.Add("Some produce have no default range and were ignored.");
        notes.Add("Prototype ranges per the engineering concept note; adjust to your cooling capability.");
        return new SuggestedThresholdsDto(new ThresholdInput(minT, maxT, minH, maxH), produce.Count > 1 ? "intersection" : "produce_default", notes);
    }

    private async Task<GeocodeLike?> LocateAsync(LocationInput input, CancellationToken ct)
    {
        if (input.Latitude is { } lat && input.Longitude is { } lng) return new GeocodeLike(null, lat, lng);
        try
        {
            var geo = await maps.GeocodeAsync(input.Address.Trim(), ct);
            return geo is null ? null : new GeocodeLike(geo.City, geo.Location.Latitude, geo.Location.Longitude);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Geocoding failed for a trip address; continuing without coordinates");
            return null;
        }
    }

    private async Task<double?> PlannedDistanceAsync(GeocodeLike? from, GeocodeLike? to, DateTimeOffset loading, DateTimeOffset now, CancellationToken ct)
    {
        if (from is null || to is null) return null;
        try
        {
            var departure = loading > now.AddMinutes(2) ? loading : now.AddMinutes(2);
            var routes = await maps.ComputeRoutesAsync(new GeoPoint(from.Latitude, from.Longitude), new GeoPoint(to.Latitude, to.Longitude), departure, ct);
            return routes.Count == 0 ? null : Math.Round(routes[0].DistanceKm, 1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Route distance lookup failed; planned distance left empty");
            return null;
        }
    }
}

internal static class AssignmentRules
{
    public static async Task<Driver> LoadDriverAsync(IApplicationDbContext db, Guid driverId, Guid partnerId, Guid? exceptTripId, CancellationToken ct)
    {
        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.Id == driverId && d.DeletedAt == null, ct)
            ?? throw RequestValidationException.For("DriverId", "Driver not found.");
        if (driver.LogisticsPartnerId != partnerId)
            throw RequestValidationException.For("DriverId", "The driver does not belong to this vehicle's logistics partner.");
        if (driver.Status == DriverStatus.OffDuty)
            throw new BusinessRuleException("driver.off_duty", $"{driver.FullName} is off duty.");
        if (await db.Trips.AnyAsync(t => t.DriverId == driverId && t.Id != exceptTripId && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct))
            throw new BusinessRuleException("driver.busy", $"{driver.FullName} is already assigned to another shipment.");
        return driver;
    }

    public static async Task<Device> LoadDeviceAsync(IApplicationDbContext db, Guid deviceId, Guid? exceptTripId, CancellationToken ct)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct)
            ?? throw RequestValidationException.For("DeviceId", "Device not found.");
        if (device.Kind != DeviceKind.Master)
            throw RequestValidationException.For("DeviceId", "Assign the master unit; sub-units follow their master automatically.");
        if (await db.Trips.AnyAsync(t => t.DeviceId == deviceId && t.Id != exceptTripId && TelemetryIngestionService.OpenTripStatuses.Contains(t.Status), ct))
            throw new BusinessRuleException("device.busy", $"Device {device.Serial} is already monitoring another shipment.");
        return device;
    }
}
