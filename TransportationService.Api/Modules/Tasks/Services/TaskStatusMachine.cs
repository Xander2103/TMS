using TransportationService.Api.Modules.Tasks.Entities;

namespace TransportationService.Api.Modules.Tasks.Services;

/// <summary>
/// The one legal source of task status transitions (spec §4):
///   Todo → InProgress | Cancelled
///   InProgress → Blocked | WaitingForReview | Completed (alleen zonder reviewplicht) | Cancelled
///   Blocked → InProgress | Cancelled
///   WaitingForReview → Completed (goedkeuring) | InProgress (afkeuring)
///   Completed/Cancelled → Todo, uitsluitend via reopen (tasks.reopen).
/// </summary>
public static class TaskStatusMachine
{
    public static bool CanTransition(EmployeeTaskStatus from, EmployeeTaskStatus to, bool requiresReview) => (from, to) switch
    {
        (EmployeeTaskStatus.Todo, EmployeeTaskStatus.InProgress) => true,
        (EmployeeTaskStatus.Todo, EmployeeTaskStatus.Cancelled) => true,
        (EmployeeTaskStatus.InProgress, EmployeeTaskStatus.Blocked) => true,
        (EmployeeTaskStatus.InProgress, EmployeeTaskStatus.WaitingForReview) => true,
        (EmployeeTaskStatus.InProgress, EmployeeTaskStatus.Completed) => !requiresReview,
        (EmployeeTaskStatus.InProgress, EmployeeTaskStatus.Cancelled) => true,
        (EmployeeTaskStatus.Blocked, EmployeeTaskStatus.InProgress) => true,
        (EmployeeTaskStatus.Blocked, EmployeeTaskStatus.Cancelled) => true,
        (EmployeeTaskStatus.WaitingForReview, EmployeeTaskStatus.Completed) => true,
        (EmployeeTaskStatus.WaitingForReview, EmployeeTaskStatus.InProgress) => true,
        _ => false,
    };

    /// <summary>Statuses that count as "open" for dashboards, redistribution and overdue checks.</summary>
    public static readonly EmployeeTaskStatus[] OpenStatuses =
    [
        EmployeeTaskStatus.Todo, EmployeeTaskStatus.InProgress,
        EmployeeTaskStatus.Blocked, EmployeeTaskStatus.WaitingForReview,
    ];
}
