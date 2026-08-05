import type { PackageUnitType } from '../packages/types'

export type TransportOrderStatus = 'Draft' | 'Submitted' | 'Confirmed' | 'Planned' | 'InProgress' | 'Completed' | 'Invoiced' | 'Cancelled'
export type StopType = 'Loading' | 'Unloading'

export const ORDER_STATUS_LABELS: Record<TransportOrderStatus, string> = {
  Draft: 'Concept',
  Submitted: 'Ingediend',
  Confirmed: 'Bevestigd',
  Planned: 'Gepland',
  InProgress: 'In uitvoering',
  Completed: 'Afgerond',
  Invoiced: 'Gefactureerd',
  Cancelled: 'Geannuleerd',
}

export const ORDER_STATUSES: TransportOrderStatus[] = ['Draft', 'Submitted', 'Confirmed', 'Planned', 'InProgress', 'Completed', 'Invoiced', 'Cancelled']

export const ORDER_STATUS_TONE: Record<TransportOrderStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Draft: 'neutral',
  Submitted: 'warning',
  Confirmed: 'info',
  Planned: 'info',
  InProgress: 'warning',
  Completed: 'success',
  Invoiced: 'success',
  Cancelled: 'danger',
}

/** Action labels for the guarded transitions offered by the backend. */
export const ORDER_TRANSITION_LABELS: Record<TransportOrderStatus, string> = {
  Draft: 'Terug naar concept',
  Submitted: 'Indienen',
  Confirmed: 'Bevestigen',
  Planned: 'Plannen',
  Invoiced: 'Factureren',
  InProgress: 'Start uitvoering',
  Completed: 'Afronden',
  Cancelled: 'Annuleren',
}

export const STOP_TYPE_LABELS: Record<StopType, string> = {
  Loading: 'Laden',
  Unloading: 'Lossen',
}

export type OrderPriority = 'Low' | 'Normal' | 'High' | 'Urgent'

export const ORDER_PRIORITIES: OrderPriority[] = ['Low', 'Normal', 'High', 'Urgent']

export const ORDER_PRIORITY_LABELS: Record<OrderPriority, string> = {
  Low: 'Laag',
  Normal: 'Normaal',
  High: 'Hoog',
  Urgent: 'Dringend',
}

export const ORDER_PRIORITY_TONE: Record<OrderPriority, 'neutral' | 'info' | 'warning' | 'danger'> = {
  Low: 'neutral',
  Normal: 'neutral',
  High: 'warning',
  Urgent: 'danger',
}

export interface TransportOrderListItem {
  id: string
  orderNumber: string
  orderDate: string
  customerId: string
  customerName: string
  customerReference: string | null
  status: TransportOrderStatus
  goodsDescription: string | null
  firstLoadingCity: string | null
  lastUnloadingCity: string | null
  stopCount: number
  adrRequired: boolean
  craneRequired: boolean
  priority: OrderPriority
}

export interface TransportOrderStop {
  id: string
  sequence: number
  stopType: StopType
  locationId: string | null
  locationCode: string | null
  locationName: string
  address: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  plannedFrom: string | null
  plannedTo: string | null
  requestedFrom: string | null
  requestedTo: string | null
  confirmedFrom: string | null
  confirmedTo: string | null
  earliestAllowed: string | null
  latestAllowed: string | null
  appointmentRequired: boolean
  appointmentReference: string | null
  reference: string | null
  instructions: string | null
  accessInstructions: string | null
  loadingInstructions: string | null
  unloadingInstructions: string | null
  /** Wave 2026-08-04 §15: simple time requirement ("Leveren vóór 10:00"). */
  timeRequirement?: StopTimeRequirementKind
  /** "HH:mm:ss" wire format (TimeOnly). */
  timeRequirementFrom?: string | null
  timeRequirementTo?: string | null
  /** Wave 2026-08-04 §18: stop-level included-minutes override (stop → order → contract). */
  includedTimeMinutesOverride?: number | null
  // --- Location snapshot (master-data wave 2026-08-05, Phase 7): frozen at order time ---
  contactName?: string | null
  contactPhone?: string | null
  contactMobile?: string | null
  contactEmail?: string | null
  openingHoursSummary?: string | null
  gate?: string | null
  /** Sensitive: null unless the caller holds locations.view_sensitive. */
  accessCode?: string | null
  dock?: string | null
  routeDescription?: string | null
  defaultLoadingMinutes?: number | null
  defaultUnloadingMinutes?: number | null
  snapshotAt?: string | null
  /** Advisory opening-hours warnings (live location hours vs planned times); never blocking. */
  warnings?: string[] | null
}

/** Wave 2026-08-04 §15. */
export type StopTimeRequirementKind = 'None' | 'Before' | 'After' | 'Window'

export interface CargoItem {
  id: string
  sequence: number
  description: string | null
  barcode: string | null
  expectedQuantity: number
  quantityUnit: string | null
  quantityUnitCode: string | null
  notes: string | null
  unitType: PackageUnitType | null
  unitTypeLabel: string | null
  totalWeightKg: number | null
  weightPerUnitKg: number | null
  lengthMeters: number | null
  widthMeters: number | null
  heightMeters: number | null
  volumeM3: number | null
  volumeIsManual: boolean
  adrRequired: boolean
  adrDetails: string | null
  stackable: boolean
  reference: string | null
  loadingStopId: string | null
  unloadingStopId: string | null
  /** Optional per-line pallet count; a commercial detail, independent of scanable colli. */
  palletCount: number | null
}

export interface CargoItemInput {
  /** Matches an existing cargo line on update (id-preserving sync); omitted/unmatched = new line. */
  id?: string | null
  description: string | null
  barcode: string | null
  expectedQuantity: number
  quantityUnit: string | null
  quantityUnitCode: string | null
  notes: string | null
  unitType: PackageUnitType | null
  unitTypeLabel: string | null
  totalWeightKg: number | null
  weightPerUnitKg: number | null
  lengthMeters: number | null
  widthMeters: number | null
  heightMeters: number | null
  volumeM3: number | null
  volumeIsManual: boolean
  adrRequired: boolean
  adrDetails: string | null
  stackable: boolean
  reference: string | null
  /** Index into the submitted stops list (stops get fresh ids on every save). */
  loadingStopIndex: number | null
  unloadingStopIndex: number | null
  /** Optional per-line pallet count; a commercial detail, independent of scanable colli. */
  palletCount?: number | null
}

export type OrderPricingSource = 'Contract' | 'OneOff'

export interface TransportOrderDetail {
  id: string
  orderNumber: string
  orderDate: string
  customerId: string
  customerName: string
  customerReference: string | null
  status: TransportOrderStatus
  goodsDescription: string | null
  quantity: number | null
  quantityUnit: string | null
  quantityUnitCode: string | null
  weightKg: number | null
  volumeM3: number | null
  palletCount: number | null
  adrRequired: boolean
  craneRequired: boolean
  agreedPrice: number | null
  notes: string | null
  cancellationReason: string | null
  stops: TransportOrderStop[]
  cargoItems: CargoItem[]
  allowedTransitions: TransportOrderStatus[]
  allowedCorrections: TransportOrderStatus[]
  canCancel: boolean
  priority: OrderPriority
  /** Facturerende entiteit voor deze opdracht; null = klantstandaard. */
  legalEntityId: string | null
  /** Afwijkend dieseltoeslagpercentage voor deze opdracht (los van de klantconfiguratie). */
  dieselSurchargeOverride: boolean
  dieselSurchargePercentOverride: number | null
  dieselSurchargeOverrideReason: string | null
  /** Engine result at last save; null when nothing could be calculated. */
  calculatedPrice: number | null
  priceIsManual: boolean
  priceOverrideReason: string | null
  pricingLines: OrderPricingLine[] | null
  serviceLines: OrderServiceLine[] | null
  pricingSnapshot: OrderPricingSnapshot | null
  /** Contract (default) or OneOff (Phase 6): this order carries its own price agreement. */
  pricingSource: OrderPricingSource
  oneOffFixedAmount: number | null
  oneOffIncludedLoadingMinutes: number | null
  oneOffIncludedUnloadingMinutes: number | null
  oneOffIncludedCombinedMinutes: number | null
  oneOffExtraHourlyRate: number | null
  oneOffNotes: string | null
  /** CalculatedPrice + proposed (unconfirmed) extra-time charges; null when nothing could be calculated. */
  totalWithProposed: number | null
  /** Task 11: order-level overrides of the engaged contract agreement's included time/rate (contract mode only). */
  includedLoadingMinutesOverride: number | null
  includedUnloadingMinutesOverride: number | null
  extraTimeHourlyRateOverride: number | null
  extraTimeRoundingStepMinutes: number | null
  extraTimeMinimumBillableMinutes: number | null
}

/** Manual-editing lifecycle of a pricing line (spec ch. 24-26). */
export type OrderPriceLineKind = 'Auto' | 'AutoAdjusted' | 'Manual' | 'Proposed'

export const ORDER_PRICE_LINE_KIND_LABELS: Record<OrderPriceLineKind, string> = {
  Auto: 'AUTO',
  AutoAdjusted: 'OVERRIDE',
  Manual: 'MANUEEL',
  Proposed: 'VOORSTEL',
}

export const ORDER_PRICE_LINE_KIND_TONE: Record<OrderPriceLineKind, 'neutral' | 'warning' | 'info' | 'success' | 'danger'> = {
  Auto: 'neutral',
  AutoAdjusted: 'warning',
  Manual: 'info',
  Proposed: 'warning',
}

/** Draft → Reviewed → Locked → Invoiced (spec ch. 24-26); Invoiced is set only by invoicing. */
export type OrderPricingStatus = 'Draft' | 'Reviewed' | 'Locked' | 'Invoiced'

/**
 * Wave 2026-08-04 §8: the visible workflow speaks in confirmation terms — the technical
 * Draft/Reviewed/Locked lifecycle stays underneath but is never shown to normal users.
 */
export const ORDER_PRICING_STATUS_LABELS: Record<OrderPricingStatus, string> = {
  Draft: 'Nog te bevestigen',
  Reviewed: 'Nog te bevestigen',
  Locked: 'Bevestigd',
  Invoiced: 'Gefactureerd',
}

export const ORDER_PRICING_STATUS_TONE: Record<OrderPricingStatus, 'neutral' | 'info' | 'warning' | 'success'> = {
  Draft: 'warning',
  Reviewed: 'warning',
  Locked: 'success',
  Invoiced: 'success',
}

/**
 * Visible price status (wave 2026-08-04 §9): Bevestigd (green) / Gefactureerd / Onvolledig
 * (red, unconfirmed with unpriced goods) / Nog te bevestigen (orange).
 */
export function priceStatusDisplay(
  status: OrderPricingStatus | undefined,
  hasUnpricedGoods: boolean,
): { label: string; tone: 'neutral' | 'warning' | 'success' | 'danger' } {
  if (status === 'Invoiced') return { label: 'Gefactureerd', tone: 'success' }
  if (status === 'Locked') return { label: 'Bevestigd', tone: 'success' }
  if (hasUnpricedGoods) return { label: 'Onvolledig', tone: 'danger' }
  return { label: 'Nog te bevestigen', tone: 'warning' }
}

export interface OrderPricingLine {
  /** Persisted line id; needed to target the confirm endpoint on a VOORSTEL line. */
  id?: string | null
  label: string
  amount: number
  source: string
  informational: boolean
  ruleName?: string | null
  agreementName?: string | null
  actualQuantity?: number | null
  billableQuantity?: number | null
  /** An unconfirmed extra-time charge (Phase 6): excluded from AgreedPrice, shown as a proposal ("VOORSTEL"). */
  proposed?: boolean
  kind: OrderPriceLineKind
  quantity?: number | null
  unitPrice?: number | null
  originalQuantity?: number | null
  originalUnitPrice?: number | null
  originalAmount?: number | null
  adjustReason?: string | null
  /** Stable merge key ("rule:{id}", "service:{id}", "manual:{guid}", ...); null only for a brand-new free line. */
  lineKey?: string | null
  /** Managed unit code for quantity (e.g. "COLLI"), editable on manual lines (spec Task 5/6). */
  unit?: string | null
  /** Frozen identity of the service option, for merge-matching (see lineKey too). */
  serviceOptionId?: string | null
}

/** Badge text for a pricing line: an Auto line tied to a service option reads as DIENST rather than AUTO. */
export function lineBadge(line: Pick<OrderPricingLine, 'kind' | 'serviceOptionId'>): string {
  return line.kind === 'Auto' && line.serviceOptionId ? 'DIENST' : ORDER_PRICE_LINE_KIND_LABELS[line.kind]
}

/** Pricing coverage of one commercial goods line/unit (wave 2026-08-04 §7). */
export type PricingCoverageStatus = 'Full' | 'Partial' | 'None'

export interface OrderPricingCoverage {
  unitTypeId: string | null
  unitLabel: string
  quantity: number
  status: PricingCoverageStatus
  baseAmount: number
  baseRuleName: string | null
  servicesAmount: number
  reason: string | null
}

export const PRICING_COVERAGE_LABELS: Record<PricingCoverageStatus, string> = {
  Full: 'Volledig geprijsd',
  Partial: 'Gedeeltelijk geprijsd',
  None: 'Niet geprijsd',
}

export const PRICING_COVERAGE_TONE: Record<PricingCoverageStatus, 'success' | 'warning' | 'danger'> = {
  Full: 'success',
  Partial: 'warning',
  None: 'danger',
}

/** Frozen header of the order's pricing snapshot (spec ch. 21, extended ch. 24-26). */
export interface OrderPricingSnapshot {
  tariffDate: string
  currency: string
  zoneCode: string | null
  zoneName: string | null
  agreementNames: string | null
  unitSummary: string | null
  calculatedTotal: number | null
  overrideAmount: number | null
  overrideReason: string | null
  overriddenByUserId: string | null
  overriddenAtUtc: string | null
  explanation: string | null
  status: OrderPricingStatus
  linesTotal: number | null
  /** Wave 2026-08-04 §7: per-goods-line pricing coverage frozen with the calculation. */
  coverage?: OrderPricingCoverage[] | null
  /** Wave 2026-08-04 §8: confirmation metadata ("Bevestigd op … door …"). */
  confirmedAtUtc?: string | null
  confirmedByUserId?: string | null
  confirmedByName?: string | null
  /** Non-null: confirmed despite unpriced goods — keep a visible warning attached. */
  confirmedWithUnpricedGoodsReason?: string | null
}

export interface OrderServiceLine {
  serviceOptionId: string | null
  name: string
  kind: 'Percent' | 'Fixed' | 'PerHour' | 'PerStop' | 'PerUnit' | 'PerOrderLine' | 'PerKg' | 'PerM3' | 'PerLdm' | 'PerDay' | 'PerPalletDay'
  value: number
  amount: number
  quantity?: number | null
  /** Per-pallet-day input: quantity = palletCount × dayCount unless manually corrected. */
  palletCount?: number | null
  /** Per-day / per-pallet-day input: number of days. */
  dayCount?: number | null
  /** Optional free-text note the user attached to a manually selected service. */
  note?: string | null
}

export interface StopInput {
  stopType: StopType
  locationId: string | null
  locationName: string | null
  address: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  plannedFrom: string | null
  plannedTo: string | null
  requestedFrom: string | null
  requestedTo: string | null
  confirmedFrom: string | null
  confirmedTo: string | null
  earliestAllowed: string | null
  latestAllowed: string | null
  appointmentRequired: boolean
  appointmentReference: string | null
  reference: string | null
  instructions: string | null
  accessInstructions: string | null
  loadingInstructions: string | null
  unloadingInstructions: string | null
  /** Wave 2026-08-04 §15: simple time requirement; "HH:mm" times. */
  timeRequirement?: StopTimeRequirementKind
  timeRequirementFrom?: string | null
  timeRequirementTo?: string | null
  /** Wave 2026-08-04 §18: stop-level included-minutes override (stop → order → contract). */
  includedTimeMinutesOverride?: number | null
  /**
   * Existing stop id echoed on update (Phase 7): a matching id with an unchanged locationId
   * carries the location snapshot over instead of re-copying live master data.
   */
  id?: string | null
  /** True = deliberately re-copy the CURRENT master-location data onto this stop (audited). */
  refreshSnapshot?: boolean
}

/** Dispatcher-side execution planning of one stop (separate endpoint, editable after planning). */
export interface StopExecutionPlanInput {
  confirmedFrom: string | null
  confirmedTo: string | null
  earliestAllowed: string | null
  latestAllowed: string | null
  appointmentRequired: boolean
  appointmentReference: string | null
  accessInstructions: string | null
  loadingInstructions: string | null
  unloadingInstructions: string | null
}

export interface TransportOrderInput {
  customerId: string
  customerReference: string | null
  orderDate: string | null
  goodsDescription: string | null
  quantity: number | null
  quantityUnit: string | null
  quantityUnitCode: string | null
  weightKg: number | null
  volumeM3: number | null
  palletCount: number | null
  adrRequired: boolean
  craneRequired: boolean
  agreedPrice: number | null
  notes: string | null
  stops: StopInput[]
  cargoItems: CargoItemInput[]
  /** Omitted = Normal on create, unchanged on update. */
  priority?: OrderPriority
  /** Facturerende entiteit; null = klantstandaard. */
  legalEntityId: string | null
  dieselSurchargeOverride: boolean
  dieselSurchargePercentOverride: number | null
  dieselSurchargeOverrideReason: string | null
  /** Selected delivery services/supplements; priced by the engine and snapshotted. */
  serviceOptionIds?: string[]
  /** Preferred over serviceOptionIds: selections incl. quantities (per-hour/per-stop/per-day/per-pallet-day services). */
  services?: {
    serviceOptionId: string
    quantity: number | null
    palletCount?: number | null
    dayCount?: number | null
    /** Optional free-text note the user attaches to a manually selected service. */
    note?: string | null
  }[]
  /** Explicit authorized override of the calculated price (orders.override_price). */
  priceIsManual?: boolean
  priceOverrideReason?: string | null
  /** Contract (default) or OneOff (Phase 6): this order carries its own price agreement. */
  pricingSource?: OrderPricingSource
  oneOffFixedAmount?: number | null
  oneOffIncludedLoadingMinutes?: number | null
  oneOffIncludedUnloadingMinutes?: number | null
  oneOffIncludedCombinedMinutes?: number | null
  oneOffExtraHourlyRate?: number | null
  oneOffNotes?: string | null
  /** Task 11: order-level overrides of the engaged contract agreement's included time/rate (contract mode only). */
  includedLoadingMinutesOverride?: number | null
  includedUnloadingMinutesOverride?: number | null
  extraTimeHourlyRateOverride?: number | null
  extraTimeRoundingStepMinutes?: number | null
  extraTimeMinimumBillableMinutes?: number | null
}
