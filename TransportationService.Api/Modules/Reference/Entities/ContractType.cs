using TransportationService.Api.Common.Lookups;

namespace TransportationService.Api.Modules.Reference.Entities;

/// <summary>An employment contract type (e.g. Permanent, Fixed-term, Temp agency, Self-employed).</summary>
public class ContractType : LookupEntity
{
    /// <summary>
    /// When true, employees on this contract type must have an EmploymentEndDate (e.g. fixed-term,
    /// temp agency, student contracts). Enforced by EmployeeService on create and update.
    /// </summary>
    public bool RequiresEndDate { get; set; }
}
