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
    int ZeroPricedActivityCount = 0,
    // --- Dossier search sprint 2026-09-23 ---
    DateOnly? DossierDate = null,
    /// <summary>Set when Status is Closed (= confirmed); null otherwise.</summary>
    DateTime? ConfirmedAt = null,
    /// <summary>"Manual" | "Automatic" | null (open, cancelled or legacy closed).</summary>
    string? ConfirmationSource = null,
    int ActivityCount = 0,
    string? FirstLoadingCity = null,
    string? LastUnloadingCity = null,
    /// <summary>One driver name, or "N chauffeurs" when several trips carry different drivers; null when unplanned.</summary>
    string? DriverSummary = null,
    /// <summary>One licence plate, or "N voertuigen"; null when unplanned.</summary>
    string? VehicleSummary = null,
    /// <summary>Earliest trip date of the dossier's orders or planned date of an executable standalone activity.</summary>
    DateOnly? PlanningDate = null,
    /// <summary>"NotInvoiced" | "Draft" | "Sent" | "Paid" over the live invoices of its orders/activities.</summary>
    string? InvoiceStatus = null,
    bool HasCmr = false);

/// <summary>
/// Dossier search sprint 2026-09-23 — the ONE typed query behind the dossier list. Every text
/// filter is a case-insensitive contains; date ranges are inclusive; unknown sort keys fall
/// back to the default; the page size is bounded by <see cref="Common.Models.PageRequest"/>.
/// </summary>
public sealed record DossierSearchQuery
{
    /// <summary>Global search: dossier/order number, title, customer name/number/reference, driver, plate, city, postcode.</summary>
    public string? Search { get; init; }
    /// <summary>Open | Closed | Cancelled; empty = all.</summary>
    public string? Status { get; init; }
    public Guid? CustomerId { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }

    public string? DossierNumber { get; init; }
    public string? OrderNumber { get; init; }
    public string? CustomerReference { get; init; }
    public string? CustomerNumber { get; init; }

    /// <summary>Manual | Automatic.</summary>
    public string? ConfirmationSource { get; init; }
    public DateOnly? ConfirmedFrom { get; init; }
    public DateOnly? ConfirmedTo { get; init; }
    public DateOnly? CreatedFrom { get; init; }
    public DateOnly? CreatedTo { get; init; }

    public DateOnly? PlanningFrom { get; init; }
    public DateOnly? PlanningTo { get; init; }
    public Guid? DriverId { get; init; }
    public Guid? VehicleId { get; init; }
    public string? LicensePlate { get; init; }
    public Guid? TrailerId { get; init; }

    public Guid? ActivityTypeId { get; init; }
    public string? LoadingCity { get; init; }
    public string? UnloadingCity { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }

    /// <summary>Priced | Partial | Unpriced (over the billable units).</summary>
    public string? PriceStatus { get; init; }
    /// <summary>NotInvoiced | Draft | Sent | Paid.</summary>
    public string? InvoiceStatus { get; init; }
    public bool? HasCmr { get; init; }

    /// <summary>number | date | customer | status | confirmedAt | planningDate | createdAt (default: date).</summary>
    public string? Sort { get; init; }
    /// <summary>asc | desc (default depends on the key: date-like keys desc, text keys asc).</summary>
    public string? Dir { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

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
    /// <summary>Step 13: priced units at exactly € 0 (intentional zero) that were NOT confirmed free (D5) — the ones that earn a ⚠.</summary>
    int ZeroPricedActivityCount = 0,
    /// <summary>D5: billable units without a price (BillableActivityCount − PricedActivityCount), spelled out so the UI never subtracts.</summary>
    int UnpricedActivityCount = 0);

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
    Guid? PricingVersion = null,
    /// <summary>D2: the activity type allows on-site work (the linked order may be OnSiteLifting).</summary>
    bool SupportsOnSiteWork = false,
    /// <summary>
    /// D1: read-only EFFECTIVE assignment — the trip of the activity's order. Null when the activity
    /// has no order or the order is on no (non-cancelled) trip. Never a second source: driver,
    /// vehicle and trailer are changed on the trip.
    /// </summary>
    DossierActivityAssignmentDto? Assignment = null,
    /// <summary>D5: sales lines of a STANDALONE activity (PricingSource "Lines"); empty for order-backed activities (their lines live on the order).</summary>
    IReadOnlyList<DossierActivityPriceLineDto>? PriceLines = null,
    /// <summary>D5: the activity was explicitly confirmed free (total € 0) — no <c>pricing.zero</c> warning.</summary>
    bool FreeConfirmed = false,
    /// <summary>
    /// D5: derived commercial status — <c>NotPriced</c> | <c>PartiallyPriced</c> | <c>Priced</c> |
    /// <c>Free</c>; null for a non-billable activity type (no commercial status). Standalone: from
    /// the activity's own record. Order-backed: from the ORDER (provenance, intentional zero,
    /// snapshot coverage Partial/None or stale ⇒ PartiallyPriced). A missing price is never 0.
    /// </summary>
    string? PriceStatus = null,
    /// <summary>D7: notes about this activity (dossier-level notes are counted on the dossier).</summary>
    int NoteCount = 0,
    /// <summary>D7: first 160 characters of the newest note, on a single line; null without notes.</summary>
    string? LatestNotePreview = null,
    DateTime? LatestNoteAt = null,
    /// <summary>D6: transport documents issued for the activity's order (own unique number), oldest first; empty without order.</summary>
    IReadOnlyList<DossierActivityIssuedDocumentDto>? IssuedDocuments = null,
    /// <summary>D6: OWN documents of the activity's order. Documents of the dossier as a whole are counted on the dossier only.</summary>
    int DocumentCount = 0);

/// <summary>D6: an issued transport document as shown on the activity card (<c>Kind</c>: Cmr | DeliveryNote | WorkOrder).</summary>
public record DossierActivityIssuedDocumentDto(Guid Id, string Kind, string DocumentNumber);

/// <summary>D5: one sales line of a standalone activity; <c>Amount</c> = round(Quantity × UnitPrice, 2), computed by the server.</summary>
public record DossierActivityPriceLineDto(
    Guid Id,
    int Sequence,
    string Label,
    decimal Quantity,
    string? Unit,
    decimal UnitPrice,
    decimal Amount,
    Guid? SalesCategoryId);

/// <summary>
/// D5: one line of the id-preserving replace. <c>Id</c> null = new line; a known id updates that
/// line; an id this price record does not own is refused. <c>Amount</c> is accepted only so a
/// client may echo the DTO — it is IGNORED: the server always computes quantity × unit price.
/// </summary>
public record DossierActivityPriceLineInput(
    Guid? Id,
    string? Label,
    decimal Quantity,
    string? Unit,
    decimal UnitPrice,
    Guid? SalesCategoryId = null,
    decimal? Amount = null);

/// <summary>
/// D5: replaces the sales lines of a standalone billable activity. <c>Version</c> is the PRICE
/// RECORD token (<c>DossierActivityDto.PricingVersion</c>), exactly like
/// <see cref="SetActivityPriceRequest"/>; null is accepted while no record exists yet. Empty list +
/// <c>FreeConfirmed</c> = explicitly free; empty list without = not priced.
/// </summary>
public record SetActivityPriceLinesRequest(
    Guid? Version = null,
    IReadOnlyList<DossierActivityPriceLineInput>? Lines = null,
    bool FreeConfirmed = false);

/// <summary>
/// D1: the most relevant non-cancelled trip of an activity's order (InProgress, then Planned, then
/// Draft, then Completed; latest trip date within a status). <c>OtherOrderCount</c> &gt; 0 means
/// the assignment is shared: changing it applies to the whole trip.
/// </summary>
public record DossierActivityAssignmentDto(
    Guid TripId,
    string TripNumber,
    DateOnly TripDate,
    string TripStatus,
    Guid? DriverId,
    string? DriverName,
    Guid? VehicleId,
    string? VehicleNumber,
    string? VehiclePlate,
    /// <summary>Suggested | Manual; null = legacy/no vehicle (treat as Manual).</summary>
    string? VehicleSelectionSource,
    Guid? TrailerId,
    string? TrailerNumber,
    string? TrailerPlate,
    /// <summary>Non-cancelled trips this order sits on (normally 1).</summary>
    int TripCount,
    /// <summary>Other orders on the same trip.</summary>
    int OtherOrderCount);

/// <summary>
/// D1: "Inplannen" of an activity — creates a Draft trip for the activity's order (or returns the
/// existing open trip unchanged). All fields optional; <c>Version</c> is the DOSSIER token.
/// </summary>
public record PlanDossierActivityRequest(
    DateOnly? TripDate = null,
    Guid? DriverId = null,
    Guid? VehicleId = null,
    Guid? TrailerId = null,
    Modules.Planning.Entities.VehicleSelectionSource? VehicleSelectionSource = null,
    Guid? Version = null);

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
    IReadOnlyList<ReadinessIssueDto>? Readiness = null,
    /// <summary>Redesign 2026-09-11 (Overzicht): documents on the linked orders — count and distinct types, one query.</summary>
    int DocumentCount = 0,
    IReadOnlyList<string>? DocumentTypes = null,
    /// <summary>Latest UpdatedAt over the dossier, its activities and its linked orders ("Laatste wijziging").</summary>
    DateTime? LastChangedAt = null,
    /// <summary>D7: dossier-level notes (notes about an activity are counted on that activity).</summary>
    int NoteCount = 0,
    /// <summary>
    /// Confirmation sprint 2026-09-23: operational confirmation metadata (Status Closed = confirmed).
    /// ConfirmationSource "Manual" | "Automatic" | null (legacy Closed rows: source unknown).
    /// </summary>
    DateTime? ConfirmedAt = null,
    Guid? ConfirmedByUserId = null,
    string? ConfirmedByName = null,
    string? ConfirmationSource = null,
    string? ConfirmationReason = null,
    DateTime? CancelledAt = null,
    string? CancellationReason = null);

// ------------------------------------------------------------ lifecycle (confirmation sprint 2026-09-23)

public record ConfirmDossierRequest(string? Reason = null, bool AcknowledgeWarnings = false, Guid? Version = null);
public record ReopenDossierRequest(string Reason, Guid? Version = null);
public record CancelDossierRequest(string Reason, Guid? Version = null);

/// <summary>One finding of the confirmation evaluation. Severity: Blocking | Warning | Info.</summary>
public record DossierConfirmationCheckDto(
    string Code, string Severity, string Message, Guid? TransportOrderId = null, Guid? ActivityId = null);

/// <summary>
/// What confirming the dossier would mean right now. Blockers refuse BOTH flows; Warnings are
/// shown to a person who may confirm anyway (historic dossiers); AutoBlockers are the stricter
/// list the automatic pipeline needs empty.
/// </summary>
public record DossierConfirmationEvaluationDto(
    Guid DossierId,
    string Status,
    bool CanConfirmManually,
    bool CanAutoConfirm,
    IReadOnlyList<DossierConfirmationCheckDto> Blockers,
    IReadOnlyList<DossierConfirmationCheckDto> Warnings,
    IReadOnlyList<DossierConfirmationCheckDto> AutoBlockers,
    IReadOnlyList<Guid> RelevantOrderIds,
    IReadOnlyList<Guid> RelevantActivityIds,
    DossierConfirmationSummaryDto Summary);

/// <summary>Compact counters for the confirmation dialog.</summary>
public record DossierConfirmationSummaryDto(
    int OrdersTotal, int OrdersCompleted, int OrdersCancelled, int OrdersOpen,
    int DeliveriesFailed, int PodMissing,
    int ActivitiesExecutable, int ActivitiesExecuted,
    int BillableUnits, int PricedUnits,
    int Documents, bool HasCmr,
    int OpenIncidents);

/// <summary>D7: one dossier note. <c>DossierActivityId</c> null = dossier-level. <c>CanEdit</c>/<c>CanDelete</c> = the caller holds dossiers.manage.</summary>
public record DossierNoteDto(
    Guid Id,
    Guid DossierId,
    Guid? DossierActivityId,
    string Text,
    string? AuthorName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool CanEdit,
    bool CanDelete);

public record CreateDossierNoteRequest(string? Text, Guid? DossierActivityId = null);

public record UpdateDossierNoteRequest(string? Text);

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
