using TransportationService.Api.Modules.Employees.Entities;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// MD-H6 follow-up: the ONE definition of "this employment has ended", shared by the offboarding
/// orchestrator (explicit deactivation, an edit to Terminated, the end-date sweep) and the access
/// checks (login, refresh, every authenticated request).
/// <para>
/// <c>EmploymentEndDate</c> is the LAST working day: the person keeps access on that day and loses
/// it from the next tenant-local day. A future end date therefore changes nothing today.
/// </para>
/// </summary>
public static class EmploymentState
{
    public static bool HasEnded(bool isActive, EmploymentStatus status, DateOnly? endDate, DateOnly today) =>
        !isActive || status == EmploymentStatus.Terminated || (endDate is { } last && last < today);

    public static bool HasEnded(Employee employee, DateOnly today) =>
        HasEnded(employee.IsActive, employee.EmploymentStatus, employee.EmploymentEndDate, today);
}
