import type { BadgeTone } from '../../components/ui/Badge'
import type { DossierActivityPriceLine } from './pricing/types'

/** Closed = operationally CONFIRMED ("Bevestigd"); Cancelled = never executed. */
export type DossierStatus = 'Open' | 'Closed' | 'Cancelled'
export type DossierConfirmationSource = 'Manual' | 'Automatic'

export type DossierRelationType = 'FollowUp' | 'Return' | 'Claim' | 'Replacement' | 'Duplicate' | 'Other'

export interface DossierListItem {
  id: string
  dossierNumber: string
  title: string
  status: DossierStatus
  customerId: string | null
  customerName: string | null
  responsibleName: string | null
  orderCount: number
  openIncidentCount: number
  createdAt: string
  /** Klantreferentie van het dossier (niet de gegenereerde titel). */
  customerReference: string | null
  customerNumber: string | null
  /** Som van de afgesproken prijzen over alle geprijsde factureerbare eenheden; `null` = nog niet geprijsd (nooit 0 als "geen prijs"). */
  agreedPriceTotal: number | null
  pricedOrderCount: number
  /** Stap 13 (2026-09-11): factureerbare eenheden = activiteiten van een IsBillable-type (+ compat: losse opdrachten). */
  billableActivityCount: number
  pricedActivityCount: number
  /** Geprijsde eenheden met bedrag exact 0 — de lijst toont er een ⚠ voor. */
  zeroPricedActivityCount: number
  // --- Search/list redesign 2026-09-23 (GET /api/dossiers/search) ---
  /** Dossierdatum (YYYY-MM-DD); null op oude rijen. */
  dossierDate: string | null
  confirmedAt: string | null
  confirmationSource: DossierConfirmationSource | null
  activityCount: number
  /** Eerste laad- en laatste losplaats over de gekoppelde opdrachten. */
  firstLoadingCity: string | null
  lastUnloadingCity: string | null
  /** "Jan Peeters" of "2 chauffeurs"; null zonder planning. */
  driverSummary: string | null
  vehicleSummary: string | null
  planningDate: string | null
  invoiceStatus: DossierInvoiceStatus | null
  hasCmr: boolean
}

export type DossierInvoiceStatus = 'NotInvoiced' | 'Draft' | 'Sent' | 'Paid'
export type DossierPriceStatusFilter = 'Priced' | 'Partial' | 'Unpriced'
export type DossierSortKey = 'number' | 'date' | 'customer' | 'status' | 'confirmedAt' | 'planningDate' | 'createdAt'

export interface DossierOrder {
  linkId: string
  orderId: string
  orderNumber: string
  orderDate: string
  status: string
  goodsDescription: string | null
  agreedPrice: number | null
  /** Backend `OrderPricingState.IsPriced` (override, one-off agreement or positive amount). Absent on older payloads. */
  isPriced?: boolean
}

export interface DossierRelation {
  id: string
  relationType: DossierRelationType
  notes: string | null
  isOutgoing: boolean
  otherDossierId: string
  otherDossierNumber: string
  otherDossierTitle: string
}

export interface DossierIncident {
  id: string
  title: string
  incidentType: string
  status: string
  severity: string
  dueDate: string | null
}

export interface DossierFinancialSummary {
  /** Dossier total over ALL priced billable units (orders + standalone activities); the name is kept for compat. */
  agreedOrderTotal: number
  /** UX-sprint 2026-09-09: linked orders that carry a price (override or AgreedPrice > 0); 0 = "Nog geen prijs". */
  pricedOrderCount?: number
  /** Stap 13: billable units (activities of an IsBillable type + legacy orders without activity). Absent on older payloads. */
  billableActivityCount?: number
  pricedActivityCount?: number
  zeroPricedActivityCount?: number
  invoicedTotal: number
  estimatedIncidentCost: number
  actualIncidentCost: number
}

/** One activity card on the dossier: type capabilities + the linked execution record. */
export interface DossierActivity {
  id: string
  activityTypeId: string
  activityTypeCode: string
  activityTypeName: string
  icon: string | null
  hasStops: boolean
  supportsGoods: boolean
  allowsDuration: boolean
  sequence: number
  label: string | null
  linkedTransportOrderId: string | null
  linkedOrderNumber: string | null
  linkedOrderStatus: string | null
  linkedActivityId: string | null
  plannedDate: string | null
  durationHours: number | null
  notes: string | null
  // --- Stap 13 (2026-09-11): the activity as a billable unit ---
  /** Activity type flag: counts as a commercial unit (gets a sales price + pricing attention). */
  isBillable: boolean
  /** D2: the activity type allows on-site work (the linked order may be an on-site lifting job). */
  supportsOnSiteWork?: boolean
  /** "Order" for a transport activity WITH an order (the order carries the price); "None"/"OneOff" for standalone ones. */
  pricingSource: 'None' | 'OneOff' | 'Order' | 'Lines'
  /** Order price resp. activity price (null when unpriced). */
  agreedPrice: number | null
  /** Provenance flag (OrderPricingState resp. ActivityPricingState) — € 0 with provenance IS priced. */
  isPriced: boolean
  /** Snapshot status of the order resp. status of the activity price record (Draft/Reviewed/Locked/Invoiced). */
  pricingStatus: string | null
  /** Concurrency token of the activity price record; null without record and for orders (they use their own version). */
  pricingVersion: string | null
  // --- Master sprint 2026-09-21 (all optional: absent on older payloads) ---
  /** D1: the trip of the activity's order — the ONLY source of driver/vehicle/trailer; null = not on a trip. */
  assignment?: DossierActivityAssignment | null
  /** D5: derived price status; null/absent → fall back to `isPriced`. */
  priceStatus?: ActivityPriceStatus | null
  /** D5: explicitly confirmed as free (shown as "Gratis", never as a missing price). */
  freeConfirmed?: boolean
  /** D5: sales lines of a STANDALONE activity (server-computed amounts); empty/absent for order-backed ones. */
  priceLines?: DossierActivityPriceLine[] | null
  /** D7: notes on this activity + the latest one (the server cuts the preview to 160 characters). */
  noteCount?: number
  latestNotePreview?: string | null
  latestNoteAt?: string | null
  /** D6: issued transport documents (CMR/werkbon) and uploaded documents of the activity's order. */
  issuedDocuments?: { id: string; kind: 'Cmr' | 'DeliveryNote' | 'WorkOrder'; documentNumber: string }[]
  documentCount?: number
}

export type ActivityPriceStatus = 'NotPriced' | 'PartiallyPriced' | 'Priced' | 'Free'

/** D1: read-only projection of the trip an activity's order sits on. `otherOrderCount` > 0 = shared trip. */
export interface DossierActivityAssignment {
  tripId: string
  tripNumber: string
  tripDate: string
  tripStatus: string
  driverId: string | null
  driverName: string | null
  vehicleId: string | null
  vehicleNumber: string | null
  vehiclePlate: string | null
  /** 'Suggested' = proposed from the driver's fixed vehicle; 'Manual'/null = a planner's choice. */
  vehicleSelectionSource: 'Suggested' | 'Manual' | null
  trailerId: string | null
  trailerNumber: string | null
  trailerPlate: string | null
  tripCount: number
  otherOrderCount: number
}

export type ReadinessSeverity = 'Info' | 'Warning' | 'Blocking'
export type ReadinessSection = 'algemeen' | 'activiteiten' | 'route' | 'goederen' | 'prijs'

/** One actionable attention item (additive readiness projection). */
export interface ReadinessIssue {
  code: string
  severity: ReadinessSeverity
  message: string
  section: ReadinessSection
  field: string | null
  stage: string
  /** The linked order an order-level rule is about (null/absent for dossier-level rules). */
  transportOrderId?: string | null
  /** The activity an activity-level rule is about (e.g. route.order_missing). */
  activityId?: string | null
}

export interface DossierDetail {
  id: string
  dossierNumber: string
  title: string
  description: string | null
  status: DossierStatus
  customerId: string | null
  customerName: string | null
  responsibleUserId: string | null
  responsibleName: string | null
  closedAt: string | null
  notes: string | null
  createdAt: string
  orders: DossierOrder[]
  relations: DossierRelation[]
  incidents: DossierIncident[]
  financials: DossierFinancialSummary
  // --- Wave 1 dossier foundation ---
  customerReference: string | null
  dossierDate: string | null
  legalEntityId: string | null
  legalEntityName: string | null
  /** Concurrency token (uuid); echoed on every mutation — a mismatch yields 409 with the current state. */
  version: string
  activities: DossierActivity[]
  readiness: ReadinessIssue[]
  /** Redesign 2026-09-11 (Overzicht): documents on the linked orders — count + distinct type codes. Absent on older payloads. */
  documentCount?: number
  documentTypes?: string[]
  /** Latest change over the dossier, its activities and linked orders (ISO); null/absent when unknown. */
  lastChangedAt?: string | null
  /** D7: number of dossier-level notes (the legacy free-text `notes` is no longer shown or edited). */
  noteCount?: number
  // --- Confirmation sprint 2026-09-23 (status Closed = confirmed) ---
  confirmedAt?: string | null
  confirmedByUserId?: string | null
  confirmedByName?: string | null
  /** null on an open/cancelled dossier and on legacy closed rows (source unknown). */
  confirmationSource?: DossierConfirmationSource | null
  confirmationReason?: string | null
  cancelledAt?: string | null
  cancellationReason?: string | null
}

/** One finding of GET /api/dossiers/{id}/confirmation. */
export interface DossierConfirmationCheck {
  code: string
  severity: 'Blocking' | 'Warning' | 'Info'
  message: string
  transportOrderId: string | null
  activityId: string | null
}

export interface DossierConfirmationSummary {
  ordersTotal: number
  ordersCompleted: number
  ordersCancelled: number
  ordersOpen: number
  deliveriesFailed: number
  podMissing: number
  activitiesExecutable: number
  activitiesExecuted: number
  billableUnits: number
  pricedUnits: number
  documents: number
  hasCmr: boolean
  openIncidents: number
}

export interface DossierConfirmationEvaluation {
  dossierId: string
  status: DossierStatus
  canConfirmManually: boolean
  canAutoConfirm: boolean
  blockers: DossierConfirmationCheck[]
  warnings: DossierConfirmationCheck[]
  autoBlockers: DossierConfirmationCheck[]
  relevantOrderIds: string[]
  relevantActivityIds: string[]
  summary: DossierConfirmationSummary
}

export interface DossierInput {
  title: string
  description: string | null
  customerId: string | null
  responsibleUserId: string | null
  notes: string | null
  customerReference?: string | null
  dossierDate?: string | null
  /** Concurrency token; omitted = check skipped (legacy callers). */
  version?: string
}

/** Fast-create request (§8): customer is the ONLY required field. */
export interface NewDossierInput {
  customerId: string
  dossierDate?: string | null
  customerReference?: string | null
  /** Quick-start template tile; null/omitted = leeg dossier. */
  activityTypeId?: string | null
  title?: string | null
}

/** Add/update payload of one dossier activity (type immutable on update). */
export interface DossierActivityInput {
  activityTypeId: string
  label?: string | null
  plannedDate?: string | null
  durationHours?: number | null
  linkedActivityId?: string | null
  notes?: string | null
  /** HasStops types only: also create a draft order inside this dossier and link it. */
  createLinkedOrder?: boolean
  version?: string
}

/** Vertaalsleutels per status — renderen als t(DOSSIER_STATUS_LABELS[status]). */
export const DOSSIER_STATUS_LABELS: Record<DossierStatus, string> = {
  Open: 'dossiers.status.Open',
  Closed: 'dossiers.status.Closed',
  Cancelled: 'dossiers.status.Cancelled',
}

/** Open = in progress (info), Closed = confirmed (success), Cancelled = neutral. */
export const DOSSIER_STATUS_TONE: Record<DossierStatus, BadgeTone> = {
  Open: 'info',
  Closed: 'success',
  Cancelled: 'neutral',
}

/** Vertaalsleutels per relatietype — renderen als t(DOSSIER_RELATION_LABELS[type]). */
export const DOSSIER_RELATION_LABELS: Record<DossierRelationType, string> = {
  FollowUp: 'dossiers.relationType.FollowUp',
  Return: 'dossiers.relationType.Return',
  Claim: 'dossiers.relationType.Claim',
  Replacement: 'dossiers.relationType.Replacement',
  Duplicate: 'dossiers.relationType.Duplicate',
  Other: 'dossiers.relationType.Other',
}
