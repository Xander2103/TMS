namespace TransportationService.Api.Modules.Dossiers.Dtos;

public record ActivityTypeDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    int SortOrder,
    string? Icon,
    string? KpiCategory,
    bool HasStops,
    bool SupportsGoods,
    bool PlanningRelevant,
    bool WarehouseRelevant,
    bool AllowsDuration,
    bool IsQuickStart,
    int QuickStartOrder,
    bool IsSystemDefaultTransport,
    /// <summary>Commercial unit: carries a sales price and counts in pricing completeness (step 13).</summary>
    bool IsBillable = true,
    /// <summary>D2: orders of this type may be on-site work (OnSiteLifting: site stop + work description).</summary>
    bool SupportsOnSiteWork = false);

/// <summary>
/// Create/update payload for a tenant activity type. <see cref="Code"/> is immutable after
/// creation (the service refuses changes); all capability flags are plain data — domain
/// logic drives on them, never on the code value.
/// </summary>
public record SaveActivityTypeRequest(
    string Code,
    string Name,
    bool IsActive = true,
    int SortOrder = 0,
    string? Icon = null,
    string? KpiCategory = null,
    bool HasStops = false,
    bool SupportsGoods = false,
    bool PlanningRelevant = false,
    bool WarehouseRelevant = false,
    bool AllowsDuration = false,
    bool IsQuickStart = false,
    int QuickStartOrder = 0,
    bool IsSystemDefaultTransport = false,
    bool IsBillable = true,
    /// <summary>D2. Null = unchanged (false on create), so a client that does not know the flag never clears it.</summary>
    bool? SupportsOnSiteWork = null);
