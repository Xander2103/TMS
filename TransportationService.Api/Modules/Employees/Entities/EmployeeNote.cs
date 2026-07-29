using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Employees.Entities;

/// <summary>
/// One free-text note attached to an employee's HR file (corrections wave §4). Replaces the
/// legacy single <see cref="Employee.Notes"/> field going forward: an employee can now have
/// any number of notes, each individually pinnable to the company dashboard as a personnel
/// "attention point". The legacy column is kept (read-only) for historical continuity — it is
/// converted to a first note by the introducing migration and never written to again.
/// </summary>
public class EmployeeNote : AuditableTenantEntity, ISoftDeletable
{
    public Guid EmployeeId { get; set; }
    public string Text { get; set; } = string.Empty;

    /// <summary>When true, a compact excerpt surfaces on the company dashboard for every user
    /// holding employee_notes.view.</summary>
    public bool IsPinnedToDashboard { get; set; }
}
