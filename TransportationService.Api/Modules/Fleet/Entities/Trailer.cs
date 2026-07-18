using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Fleet.Entities;

public enum TrailerOperationalStatus
{
    Active,
    InMaintenance,
    OutOfService,
    Decommissioned,
}

/// <summary>
/// An unpowered trailer (curtainsider, reefer, tanker, flatbed, ...) in the operator's fleet.
/// Documents, maintenance, inspections and damage reports reference this entity (later Fleet
/// waves) rather than being embedded here, mirroring the Vehicle design.
/// </summary>
public class Trailer : AuditableTenantEntity
{
    public string InternalNumber { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public string? Vin { get; set; }

    public Guid? CategoryId { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public DateOnly? FirstRegistrationDate { get; set; }

    public decimal? CapacityKg { get; set; }
    public decimal? LengthMeters { get; set; }
    public decimal? WidthMeters { get; set; }
    public decimal? HeightMeters { get; set; }
    public decimal? VolumeM3 { get; set; }

    public bool HasRefrigeration { get; set; }
    public bool AdrSuitable { get; set; }

    public VehicleOwnershipType OwnershipType { get; set; } = VehicleOwnershipType.Owned;
    public TrailerOperationalStatus OperationalStatus { get; set; } = TrailerOperationalStatus.Active;
    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }
}
