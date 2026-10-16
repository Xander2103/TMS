using TransportationService.Api.Modules.Employees.Entities;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// HR acceptance H5: the HR employment status decides whether someone can be planned at all.
/// Only "In dienst" (<see cref="EmploymentStatus.Active"/>) is deployable; a suspended
/// ("Geschorst"), terminated or on-leave employment makes the person unavailable in personnel
/// planning, the Planbord, the trip driver picker and the trip conflict check. History is never
/// touched — existing shifts/trips stay visible, they just surface as conflicts.
/// </summary>
public static class EmploymentDeployability
{
    public static bool IsDeployable(EmploymentStatus status) => status == EmploymentStatus.Active;

    /// <summary>Lower-case Dutch phrase for use inside a sentence; null when deployable.</summary>
    public static string? Restriction(EmploymentStatus status) => status switch
    {
        EmploymentStatus.Suspended => "geschorst",
        EmploymentStatus.Terminated => "uit dienst",
        EmploymentStatus.OnLeave => "met verlof (dienstverband)",
        _ => null,
    };

    /// <summary>Stable code for DTOs (the web client translates it with its own
    /// <c>employees.status.*</c> labels); null when deployable.</summary>
    public static string? RestrictionCode(EmploymentStatus status) => IsDeployable(status) ? null : status.ToString();
}
