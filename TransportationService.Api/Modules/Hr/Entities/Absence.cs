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
    UnderReview,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>Half-day support for single-day absences; multi-day ranges are always full days.</summary>
public enum AbsencePartDay
{
    FullDay,
    Morning,
    Afternoon,
}

/// <summary>
/// Employee absence over an inclusive date range. Requested absences go through an approval
/// workflow (optionally via UnderReview, with a change-request loop back to Requested);
/// approved absences drive driver availability and planning conflicts. Overlapping
/// non-rejected/non-cancelled absences for the same employee are refused.
/// </summary>
public class Absence : AuditableTenantEntity
{
    public Guid EmployeeId { get; set; }

    public AbsenceType Type { get; set; }

    public DateOnly StartDate { get; set; }

    /// <summary>Inclusive end date (same day as start for a single-day absence).</summary>
    public DateOnly EndDate { get; set; }

    public AbsencePartDay PartDay { get; set; } = AbsencePartDay.FullDay;

    public AbsenceStatus Status { get; set; } = AbsenceStatus.Requested;

    public string? Reason { get; set; }

    /// <summary>Employee-visible note from HR (decision or change request).</summary>
    public string? DecisionNote { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }

    /// <summary>HR-only note; never exposed through the employee portal.</summary>
    public string? InternalNote { get; set; }

    /// <summary>Optional supporting document (e.g. sick note), stored via the file-storage abstraction.</summary>
    public string? AttachmentPath { get; set; }
    public string? AttachmentFileName { get; set; }
}
