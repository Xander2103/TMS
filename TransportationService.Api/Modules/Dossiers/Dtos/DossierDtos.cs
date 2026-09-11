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
    DateTime CreatedAt,
    string? CustomerReference = null,
    string? CustomerNumber = null,
    /// <summary>Sum of AgreedPrice over ALL priced billable units (orders + standalone activities); null when none is priced (never € 0,00).</summary>
    decimal? AgreedPriceTotal = null,
    /// <summary>Linked orders that count as priced (OrderPricingState.IsPriced); kept for compatibility, see the activity counts.</summary>
    int PricedOrderCount = 0,
    /// <summary>Step 13: billable units of the dossier (activities of a billable type + legacy links without activity).</summary>
    int BillableActivityCount = 0,
    /// <summary>Step 13: billable units that are priced (order provenance or activity agreement).</summary>
    int PricedActivityCount = 0,
    /// <summary>Step 13: priced units whose effective price is exactly € 0 (intentional zero → ⚠ in the list).</summary>
    int ZeroPricedActivityCount = 0);

public record DossierOrderDto(
    Guid LinkId,
    Guid OrderId,
    string OrderNumber,
    DateOnly OrderDate,
    string Status,
    string? GoodsDescription,
    decimal? AgreedPrice,
    /// <summary>OrderPricingState.IsPriced for this order (override, one-off agreement or positive amount); the UI never re-derives it.</summary>
    bool IsPriced = false);

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
    decimal ActualIncidentCost,
    /// <summary>Linked orders that count as priced (OrderPricingState.IsPriced); kept for compatibility.</summary>
    int PricedOrderCount = 0,
    /// <summary>Step 13: billable units (activities of a billable type + legacy links without activity).</summary>
    int BillableActivityCount = 0,
    /// <summary>Step 13: billable units that are priced; 0 → "Nog geen prijs", partial → "x van y activiteiten geprijsd".</summary>
    int PricedActivityCount = 0,
    /// <summary>Step 13: priced units at exactly € 0 (intentional zero).</summary>
    int ZeroPricedActivityCount = 0);

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
    string? Notes,
    /// <summary>Step 13: the activity type is a commercial unit (counts in pricing completeness).</summary>
    bool IsBillable = true,
    /// <summary>"Order" (HasStops with a linked order), "OneOff" (activity agreement) or "None".</summary>
    string PricingSource = "None",
    /// <summary>Effective sales price of the unit: the order's AgreedPrice or the activity's agreed amount.</summary>
    decimal? AgreedPrice = null,
    /// <summary>OrderPricingState resp. ActivityPricingState — the UI never re-derives it from the amount.</summary>
    bool IsPriced = false,
    /// <summary>Order snapshot status resp. activity pricing status (Draft/Reviewed/Locked/Invoiced); null without carrier.</summary>
    string? PricingStatus = null,
    /// <summary>Concurrency token of the activity's own price record (null without record; orders use their order version).</summary>
    Guid? PricingVersion = null);

/// <summary>Step 13: set (or clear with null) the agreed sales price of a standalone billable activity.</summary>
public record SetActivityPriceRequest(decimal? FixedAmount, Guid? Version = null);

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
    string Stage,      // Planning | Warehouse | Execution | Commercial | Invoice
    /// <summary>The linked order an order-level rule is about, so a multi-order dossier can target the right editor (null for dossier-level rules).</summary>
    Guid? TransportOrderId = null,
    /// <summary>The activity an activity-level rule is about (e.g. route.order_missing); null for dossier-level rules.</summary>
    Guid? ActivityId = null);

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

/// <summary>What a dossier-level entity change will do to its linked orders, shown BEFORE confirming.</summary>
public record DossierLegalEntityChangeImpactDto(
    Guid DossierId,
    Guid? CurrentLegalEntityId,
    Guid TargetLegalEntityId,
    /// <summary>True when the target is not the customer default: needs the override right AND a reason.</summary>
    bool DeviatesFromCustomerDefault,
    /// <summary>Blocking reason for the whole dossier; set as soon as one linked order is refused.</summary>
    string? BlockedReason,
    /// <summary>Linked orders (on the dossier's current entity) that move along.</summary>
    IReadOnlyList<DossierLegalEntityChangeOrderDto> Orders,
    /// <summary>Total concept-invoice lines released across those orders.</summary>
    int DraftInvoiceLinesReleased);

public record DossierLegalEntityChangeOrderDto(
    Guid OrderId, string OrderNumber, string? BlockedReason, int DraftInvoiceLinesReleased);

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
