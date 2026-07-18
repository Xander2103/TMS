using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Hr.Entities;

public enum AbsenceType
{
    Vacation,
    Sick,
    Training,
    PersonalLeave,
    Unpaid,
    Other,
}

public enum AbsenceStatus
{
    Requested,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>
/// Employee absence over an inclusive date range. Requested absences go through an approval
/// decision; approved absences drive driver availability and (later) planning conflicts.
/// Overlapping non-rejected/non-cancelled absences for the same employee are refused.
/// </summary>
public class Absence : AuditableTenantEntity
{
    public Guid EmployeeId { get; set; }

    public AbsenceType Type { get; set; }

    public DateOnly StartDate { get; set; }

    /// <summary>Inclusive end date (same day as start for a single-day absence).</summary>
    public DateOnly EndDate { get; set; }

    public AbsenceStatus Status { get; set; } = AbsenceStatus.Requested;

    public string? Reason { get; set; }

    public string? DecisionNote { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
}
