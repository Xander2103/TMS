namespace TransportationService.Api.Modules.Reporting.Dtos;

/// <summary>
/// One row per activity type with at least one dossier activity in the period. A dossier that
/// contains a crane AND a plateau activity contributes to BOTH rows independently (rows count
/// activities, not dossiers). Revenue is the sum of the linked orders' AgreedPrice (?? 0),
/// counted once per distinct order within the row.
/// </summary>
public record ActivityKpiRowDto(
    Guid ActivityTypeId,
    string Code,
    string Name,
    string? KpiCategory,
    int ActivityCount,
    int LinkedOrderCount,
    decimal Revenue,
    int RedeliveryCount);

/// <summary>Rollup of the rows per ActivityType.KpiCategory (null = types without a category).</summary>
public record ActivityKpiCategoryRowDto(
    string? KpiCategory,
    int ActivityCount,
    int LinkedOrderCount,
    decimal Revenue,
    int RedeliveryCount);

/// <summary>
/// Grand totals. LinkedOrderCount/Revenue dedupe orders ACROSS rows (an order linked from two
/// activity types counts once here). RedeliveryCount is the tenant-wide number of incidents
/// whose redelivery order falls in the period — it can exceed the sum of the rows when a
/// redelivery order is not linked to an in-period activity.
/// </summary>
public record ActivityKpiTotalsDto(
    int ActivityCount,
    int LinkedOrderCount,
    decimal Revenue,
    int RedeliveryCount);

/// <summary>
/// Activity-based KPI report (P11). Activities fall in the period by their effective date
/// = PlannedDate ?? date(CreatedAt) — standalone activities carry a PlannedDate, auto-wrapped
/// transport activities fall back to their creation date. PalletDays is the tenant-wide
/// started-days total of storage stays overlapping the period (StorageBillingService
/// semantics); null when no stay overlaps the period.
/// </summary>
public record ActivityKpiReportDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ActivityKpiRowDto> Rows,
    ActivityKpiTotalsDto Totals,
    decimal? PalletDays,
    IReadOnlyList<ActivityKpiCategoryRowDto> PerCategory);
