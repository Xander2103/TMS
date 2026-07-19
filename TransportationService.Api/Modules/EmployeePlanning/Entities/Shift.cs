using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.EmployeePlanning.Entities;

public enum ShiftType
{
    Work,
    Standby,
    Training,
}

public enum ShiftStatus
{
    Draft,
    Planned,
    Confirmed,
}

/// <summary>
/// One personnel shift — deliberately separate from transport trip planning. Leave and
/// sickness are NOT shifts: they live in the Absence domain and the schedule grid merges
/// both sources, so availability has a single source of truth per concern.
/// </summary>
public class Shift : AuditableTenantEntity
{
    public Guid EmployeeId { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public int BreakMinutes { get; set; }

    public ShiftType Type { get; set; } = ShiftType.Work;
    public ShiftStatus Status { get; set; } = ShiftStatus.Draft;

    public string? WorkLocation { get; set; }

    /// <summary>Function/role for this shift (may differ from the HR job function).</summary>
    public string? RoleLabel { get; set; }

    public string? Notes { get; set; }
}
