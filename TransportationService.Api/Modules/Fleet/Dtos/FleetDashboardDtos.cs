namespace TransportationService.Api.Modules.Fleet.Dtos;

/// <summary>Vehicle or trailer population broken down by operational status; Inactive is orthogonal (IsActive flag).</summary>
public record FleetAssetCountsDto(
    int Total,
    int Available,
    int InUse,
    int InMaintenance,
    int OutOfService,
    int Inactive);

/// <summary>
/// One-call aggregate for the fleet dashboard. Counts cover everything in-tenant; the lists are
/// bounded top-N previews — the nested module endpoints serve the full data.
/// </summary>
public record FleetDashboardDto(
    FleetAssetCountsDto Vehicles,
    FleetAssetCountsDto Trailers,
    int MaintenanceDueCount,
    IReadOnlyList<DueMaintenanceDto> MaintenanceDue,
    int InspectionsDueCount,
    IReadOnlyList<DueInspectionDto> InspectionsDue,
    int DocumentsExpiringCount,
    IReadOnlyList<ExpiringFleetDocumentDto> DocumentsExpiring,
    int OpenDamageCount,
    IReadOnlyList<RecentDamageDto> RecentDamage,
    IReadOnlyList<FuelWarningDto> FuelWarnings,
    /// <summary>FL-M9: active leasing contracts that ended or end within 30 days.</summary>
    int LeasingEndingCount = 0,
    IReadOnlyList<LeasingEndingDto>? LeasingEnding = null);

public record LeasingEndingDto(
    Guid Id,
    Guid? VehicleId,
    Guid? TrailerId,
    string OwnerNumber,
    string OwnerLicensePlate,
    string LeasingCompany,
    string? ContractNumber,
    DateOnly EndDate,
    bool Ended);
