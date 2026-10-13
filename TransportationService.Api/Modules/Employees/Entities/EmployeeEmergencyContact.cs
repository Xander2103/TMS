using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Employees.Entities;

/// <summary>Burgerlijke staat of an employee (managed enum; UI shows Dutch labels).</summary>
public enum CivilStatus
{
    Married,
    Unmarried,
    Widowed,
    Divorced,
    LegallyCohabiting,
    Single,
    Other,
}

/// <summary>
/// Emergency contact of an employee. Multiple per employee, ordered by Priority (1 = call
/// first). The employee's legacy EmergencyContactName/Phone columns mirror the priority-1
/// row for backward compatibility.
/// </summary>
public class EmployeeEmergencyContact : AuditableTenantEntity
{
    public Guid EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Relationship { get; set; }
    public string? Phone { get; set; }
    public string? MobilePhone { get; set; }
    public string? Notes { get; set; }
    public int Priority { get; set; } = 1;
}
