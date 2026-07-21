using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Fleet.Entities;

/// <summary>
/// A leasing contract for exactly one vehicle or one trailer. Financial values
/// (MonthlyAmount, KilometerAllowancePerYear, EndOfContractMileageKm) are sensitive and
/// only served with the fleet_finance.view permission; mutations require fleet_finance.manage.
/// </summary>
public class LeasingContract : AuditableTenantEntity
{
    public Guid? VehicleId { get; set; }
    public Guid? TrailerId { get; set; }

    public string LeasingCompany { get; set; } = string.Empty;
    public string? ContractNumber { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    // Sensitive financial fields.
    public decimal? MonthlyAmount { get; set; }
    public string Currency { get; set; } = "EUR";
    public int? KilometerAllowancePerYear { get; set; }
    public int? EndOfContractMileageKm { get; set; }

    public string? ContactPerson { get; set; }
    public string? Notes { get; set; }

    public string? StorageKey { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }

    public bool IsActive { get; set; } = true;
}
