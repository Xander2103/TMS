namespace TransportationService.Api.Modules.Dossiers.Dtos;

public record DossierListItemDto(
    Guid Id,
    string DossierNumber,
    string Title,
    string Status,
    Guid? CustomerId,
    string? CustomerName,
    string? ResponsibleName,
    int OrderCount,
    int OpenIncidentCount,
    DateTime CreatedAt);

public record DossierOrderDto(
    Guid LinkId,
    Guid OrderId,
    string OrderNumber,
    DateOnly OrderDate,
    string Status,
    string? GoodsDescription,
    decimal? AgreedPrice);

/// <summary>One relation seen from the dossier being viewed; Other* describes the far end.</summary>
public record DossierRelationDto(
    Guid Id,
    string RelationType,
    string? Notes,
    bool IsOutgoing,
    Guid OtherDossierId,
    string OtherDossierNumber,
    string OtherDossierTitle);

public record DossierIncidentDto(
    Guid Id,
    string Title,
    string IncidentType,
    string Status,
    string Severity,
    DateOnly? DueDate);

/// <summary>
/// Money view of the dossier: agreed order revenue, what was actually invoiced for those
/// orders, and the incident cost estimate/actuals booked against the dossier.
/// </summary>
public record DossierFinancialSummaryDto(
    decimal AgreedOrderTotal,
    decimal InvoicedTotal,
    decimal EstimatedIncidentCost,
    decimal ActualIncidentCost);

/// <summary>One activity card on the dossier: type capabilities + the linked execution record.</summary>
public record DossierActivityDto(
    Guid Id,
    Guid ActivityTypeId,
    string ActivityTypeCode,
    string ActivityTypeName,
    string? Icon,
    bool HasStops,
    bool SupportsGoods,
    bool AllowsDuration,
    int Sequence,
    string? Label,
    Guid? LinkedTransportOrderId,
    string? LinkedOrderNumber,
    string? LinkedOrderStatus,
    Guid? LinkedActivityId,
    DateOnly? PlannedDate,
    decimal? DurationHours,
    string? Notes);

/// <summary>
/// One actionable attention item (additive readiness projection — never a new order status).
/// Section is the dossier-page anchor the frontend scrolls/opens; Stage names the workflow
/// dimension so later waves add producers without schema change.
/// </summary>
public record ReadinessIssueDto(
    string Code,
    string Severity,   // Info | Warning | Blocking
    string Message,
    string Section,    // algemeen | activiteiten | route | goederen | prijs
    string? Field,
    string Stage);     // Planning | Warehouse | Execution | Commercial | Invoice

public record DossierDetailDto(
    Guid Id,
    string DossierNumber,
    string Title,
    string? Description,
    string Status,
    Guid? CustomerId,
    string? CustomerName,
    Guid? ResponsibleUserId,
    string? ResponsibleName,
    DateTime? ClosedAt,
    string? Notes,
    DateTime CreatedAt,
    IReadOnlyList<DossierOrderDto> Orders,
    IReadOnlyList<DossierRelationDto> Relations,
    IReadOnlyList<DossierIncidentDto> Incidents,
    DossierFinancialSummaryDto Financials,
    string? CustomerReference = null,
    DateOnly? DossierDate = null,
    Guid? LegalEntityId = null,
    string? LegalEntityName = null,
    Guid Version = default,
    IReadOnlyList<DossierActivityDto>? Activities = null,
    IReadOnlyList<ReadinessIssueDto>? Readiness = null);

public record SaveDossierRequest(
    /// <summary>Optional since the dossier-foundation wave: defaults to "{klant} — {datum}".</summary>
    string? Title = null,
    string? Description = null,
    Guid? CustomerId = null,
    Guid? ResponsibleUserId = null,
    string? Notes = null,
    string? CustomerReference = null,
    DateOnly? DossierDate = null,
    /// <summary>Fast-create template: the quick-start activity type to start with.</summary>
    Guid? ActivityTypeId = null,
    /// <summary>Expected concurrency token on update; null (legacy clients) skips the check.</summary>
    Guid? Version = null);

public record ChangeDossierEntityRequest(
    Guid LegalEntityId, Guid? Version = null,
    /// <summary>Wave 2: mandatory when the target differs from the customer default (audited old→new+reason).</summary>
    string? Reason = null);

public record SaveDossierActivityRequest(
    Guid ActivityTypeId,
    string? Label = null,
    DateOnly? PlannedDate = null,
    decimal? DurationHours = null,
    Guid? LinkedActivityId = null,
    string? Notes = null,
    /// <summary>HasStops types only: also create a draft order inside this dossier and link it.</summary>
    bool CreateLinkedOrder = false,
    Guid? Version = null);

public record ReorderDossierActivitiesRequest(IReadOnlyList<Guid> ActivityIds, Guid? Version = null);

/// <summary>Explicit "Transportopdracht aanmaken" on an existing order-less transport activity.</summary>
public record CreateActivityOrderRequest(Guid? Version = null);

public record LinkDossierOrderRequest(Guid TransportOrderId);

public record AddDossierRelationRequest(
    Guid TargetDossierId,
    string RelationType,
    string? Notes = null);
