import type { BadgeTone } from '../../components/ui/Badge'

export type DossierStatus = 'Open' | 'Closed'

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
}

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
  /** "Order" for a transport activity WITH an order (the order carries the price); "None"/"OneOff" for standalone ones. */
  pricingSource: 'None' | 'OneOff' | 'Order'
  /** Order price resp. activity price (null when unpriced). */
  agreedPrice: number | null
  /** Provenance flag (OrderPricingState resp. ActivityPricingState) — € 0 with provenance IS priced. */
  isPriced: boolean
  /** Snapshot status of the order resp. status of the activity price record (Draft/Reviewed/Locked/Invoiced). */
  pricingStatus: string | null
  /** Concurrency token of the activity price record; null without record and for orders (they use their own version). */
  pricingVersion: string | null
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
}

export const DOSSIER_STATUS_TONE: Record<DossierStatus, BadgeTone> = {
  Open: 'success',
  Closed: 'neutral',
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
