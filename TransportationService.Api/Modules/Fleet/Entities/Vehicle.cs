using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Fleet.Entities;

public enum FuelType
{
    Diesel,
    Petrol,
    Electric,
    Hybrid,
    Lng,
    Cng,
    Hydrogen,
    Other,
}

public enum EmissionClass
{
    Euro3,
    Euro4,
    Euro5,
    Euro6,
    Electric,
    Other,
}

public enum VehicleOwnershipType
{
    Owned,
    Rented,
    Leased,
}

/// <summary>
/// Operational status — deliberately distinct from the administrative IsActive flag, and
/// with no member that could read as a second "Actief": Available (Beschikbaar), InUse
/// (In gebruik), InMaintenance (In onderhoud), OutOfService (Buiten dienst).
/// </summary>
public enum VehicleOperationalStatus
{
    Available,
    InUse,
    InMaintenance,
    OutOfService,
}

/// <summary>
/// A powered vehicle (tractor unit, rigid truck, van, ...) in the operator's fleet.
/// Documents, maintenance, inspections, damage reports and tank cards are modelled as
/// separate entities referencing this one (later Fleet waves) rather than embedded here.
/// </summary>
public class Vehicle : AuditableTenantEntity
{
    public string InternalNumber { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public string? Vin { get; set; }

    public Guid? CategoryId { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public DateOnly? FirstRegistrationDate { get; set; }

    public FuelType FuelType { get; set; } = FuelType.Diesel;
    public EmissionClass? EmissionClass { get; set; }

    public decimal? GrossVehicleWeightKg { get; set; }
    public decimal? PayloadKg { get; set; }
    public decimal? LengthMeters { get; set; }
    public decimal? WidthMeters { get; set; }
    public decimal? HeightMeters { get; set; }
    public decimal? VolumeM3 { get; set; }

    public int OdometerKm { get; set; }

    public bool HasCrane { get; set; }
    public bool HasRefrigeration { get; set; }
    public bool HasTailLift { get; set; }
    public bool AdrSuitable { get; set; }

    public VehicleOwnershipType OwnershipType { get; set; } = VehicleOwnershipType.Owned;
    public VehicleOperationalStatus OperationalStatus { get; set; } = VehicleOperationalStatus.Available;

    /// <summary>Why the vehicle is not available (maintenance details, defect, ...).</summary>
    public string? StatusReason { get; set; }

    /// <summary>Administrative lifecycle: part of the fleet or archived. Separate from OperationalStatus.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The driver this vehicle is permanently assigned to, if any.</summary>
    public Guid? FixedDriverId { get; set; }

    /// <summary>The driver currently operating this vehicle (day-to-day assignment), if any.</summary>
    public Guid? CurrentDriverId { get; set; }

    public string? Notes { get; set; }
}
