using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public class VehicleService : IVehicleService
{
    private const string EntityType = "Vehicle";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public VehicleService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
    }

    private IQueryable<Vehicle> TenantScoped() =>
        _dbContext.Set<Vehicle>().Where(v => v.TenantId == _tenantContext.TenantId);

    public async Task<PagedResult<VehicleListItemDto>> SearchAsync(
        string? search, VehicleOperationalStatus? status, bool? isActive, Guid? categoryId,
        PageRequest page, CancellationToken cancellationToken, string? sort = null, string? dir = null)
    {
        var query = TenantScoped().AsNoTracking();

        if (status is { } s) query = query.Where(v => v.OperationalStatus == s);
        if (isActive is { } active) query = query.Where(v => v.IsActive == active);
        if (categoryId is { } cat) query = query.Where(v => v.CategoryId == cat);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive on both PostgreSQL and SQLite (plain LIKE is case-sensitive on PostgreSQL).
            var term = search.Trim().ToLowerInvariant();
            // FL-M1: "1tft001" finds "1-TFT-001" (normalised plate) and the chassis number is searchable.
            var plateTerm = FleetIdentifiers.NormalizePlate(search);
            query = query.Where(v =>
                v.InternalNumber.ToLower().Contains(term) ||
                v.LicensePlate.ToLower().Contains(term) ||
                (plateTerm != "" && v.NormalizedLicensePlate.Contains(plateTerm)) ||
                (v.Vin != null && v.Vin.ToLower().Contains(term)) ||
                (v.Brand != null && v.Brand.ToLower().Contains(term)) ||
                (v.Model != null && v.Model.ToLower().Contains(term)));
        }

        var listSort = ListSort.Parse(sort, dir, "number", ["number", "plate"]);
        IQueryable<Vehicle> sorted = listSort.Key switch
        {
            "plate" => query.OrderBy(v => v.LicensePlate, listSort.Descending),
            _ => query.OrderBy(v => v.InternalNumber, listSort.Descending),
        };

        var projected = from v in sorted
                        join c in _dbContext.VehicleCategories.AsNoTracking().Where(vc => vc.TenantId == _tenantContext.TenantId)
                            on v.CategoryId equals c.Id into cats
                        from c in cats.DefaultIfEmpty()
                        select new VehicleListItemDto(
                            v.Id, v.InternalNumber, v.LicensePlate, v.Brand, v.Model,
                            c != null ? c.Name : null, v.OperationalStatus, v.IsActive);

        return await projected.ToPagedResultAsync(page, dto => dto, cancellationToken);
    }

    public async Task<IReadOnlyList<VehicleOptionDto>> GetOptionsAsync(CancellationToken cancellationToken)
    {
        return await TenantScoped().AsNoTracking()
            .Where(v => v.IsActive)
            .OrderBy(v => v.InternalNumber)
            .Select(v => new VehicleOptionDto(v.Id, v.InternalNumber, v.LicensePlate, v.Brand, v.Model,
                v.OperationalStatus.ToString(), v.PayloadKg, v.HasCrane, v.AdrSuitable))
            .ToListAsync(cancellationToken);
    }

    public async Task<VehicleDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await TenantScoped().AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        return vehicle is null ? null : await MapToDetailAsync(vehicle, cancellationToken);
    }

    public async Task<VehicleOperationResult> CreateAsync(CreateVehicleRequest request, CancellationToken cancellationToken)
    {
        var plate = FleetIdentifiers.DisplayPlate(request.LicensePlate);
        if (await IdentityConflictAsync(null, plate, request.Vin, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        if (!await ReferencesInTenantAsync(request.CategoryId, request.FixedDriverId, request.CurrentDriverId, cancellationToken))
        {
            return VehicleOperationResult.InvalidReference;
        }

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        // Assignment slots on create route through the same one-holder-per-slot rule the
        // assignment service enforces; conflicts are rejected before anything is written.
        if (request.FixedDriverId is { } fixedDriver
            && await TenantScoped().AnyAsync(v => v.FixedDriverId == fixedDriver, cancellationToken))
        {
            return VehicleOperationResult.DriverAlreadyAssigned;
        }

        if (request.CurrentDriverId is { } currentDriver
            && await TenantScoped().AnyAsync(v => v.CurrentDriverId == currentDriver, cancellationToken))
        {
            return VehicleOperationResult.DriverAlreadyAssigned;
        }

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            LicensePlate = plate,
            IsActive = true,
            OperationalStatus = VehicleOperationalStatus.Available,
            FixedDriverId = request.FixedDriverId,
            CurrentDriverId = request.CurrentDriverId,
        };
        ApplyEditableFields(vehicle, request.Vin, request.CategoryId, request.Brand, request.Model, request.Year,
            request.FirstRegistrationDate, request.FuelType, request.EmissionClass,
            request.GrossVehicleWeightKg, request.PayloadKg, request.LengthMeters, request.WidthMeters, request.HeightMeters, request.VolumeM3,
            request.VolumeIsManual, request.OdometerKm, request.ConsumptionLPer100Km, request.HasCrane, request.HasRefrigeration, request.HasTailLift, request.AdrSuitable,
            request.OwnershipType, request.Notes);
        ApplyMautFields(vehicle, request.AxleCount, request.LoadingMeters, request.RequiredLicenceCode);
        vehicle.TailLiftCapacityKg = FleetFieldRules.ResolveTailLiftCapacity(request.HasTailLift, request.TailLiftCapacityKg);

        _dbContext.Add(vehicle);
        try
        {
            await TenantNumbering.SaveWithClaimedNumberAsync(
                _dbContext, settings,
                () => vehicle.InternalNumber = GenerateInternalNumber(settings),
                cancellationToken);
        }
        catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException)
        {
            // Race: a concurrent request claimed the same plate/VIN between the check and the insert.
            _dbContext.Entry(vehicle).State = EntityState.Detached;
            return await IdentityConflictAsync(null, plate, request.Vin, cancellationToken)
                   ?? VehicleOperationResult.DuplicateLicensePlate;
        }

        await _auditService.RecordAsync(EntityType, vehicle.Id.ToString(), "Created", null,
            new { vehicle.InternalNumber, vehicle.LicensePlate }, cancellationToken);

        return VehicleOperationResult.Success(await MapToDetailAsync(vehicle, cancellationToken));
    }

    public async Task<VehicleOperationResult> UpdateAsync(Guid id, UpdateVehicleRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await TenantScoped().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vehicle is null)
        {
            return VehicleOperationResult.NotFound;
        }

        var plate = FleetIdentifiers.DisplayPlate(request.LicensePlate);
        if (await IdentityConflictAsync(id, plate, request.Vin, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        if (!await ReferencesInTenantAsync(request.CategoryId, null, null, cancellationToken))
        {
            return VehicleOperationResult.InvalidReference;
        }

        // FL-M6: the odometer only moves forward through an ordinary edit; lowering it is a
        // correction that needs a reason (recorded in the audit trail).
        var correctionReason = Trim(request.OdometerCorrectionReason);
        if (request.OdometerKm < vehicle.OdometerKm && correctionReason is null)
        {
            throw new Common.DomainValidationException("odometerKm",
                $"De kilometerstand kan niet lager zijn dan de huidige stand ({vehicle.OdometerKm.ToString("N0", Dutch)} km). " +
                "Geef een correctiereden op als dit een correctie is.");
        }

        var before = new
        {
            vehicle.LicensePlate, vehicle.Vin, vehicle.OperationalStatus, vehicle.StatusReason, vehicle.IsActive,
            vehicle.OdometerKm, vehicle.PayloadKg, vehicle.GrossVehicleWeightKg, vehicle.CategoryId, vehicle.Brand, vehicle.Model,
            vehicle.HasCrane, vehicle.AdrSuitable, vehicle.RequiredLicenceCode,
        };
        var odometerCorrected = request.OdometerKm < vehicle.OdometerKm;

        vehicle.LicensePlate = plate;
        vehicle.OperationalStatus = request.OperationalStatus;
        // The reason only makes sense while the vehicle is not available.
        vehicle.StatusReason = request.OperationalStatus == VehicleOperationalStatus.Available
            ? null
            : Trim(request.StatusReason);
        vehicle.IsActive = request.IsActive;
        // Fixed/current driver deliberately untouched here: assignment changes go through
        // IFleetAssignmentService so both sides stay in sync and every change is audited.
        ApplyEditableFields(vehicle, request.Vin, request.CategoryId, request.Brand, request.Model, request.Year,
            request.FirstRegistrationDate, request.FuelType, request.EmissionClass,
            request.GrossVehicleWeightKg, request.PayloadKg, request.LengthMeters, request.WidthMeters, request.HeightMeters, request.VolumeM3,
            request.VolumeIsManual, request.OdometerKm, request.ConsumptionLPer100Km, request.HasCrane, request.HasRefrigeration, request.HasTailLift, request.AdrSuitable,
            request.OwnershipType, request.Notes);
        ApplyMautFields(vehicle, request.AxleCount, request.LoadingMeters, request.RequiredLicenceCode);
        vehicle.TailLiftCapacityKg = FleetFieldRules.ResolveTailLiftCapacity(request.HasTailLift, request.TailLiftCapacityKg);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException)
        {
            return await IdentityConflictAsync(id, plate, request.Vin, cancellationToken)
                   ?? VehicleOperationResult.DuplicateLicensePlate;
        }

        await _auditService.RecordAsync(EntityType, vehicle.Id.ToString(), "Updated", before,
            new
            {
                vehicle.LicensePlate, vehicle.Vin, vehicle.OperationalStatus, vehicle.StatusReason, vehicle.IsActive,
                vehicle.OdometerKm, vehicle.PayloadKg, vehicle.GrossVehicleWeightKg, vehicle.CategoryId, vehicle.Brand, vehicle.Model,
                vehicle.HasCrane, vehicle.AdrSuitable, vehicle.RequiredLicenceCode,
                OdometerCorrectionReason = odometerCorrected ? correctionReason : null,
            }, cancellationToken);

        return VehicleOperationResult.Success(await MapToDetailAsync(vehicle, cancellationToken));
    }

    public async Task<bool> SetActiveAsync(Guid id, SetVehicleActiveRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await TenantScoped().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vehicle is null)
        {
            return false;
        }

        if (vehicle.IsActive != request.IsActive)
        {
            vehicle.IsActive = request.IsActive;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.RecordAsync(EntityType, vehicle.Id.ToString(),
                request.IsActive ? "Activated" : "Deactivated", null,
                new { vehicle.IsActive }, cancellationToken);
        }

        return true;
    }

    public async Task<FleetDeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await TenantScoped().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vehicle is null)
        {
            return FleetDeleteResult.NotFound;
        }

        // FL-B1: a vehicle that any trip or fleet record points at is history, never deletable —
        // deleting it blanked planned trips and made its history unreachable. Deactivate instead.
        var references = await FleetReferences.ForVehicleAsync(_dbContext, _tenantContext.TenantId, id, cancellationToken);
        if (references.HasReferences)
        {
            return new FleetDeleteResult(FleetDeleteOutcome.Refused,
                FleetReferences.RefusalMessage("Dit voertuig", "het", references), references);
        }

        // Unused master-data record: the standing driver slots are released with it.
        vehicle.FixedDriverId = null;
        vehicle.CurrentDriverId = null;
        _dbContext.Remove(vehicle); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, vehicle.Id.ToString(), "Deleted",
            new { vehicle.InternalNumber, vehicle.LicensePlate }, null, cancellationToken);

        return FleetDeleteResult.Deleted;
    }

    public async Task<VehicleReferencesDto?> GetReferencesAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await TenantScoped().AnyAsync(v => v.Id == id, cancellationToken))
        {
            return null;
        }

        return await FleetReferences.ForVehicleAsync(_dbContext, _tenantContext.TenantId, id, cancellationToken);
    }

    public async Task<IReadOnlyList<TripReferenceDto>?> ListRecentTripsAsync(Guid id, int limit, CancellationToken cancellationToken)
    {
        if (!await TenantScoped().AnyAsync(v => v.Id == id, cancellationToken))
        {
            return null;
        }

        var rows = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId && t.VehicleId == id)
            .OrderByDescending(t => t.TripDate).ThenByDescending(t => t.TripNumber)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(t => new { t.Id, t.TripNumber, t.TripDate, t.Status })
            .ToListAsync(cancellationToken);
        return rows.Select(t => new TripReferenceDto(t.Id, t.TripNumber, t.TripDate, t.Status.ToString())).ToList();
    }

    /// <summary>
    /// FL-M1: plate identity is the normalised plate; the VIN is unique whenever it is known.
    /// Returns the conflict result (with a message naming the existing vehicle) or null.
    /// </summary>
    private async Task<VehicleOperationResult?> IdentityConflictAsync(
        Guid? selfId, string plate, string? vin, CancellationToken cancellationToken)
    {
        var normalizedPlate = FleetIdentifiers.NormalizePlate(plate);
        var plateOwner = await TenantScoped().AsNoTracking()
            .Where(v => v.NormalizedLicensePlate == normalizedPlate && v.Id != selfId)
            .Select(v => new { v.InternalNumber, v.LicensePlate })
            .FirstOrDefaultAsync(cancellationToken);
        if (plateOwner is not null)
        {
            return new VehicleOperationResult(VehicleOperationOutcome.DuplicateLicensePlate, null,
                $"Er bestaat al een voertuig met dit kenteken ({plateOwner.LicensePlate}, {plateOwner.InternalNumber}).");
        }

        // MD-M16: a registration identifies ONE registered asset — a trailer may not carry it either.
        var trailerOwner = await _dbContext.Trailers.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId && t.NormalizedLicensePlate == normalizedPlate)
            .Select(t => new { t.InternalNumber, t.LicensePlate })
            .FirstOrDefaultAsync(cancellationToken);
        if (trailerOwner is not null)
        {
            return new VehicleOperationResult(VehicleOperationOutcome.DuplicateLicensePlate, null,
                $"Dit kenteken is al geregistreerd voor oplegger {trailerOwner.InternalNumber} ({trailerOwner.LicensePlate}).");
        }

        if (FleetIdentifiers.NormalizeVin(vin) is { } normalizedVin)
        {
            var vinOwner = await TenantScoped().AsNoTracking()
                .Where(v => v.Vin == normalizedVin && v.Id != selfId)
                .Select(v => new { v.InternalNumber, v.LicensePlate })
                .FirstOrDefaultAsync(cancellationToken);
            if (vinOwner is not null)
            {
                return new VehicleOperationResult(VehicleOperationOutcome.DuplicateVin, null,
                    $"Er bestaat al een voertuig met dit chassisnummer (VIN): {vinOwner.InternalNumber} ({vinOwner.LicensePlate}).");
            }
        }

        return null;
    }

    private void ApplyEditableFields(
        Vehicle v, string? vin, Guid? categoryId, string? brand, string? model, int? year,
        DateOnly? firstRegistration, FuelType fuelType, EmissionClass? emissionClass,
        decimal? grossWeight, decimal? payload, decimal? length, decimal? width, decimal? height, decimal? volume,
        bool volumeIsManual, int odometer, decimal? consumptionLPer100Km, bool crane, bool refrigeration, bool tailLift, bool adr,
        VehicleOwnershipType ownership, string? notes)
    {
        FleetFieldRules.ValidateConstructionYear(year, _timeProvider.GetUtcNow().Year);
        // FL-M5: impossible physical values are refused, never silently clamped.
        FleetFieldRules.RequireNonNegative(grossWeight, "grossVehicleWeightKg", "De MTM");
        FleetFieldRules.RequireNonNegative(payload, "payloadKg", "Het laadvermogen");
        FleetFieldRules.RequireNonNegative(length, "lengthMeters", "De lengte");
        FleetFieldRules.RequireNonNegative(width, "widthMeters", "De breedte");
        FleetFieldRules.RequireNonNegative(height, "heightMeters", "De hoogte");
        FleetFieldRules.RequireNonNegative(odometer, "odometerKm", "De kilometerstand");
        FleetFieldRules.RequireNonNegative(consumptionLPer100Km, "consumptionLPer100Km", "Het verbruik");
        var (resolvedVolume, isManual) = FleetFieldRules.ResolveVolume(length, width, height, volume, volumeIsManual);

        v.Vin = FleetIdentifiers.NormalizeVin(vin);
        v.CategoryId = categoryId;
        v.Brand = Trim(brand);
        v.Model = Trim(model);
        v.Year = year;
        v.FirstRegistrationDate = firstRegistration;
        v.FuelType = fuelType;
        v.EmissionClass = emissionClass;
        v.GrossVehicleWeightKg = grossWeight;
        v.PayloadKg = payload;
        v.LengthMeters = length;
        v.WidthMeters = width;
        v.HeightMeters = height;
        v.VolumeM3 = resolvedVolume;
        v.VolumeIsManual = isManual;
        v.OdometerKm = odometer;
        v.ConsumptionLPer100Km = consumptionLPer100Km;
        v.HasCrane = crane;
        v.HasRefrigeration = refrigeration;
        v.HasTailLift = tailLift;
        v.AdrSuitable = adr;
        v.OwnershipType = ownership;
        v.Notes = Trim(notes);
    }

    private static void ApplyMautFields(Vehicle v, int axleCount, decimal loadingMeters, string? requiredLicenceCode)
    {
        FleetFieldRules.RequireNonNegative(axleCount, "axleCount", "Het aantal assen");
        FleetFieldRules.RequireNonNegative(loadingMeters, "loadingMeters", "Het aantal laadmeters");
        v.AxleCount = Math.Clamp(axleCount, 0, 12);
        v.LoadingMeters = loadingMeters < 0 ? 0 : Math.Round(loadingMeters, 2);
        v.RequiredLicenceCode = string.IsNullOrWhiteSpace(requiredLicenceCode) ? null : requiredLicenceCode.Trim().ToUpperInvariant();
    }

    /// <summary>All optional references on a vehicle must resolve within the current tenant.</summary>
    private async Task<bool> ReferencesInTenantAsync(
        Guid? categoryId, Guid? fixedDriverId, Guid? currentDriverId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        if (categoryId is { } cat && !await _dbContext.VehicleCategories
                .AnyAsync(c => c.Id == cat && c.TenantId == tenantId, cancellationToken))
        {
            return false;
        }

        foreach (var driverId in new[] { fixedDriverId, currentDriverId })
        {
            if (driverId is { } d && !await _dbContext.Drivers
                    .AnyAsync(x => x.Id == d && x.TenantId == tenantId, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<VehicleDetailDto> MapToDetailAsync(Vehicle v, CancellationToken cancellationToken)
    {
        var categoryName = v.CategoryId is { } catId
            ? await _dbContext.VehicleCategories.AsNoTracking()
                .Where(c => c.Id == catId && c.TenantId == _tenantContext.TenantId)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        var fixedDriverName = await ResolveDriverNameAsync(v.FixedDriverId, cancellationToken);
        var currentDriverName = await ResolveDriverNameAsync(v.CurrentDriverId, cancellationToken);

        return new VehicleDetailDto(
            v.Id, v.InternalNumber, v.LicensePlate, v.Vin, v.CategoryId, categoryName, v.Brand, v.Model, v.Year, v.FirstRegistrationDate,
            v.FuelType, v.EmissionClass, v.GrossVehicleWeightKg, v.PayloadKg, v.LengthMeters, v.WidthMeters, v.HeightMeters, v.VolumeM3,
            v.VolumeIsManual, v.OdometerKm, v.ConsumptionLPer100Km, v.HasCrane, v.HasRefrigeration, v.HasTailLift, v.AdrSuitable,
            v.OwnershipType, v.OperationalStatus, v.StatusReason, v.IsActive,
            v.FixedDriverId, fixedDriverName, v.CurrentDriverId, currentDriverName, v.Notes,
            v.AxleCount, v.LoadingMeters, v.RequiredLicenceCode, v.TailLiftCapacityKg);
    }

    private async Task<string?> ResolveDriverNameAsync(Guid? driverId, CancellationToken cancellationToken)
    {
        if (driverId is not { } id)
        {
            return null;
        }

        return await _dbContext.Set<TransportationService.Api.Modules.Drivers.Entities.Driver>().AsNoTracking()
            .Where(d => d.Id == id && d.TenantId == _tenantContext.TenantId)
            .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id, (d, e) => e.FirstName + " " + e.LastName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string GenerateInternalNumber(TenantSettings? settings)
    {
        if (settings is null)
        {
            return $"VRT-{Guid.NewGuid().ToString("N")[..8]}";
        }

        var prefix = settings.VehicleNumberPrefix ?? "VRT-";
        var number = $"{prefix}{settings.VehicleNumberNextValue:D4}";
        settings.VehicleNumberNextValue++;
        return number;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly System.Globalization.CultureInfo Dutch = System.Globalization.CultureInfo.GetCultureInfo("nl-BE");
}
