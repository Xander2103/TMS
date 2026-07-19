using TransportationService.Api.Modules.Organization.Entities;

namespace TransportationService.Api.Modules.Employees.Entities;

/// <summary>Join row linking an employee to one of the tenant's JobFunction lookups.</summary>
public class EmployeeJobFunction
{
    public Guid EmployeeId { get; set; }
    public Guid JobFunctionId { get; set; }

    public JobFunction? JobFunction { get; set; }
}
