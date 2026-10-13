import { getActiveLocale } from '../../../../i18n/activeLocale'
import { translate } from '../../../../i18n/translations'
import { fromWireDateTime, toDateTimeLocalInput } from '../../../../utils/dates'
import { parseDecimalInput } from '../../../../utils/numbers'
import { computeVolumeM3 } from '../../../../utils/volume'
import { inferTotalWeightIsManual, recalculateTotalWeight } from './cargoWeight'
import type { ServiceOption, UnitTypeMaster } from '../../../tarification/api/pricingApi'
import type { PackageUnitType } from '../../../packages/types'
import { fullAddressLine, streetLine, type PickedAddress } from '../../../locations/addressFields'
import { postalCodeErrorKey } from '../../../locations/postalCode'
import type { CraneJobKind, StopInput, TransportOrderDetail } from '../../types'

/**
 * Form state types, initial-state builders and validation of the transport-order form.
 * Extracted from `TransportOrderForm.tsx` (Wave 1 phase 6) so the form composes section
 * components; the submit-payload mapping lives in `orderFormPayload.ts` (size budget).
 * Nothing here renders — pure state and mapping.
 */

export interface CargoFormRow {
  key: string
  /** Existing cargo line id (id-preserving update sync); null for a new/unsaved row. */
  id: string | null
  description: string
  barcode: string
  expectedQuantity: string
  quantityUnit: string
  quantityUnitCode: string | null
  notes: string
  unitType: PackageUnitType | ''
  unitTypeLabel: string
  totalWeightKg: string
  weightPerUnitKg: string
  /**
   * D4: false = total follows quantity × weight per unit; true once the planner typed the total.
   * Form-only (the API stores no flag) — see `cargoWeight.ts`.
   */
  totalWeightIsManual: boolean
  lengthMeters: string
  widthMeters: string
  heightMeters: string
  volumeM3: string
  volumeIsManual: boolean
  adrRequired: boolean
  adrDetails: string
  stackable: boolean
  reference: string
  /** '' = automatic (unambiguous orders link themselves server-side). */
  loadingStopIndex: string
  unloadingStopIndex: string
  /** Optional commercial pallet count for this line; independent of scanable colli. */
  palletCount: string
}

export interface StopFormRow {
  key: string
  /** Existing stop id (Phase 7): echoed on submit so the backend can carry the location snapshot over. */
  id: string | null
  /** Phase 7: pending "Adres opnieuw overnemen" — applied by the backend on save. */
  refreshSnapshot: boolean
  /** Snapshot display line for a master-location stop ("Magazijn Antwerpen"). */
  snapshotName: string
  /** Snapshot address line ("Noorderlaan 10, 2030 Antwerpen"). */
  snapshotAddress: string
  stopType: StopInput['stopType']
  locationId: string
  /** D3: the address fields below are ALWAYS shown and submitted — with or without a `locationId`. */
  locationName: string
  address: string
  postalCode: string
  city: string
  countryCode: string
  /** D3: the fields deviate from the linked address-book record — for this dossier only. */
  addressOverridden: boolean
  /** D3 (write-only): also store this new/deviating address in the customer's address book on save. */
  saveToAddressBook: boolean
  /** Form-only: the postal code was typed in THIS session — only then is its format validated. */
  postalCodeTouched: boolean
  /** §14: one date + optional from/to time replace the raw planned datetime pair. */
  date: string
  fromTime: string
  toTime: string
  /** D2, site stops only: end DATE of the job ('' = same day as `date`); 22:00 + 4 u ends the next day. */
  toDate: string
  /** D2, site stops only: false = the end follows start + duration; true = typed by the planner. */
  plannedToIsManual: boolean
  /** §15: simple time requirement ('' = geen specifieke eis). */
  timeRequirement: '' | 'Before' | 'After' | 'Window'
  timeReqFrom: string
  timeReqTo: string
  requestedFrom: string
  requestedTo: string
  confirmedFrom: string
  confirmedTo: string
  earliestAllowed: string
  latestAllowed: string
  appointmentRequired: boolean
  appointmentReference: string
  reference: string
  instructions: string
  accessInstructions: string
  loadingInstructions: string
  unloadingInstructions: string
  /** §18: stop-level included-minutes override ('' = geen afwijking). */
  includedTimeMinutesOverride: string
  /**
   * Placeholder row an editor seeds for a stop type the route still lacks (dossier route editor).
   * Re-seeded from the saved order after every save, so dropping it untouched loses nothing — unlike
   * a row the planner added, which is only dropped after explicit confirmation.
   */
  seeded?: boolean
  /** §13: compact card state for orders with many stops. */
  collapsed: boolean
}

/** Translation KEY per calculation method (render via t()), for "Berekeningswijze" and kind badges. */
export const SERVICE_KIND_LABELS: Record<ServiceOption['kind'], string> = {
  Percent: 'transportOrders.serviceKind.Percent',
  Fixed: 'transportOrders.serviceKind.Fixed',
  PerHour: 'transportOrders.serviceKind.PerHour',
  PerStop: 'transportOrders.serviceKind.PerStop',
  PerUnit: 'transportOrders.serviceKind.PerUnit',
  PerOrderLine: 'transportOrders.serviceKind.PerOrderLine',
  PerKg: 'transportOrders.serviceKind.PerKg',
  PerM3: 'transportOrders.serviceKind.PerM3',
  PerLdm: 'transportOrders.serviceKind.PerLdm',
  PerDay: 'transportOrders.serviceKind.PerDay',
  PerPalletDay: 'transportOrders.serviceKind.PerPalletDay',
  PerKm: 'transportOrders.serviceKind.PerKm',
}

export function numberOrNullFrom(value: string): number | null {
  return parseDecimalInput(value)
}

let rowKeyCounter = 0
export function nextRowKey(): string {
  rowKeyCounter += 1
  return `stop-${rowKeyCounter}`
}

/** Country a new stop starts with; a different value counts as entered data (see `isEmptyStopRow`). */
export const DEFAULT_STOP_COUNTRY = 'BE'

export function emptyStop(stopType: StopInput['stopType']): StopFormRow {
  return {
    key: nextRowKey(),
    id: null,
    refreshSnapshot: false,
    snapshotName: '',
    snapshotAddress: '',
    stopType,
    locationId: '',
    locationName: '',
    address: '',
    postalCode: '',
    city: '',
    countryCode: DEFAULT_STOP_COUNTRY,
    addressOverridden: false,
    saveToAddressBook: false,
    postalCodeTouched: false,
    date: '',
    fromTime: '',
    toTime: '',
    toDate: '',
    plannedToIsManual: false,
    timeRequirement: '',
    timeReqFrom: '',
    timeReqTo: '',
    requestedFrom: '',
    requestedTo: '',
    confirmedFrom: '',
    confirmedTo: '',
    earliestAllowed: '',
    latestAllowed: '',
    appointmentRequired: false,
    appointmentReference: '',
    reference: '',
    instructions: '',
    accessInstructions: '',
    loadingInstructions: '',
    unloadingInstructions: '',
    includedTimeMinutesOverride: '',
    collapsed: false,
  }
}

export function emptyCargoRow(): CargoFormRow {
  return {
    key: nextRowKey(),
    id: null,
    description: '',
    barcode: '',
    expectedQuantity: '1',
    quantityUnit: '',
    quantityUnitCode: null,
    notes: '',
    unitType: '',
    unitTypeLabel: '',
    totalWeightKg: '',
    weightPerUnitKg: '',
    totalWeightIsManual: false,
    lengthMeters: '',
    widthMeters: '',
    heightMeters: '',
    volumeM3: '',
    volumeIsManual: false,
    adrRequired: false,
    adrDetails: '',
    stackable: true,
    reference: '',
    loadingStopIndex: '',
    unloadingStopIndex: '',
    palletCount: '',
  }
}

/**
 * Selecting a unit auto-fills the physical defaults from the unit master data (spec §2.2):
 * Fixed always sets them, DefaultButOverridable only fills what is still empty, Variable
 * leaves everything to the planner. Pure helper shared by the order form and the dossier
 * intake — one behaviour, one place.
 */
export function applyUnitToCargoRow(row: CargoFormRow, code: string | null, master: UnitTypeMaster | null): CargoFormRow {
  const next: CargoFormRow = { ...row, quantityUnitCode: code }
  if (!master || master.dimensionBehavior === 'Variable') return next
  const fixed = master.dimensionBehavior === 'Fixed'
  const cmToM = (cm: number | null) => (cm === null ? null : String(cm / 100))
  const fill = (current: string, cm: number | null) => {
    const value = cmToM(cm)
    if (value === null) return fixed ? '' : current
    return fixed || current.trim() === '' ? value : current
  }
  next.lengthMeters = fill(row.lengthMeters, master.defaultLengthCm)
  next.widthMeters = fill(row.widthMeters, master.defaultWidthCm)
  next.heightMeters = fill(row.heightMeters, master.defaultHeightCm)
  if (master.defaultWeightKg !== null && (fixed || row.weightPerUnitKg.trim() === '')) {
    next.weightPerUnitKg = String(master.defaultWeightKg)
    // D4: a unit default is a weight per unit like any other — an automatic total follows it.
    if (!next.totalWeightIsManual) return recalculateTotalWeight(next)
  }
  return next
}

/**
 * "Regel dupliceren" (D4): different weights per unit are separate goods lines, so a copy is the
 * fast path. The copy is a NEW line — no id, and no barcode (barcodes must stay unique).
 */
export function duplicateCargoRow(row: CargoFormRow): CargoFormRow {
  return { ...row, key: nextRowKey(), id: null, barcode: '' }
}

/** The copy lands right under its source, so the planner sees what was duplicated. */
export function duplicateCargoRowInList(rows: CargoFormRow[], key: string): CargoFormRow[] {
  return rows.flatMap((row) => (row.key === key ? [row, duplicateCargoRow(row)] : [row]))
}

/** True when a stop row was never touched (no address, no location, no planning) — intake fast path. */
/**
 * True only for a row the planner never touched: a NEW stop (no persisted id) with every
 * persisted field still at its `emptyStop` default. Such a row may be dropped; any other row —
 * a persisted stop, or a new one carrying a date, a window, a reference, an instruction, a
 * free address, a changed country, … — is "incomplete" at most and must be validated, never
 * discarded silently (data-safety contract of the route editors, 2026-09-11).
 */
export function isEmptyStopRow(stop: StopFormRow): boolean {
  if (stop.id) return false
  if (stop.appointmentRequired || stop.countryCode !== DEFAULT_STOP_COUNTRY) return false
  const persistedText: string[] = [
    stop.locationId, stop.locationName, stop.address, stop.postalCode, stop.city,
    stop.date, stop.fromTime, stop.toTime,
    stop.timeRequirement, stop.timeReqFrom, stop.timeReqTo,
    stop.requestedFrom, stop.requestedTo, stop.confirmedFrom, stop.confirmedTo,
    stop.earliestAllowed, stop.latestAllowed, stop.appointmentReference,
    stop.reference, stop.instructions, stop.accessInstructions, stop.loadingInstructions, stop.unloadingInstructions,
    stop.includedTimeMinutesOverride,
  ]
  return persistedText.every((value) => value.trim() === '')
}

/** True when a cargo row carries no meaningful content beyond the seeded quantity. */
export function isEmptyCargoRow(cargo: CargoFormRow): boolean {
  return (
    !cargo.description.trim() && !cargo.quantityUnitCode && !cargo.barcode.trim() &&
    !cargo.totalWeightKg.trim() && !cargo.weightPerUnitKg.trim() && !cargo.volumeM3.trim() &&
    !cargo.lengthMeters.trim() && !cargo.widthMeters.trim() && !cargo.heightMeters.trim() &&
    !cargo.palletCount.trim() && !cargo.reference.trim() && !cargo.notes.trim() && !cargo.adrRequired
  )
}

/**
 * Migration action (wave 2026-08-04 §2): the legacy header summary becomes the first
 * commercial line, so nothing is double-counted once lines take over.
 */
export function cargoRowFromHeader(header: {
  quantity: string
  quantityUnit: string
  quantityUnitCode: string | null
  weightKg: string
  volumeM3: string
  palletCount: string
}): CargoFormRow {
  return {
    ...emptyCargoRow(),
    expectedQuantity: header.quantity !== '' && Number(header.quantity) > 0 ? header.quantity : '1',
    quantityUnit: header.quantityUnit,
    quantityUnitCode: header.quantityUnitCode,
    totalWeightKg: header.weightKg,
    // The header weight is a typed total without a weight per unit: it stays exactly as entered.
    totalWeightIsManual: header.weightKg.trim() !== '',
    volumeM3: header.volumeM3,
    volumeIsManual: header.volumeM3 !== '',
    palletCount: header.palletCount,
  }
}

/**
 * Wire timestamp → `<input type="datetime-local">` value (minute precision). C-03: the ISO substring it used to
 * take showed UTC while the label said local time; the conversion now runs through the tenant
 * zone. Kept as a named re-export so the call sites and their tests stay readable.
 */
export function toLocalInput(value: string | null): string {
  return toDateTimeLocalInput(value)
}

/** "10:00:00" (TimeOnly wire format) → "10:00" for <input type="time">. */
export function toTimeInput(value: string | null | undefined): string {
  return value ? value.slice(0, 5) : ''
}

/** §15 badge text ("Vóór 10:00") for a stop's time requirement, '' when none. Locale-aware via the active locale. */
export function timeRequirementBadge(stop: Pick<StopFormRow, 'timeRequirement' | 'timeReqFrom' | 'timeReqTo'>): string {
  const locale = getActiveLocale()
  switch (stop.timeRequirement) {
    case 'Before':
      return stop.timeReqTo ? translate(locale, 'transportOrders.timeReq.before', { time: stop.timeReqTo }) : ''
    case 'After':
      return stop.timeReqFrom ? translate(locale, 'transportOrders.timeReq.after', { time: stop.timeReqFrom }) : ''
    case 'Window':
      return stop.timeReqFrom && stop.timeReqTo ? `${stop.timeReqFrom}–${stop.timeReqTo}` : ''
    default:
      return ''
  }
}

/**
 * §14 planned window → the form's one-date-plus-two-times shape, read in the tenant zone.
 * A lone midnight "from" is the wire encoding of a date-only stop — shown as an empty time.
 */
function plannedWindowFields(
  plannedFrom: string | null, plannedTo: string | null,
): Pick<StopFormRow, 'date' | 'fromTime' | 'toTime' | 'toDate'> {
  const from = fromWireDateTime(plannedFrom)
  const to = fromWireDateTime(plannedTo)
  return {
    date: from?.date ?? to?.date ?? '',
    fromTime: from && !(from.time === '00:00' && !to) ? from.time : '',
    toTime: to?.time ?? '',
    // D2: only a site stop submits its own end date (a job may end the next day).
    toDate: to?.date ?? '',
  }
}

/**
 * D3: every field an address-book record fills on a stop. The location NAME goes into the name
 * field (never the whole address line); the link is fresh, so nothing deviates and nothing has to
 * be stored again. Shared by every way of picking an address (search field, street suggestions,
 * quick-create, duplicate hint, "Adres opnieuw overnemen").
 */
export function stopPatchFromAddress(address: PickedAddress): Partial<StopFormRow> {
  return {
    locationId: address.locationId,
    locationName: address.name,
    address: streetLine(address),
    postalCode: address.postalCode ?? '',
    city: address.city ?? '',
    countryCode: address.countryCode?.trim() || DEFAULT_STOP_COUNTRY,
    snapshotName: address.name,
    snapshotAddress: fullAddressLine(address),
    addressOverridden: false,
    saveToAddressBook: false,
    postalCodeTouched: false,
  }
}

/** Stop rows for the form: mapped from an existing order, or the default laad+los pair. */
export function stopsFromOrder(order: TransportOrderDetail | undefined): StopFormRow[] {
  if (!order || order.stops.length === 0) {
    return [emptyStop('Loading'), emptyStop('Unloading')]
  }
  return order.stops.map((s, index) => ({
    key: nextRowKey(),
    id: s.id,
    refreshSnapshot: false,
    snapshotName: s.locationId ? s.locationName : '',
    snapshotAddress: s.locationId
      ? [s.address, [s.postalCode, s.city].filter(Boolean).join(' ')].filter(Boolean).join(', ')
      : '',
    stopType: s.stopType,
    locationId: s.locationId ?? '',
    // D3: the stored snapshot is shown as-is the moment a dossier is reopened — name included.
    locationName: s.locationName ?? '',
    address: s.address ?? '',
    postalCode: s.postalCode ?? '',
    city: s.city ?? '',
    countryCode: s.countryCode ?? 'BE',
    addressOverridden: Boolean(s.locationId) && (s.addressOverridden ?? false),
    saveToAddressBook: false,
    postalCodeTouched: false,
    plannedToIsManual: s.stopType === 'Site' && (s.plannedToIsManual ?? false),
    // C-03: the planned window is a UTC instant on the wire; the form edits the TENANT wall
    // clock, so date and times come from one conversion (never an ISO substring, which showed
    // 06:00 for an 08:00 stop and wrote that back on the next save).
    ...plannedWindowFields(s.plannedFrom, s.plannedTo),
    timeRequirement: s.timeRequirement && s.timeRequirement !== 'None' ? s.timeRequirement : '',
    timeReqFrom: toTimeInput(s.timeRequirementFrom),
    timeReqTo: toTimeInput(s.timeRequirementTo),
    requestedFrom: toLocalInput(s.requestedFrom),
    requestedTo: toLocalInput(s.requestedTo),
    confirmedFrom: toLocalInput(s.confirmedFrom),
    confirmedTo: toLocalInput(s.confirmedTo),
    earliestAllowed: toLocalInput(s.earliestAllowed),
    latestAllowed: toLocalInput(s.latestAllowed),
    appointmentRequired: s.appointmentRequired,
    appointmentReference: s.appointmentReference ?? '',
    reference: s.reference ?? '',
    instructions: s.instructions ?? '',
    accessInstructions: s.accessInstructions ?? '',
    loadingInstructions: s.loadingInstructions ?? '',
    unloadingInstructions: s.unloadingInstructions ?? '',
    includedTimeMinutesOverride:
      s.includedTimeMinutesOverride != null ? String(s.includedTimeMinutesOverride) : '',
    // Beyond the primary loading/unloading pair stops start compact (§13).
    collapsed: index >= 2,
  }))
}

/** Cargo rows for the form, mapped from an existing order's lines. */
export function cargoFromOrder(order: TransportOrderDetail | undefined): CargoFormRow[] {
  return (order?.cargoItems ?? []).map((c) => {
    const loadingIndex = order?.stops.findIndex((s) => s.id === c.loadingStopId) ?? -1
    const unloadingIndex = order?.stops.findIndex((s) => s.id === c.unloadingStopId) ?? -1
    return {
      key: nextRowKey(),
      id: c.id,
      description: c.description ?? '',
      barcode: c.barcode ?? '',
      expectedQuantity: String(c.expectedQuantity),
      quantityUnit: c.quantityUnit ?? '',
      quantityUnitCode: c.quantityUnitCode ?? null,
      notes: c.notes ?? '',
      unitType: c.unitType ?? '',
      unitTypeLabel: c.unitTypeLabel ?? '',
      totalWeightKg: c.totalWeightKg !== null ? String(c.totalWeightKg) : '',
      weightPerUnitKg: c.weightPerUnitKg !== null ? String(c.weightPerUnitKg) : '',
      totalWeightIsManual: inferTotalWeightIsManual(c.expectedQuantity, c.weightPerUnitKg, c.totalWeightKg),
      lengthMeters: c.lengthMeters !== null ? String(c.lengthMeters) : '',
      widthMeters: c.widthMeters !== null ? String(c.widthMeters) : '',
      heightMeters: c.heightMeters !== null ? String(c.heightMeters) : '',
      volumeM3: c.volumeM3 !== null ? String(c.volumeM3) : '',
      volumeIsManual: c.volumeIsManual,
      adrRequired: c.adrRequired,
      adrDetails: c.adrDetails ?? '',
      stackable: c.stackable,
      reference: c.reference ?? '',
      loadingStopIndex: loadingIndex >= 0 ? String(loadingIndex) : '',
      unloadingStopIndex: unloadingIndex >= 0 ? String(unloadingIndex) : '',
      palletCount: c.palletCount !== null && c.palletCount !== undefined ? String(c.palletCount) : '',
    }
  })
}

/**
 * Wave 1 fix A (A1a) — keeps every goods line pointing at the SAME stop when the stop list is
 * reordered, has a stop removed, or gets one inserted.
 *
 * A cargo row addresses its stops by position in the submitted stop list (`loadingStopIndex` /
 * `unloadingStopIndex`), and the backend resolves those positions against the request. Moving a
 * stop renumbered the list without touching the indexes, so a pure reorder silently moved every
 * goods line to a different stop: the server re-pinned the cargo while the colli generated from it
 * kept the pin they were born with, and the driver's delivery scan raised "hoort bij een andere
 * losstop" on every collo. Removing a stop was worse — the stale index went out of range and the
 * route drawer, which renders no goods fields at all, showed a 400 the user could not act on.
 *
 * Rows are matched to stops by ROW KEY, which is stable for the lifetime of an editor, so this
 * works for saved stops (id) and unsaved ones alike. A link whose stop is gone is cleared to ''
 * ("automatic"), which is what the unambiguous-order auto-link on the server expects; an already
 * automatic link stays automatic.
 *
 * Both editors that mutate stops — the full order form and the dossier route drawer — must route
 * their mutation through this helper; that is the whole point of it living here.
 */
export function remapCargoStopIndices(
  cargoItems: CargoFormRow[], previousStops: StopFormRow[], nextStops: StopFormRow[],
): CargoFormRow[] {
  const positionByKey = new Map(nextStops.map((stop, index) => [stop.key, index]))
  const remap = (index: string): string => {
    if (index === '') return ''
    const previous = previousStops[Number(index)]
    if (!previous) return ''
    const position = positionByKey.get(previous.key)
    return position === undefined ? '' : String(position)
  }
  return cargoItems.map((row) => {
    const loadingStopIndex = remap(row.loadingStopIndex)
    const unloadingStopIndex = remap(row.unloadingStopIndex)
    return loadingStopIndex === row.loadingStopIndex && unloadingStopIndex === row.unloadingStopIndex
      ? row
      : { ...row, loadingStopIndex, unloadingStopIndex }
  })
}

// --- Service selection initial state (spec 7) ---

export function serviceIdsFromOrder(order: TransportOrderDetail | undefined): string[] {
  return (order?.serviceLines ?? []).map((l) => l.serviceOptionId).filter((id): id is string => id !== null)
}

function serviceRecordFromOrder(
  order: TransportOrderDetail | undefined,
  value: (line: NonNullable<TransportOrderDetail['serviceLines']>[number]) => number | string | null | undefined,
): Record<string, string> {
  return Object.fromEntries(
    (order?.serviceLines ?? [])
      .filter((l) => l.serviceOptionId !== null && value(l) !== null && value(l) !== undefined && value(l) !== '')
      .map((l) => [l.serviceOptionId as string, String(value(l))]),
  )
}

export function serviceQuantitiesFromOrder(order: TransportOrderDetail | undefined): Record<string, string> {
  return serviceRecordFromOrder(order, (l) => l.quantity)
}

export function servicePalletsFromOrder(order: TransportOrderDetail | undefined): Record<string, string> {
  return serviceRecordFromOrder(order, (l) => l.palletCount)
}

export function serviceDaysFromOrder(order: TransportOrderDetail | undefined): Record<string, string> {
  return serviceRecordFromOrder(order, (l) => l.dayCount)
}

export function serviceNotesFromOrder(order: TransportOrderDetail | undefined): Record<string, string> {
  return serviceRecordFromOrder(order, (l) => l.note)
}

// --- Whole-form value snapshot: validation + submit-payload mapping consume this ---

export interface OrderFormValues {
  customerId: string
  customerReference: string
  orderDate: string
  goodsDescription: string
  quantity: string
  quantityUnit: string
  quantityUnitCode: string | null
  weightKg: string
  volumeM3: string
  palletCount: string
  /** Wave 3 §1: geplande afstand in km — voedt PerKm-tarieven en km-diensten (Maut). */
  distanceKm: string
  /** Wave 3 §1: laadmeters — voedt PerLdm-tarieven en ldm-staffelgrenzen. */
  loadingMeters: string
  adrRequired: boolean
  craneRequired: boolean
  /** P6: uitrusting/beweging-prijsdimensies (naast ADR/kraan). */
  plateauRequired: boolean
  moffettRequired: boolean
  isReturnMovement: boolean
  agreedPrice: string
  notes: string
  legalEntityId: string
  dieselSurchargeOverride: boolean
  dieselSurchargePercentOverride: string
  dieselSurchargeOverrideReason: string
  stops: StopFormRow[]
  cargoItems: CargoFormRow[]
  serviceOptions: ServiceOption[]
  selectedServiceOptionIds: string[]
  serviceQuantities: Record<string, string>
  servicePallets: Record<string, string>
  serviceDays: Record<string, string>
  serviceNotes: Record<string, string>
  priceIsManual: boolean
  priceOverrideReason: string
  pricingSource: 'Contract' | 'OneOff'
  oneOffFixedAmount: string
  oneOffTimeMode: 'none' | 'separate' | 'combined'
  oneOffIncludedLoadingMinutes: string
  oneOffIncludedUnloadingMinutes: string
  oneOffIncludedCombinedMinutes: string
  oneOffExtraHourlyRate: string
  oneOffNotes: string
  includedLoadingMinutesOverride: string
  includedUnloadingMinutesOverride: string
  extraTimeHourlyRateOverride: string
  extraTimeRoundingStepMinutes: string
  extraTimeMinimumBillableMinutes: string
  /** Concurrency token of the loaded order; echoed on update, absent on create. */
  version?: string
  /**
   * D2: crane job of the order. UNDEFINED = this editor does not own the crane fields — nothing is
   * submitted and the server keeps kind, description and lift data exactly as stored.
   */
  craneJob?: CraneJobFormValues
}

/** D2: kind of crane job + the on-site work it describes. The lift data is the load to LIFT, never goods. */
export interface CraneJobFormValues {
  kind: CraneJobKind
  workDescription: string
  liftLoadWeightKg: string
  liftLoadDimensions: string
  liftRadiusMeters: string
  liftHeightMeters: string
  liftConditions: string
  liftEquipment: string
}

export function emptyCraneJob(kind: CraneJobKind = 'None'): CraneJobFormValues {
  return {
    kind,
    workDescription: '',
    liftLoadWeightKg: '',
    liftLoadDimensions: '',
    liftRadiusMeters: '',
    liftHeightMeters: '',
    liftConditions: '',
    liftEquipment: '',
  }
}

/** Crane-job form values of a loaded order (older payloads without the fields read as None/empty). */
export function craneJobFromOrder(order: TransportOrderDetail | undefined): CraneJobFormValues {
  const text = (value: number | null | undefined) => (value == null ? '' : String(value))
  return {
    kind: order?.craneJobKind ?? 'None',
    workDescription: order?.workDescription ?? '',
    liftLoadWeightKg: text(order?.liftLoadWeightKg),
    liftLoadDimensions: order?.liftLoadDimensions ?? '',
    liftRadiusMeters: text(order?.liftRadiusMeters),
    liftHeightMeters: text(order?.liftHeightMeters),
    liftConditions: order?.liftConditions ?? '',
    liftEquipment: order?.liftEquipment ?? '',
  }
}

/** True when the planner entered anything about the on-site job (intake "touched" detection). */
export function isCraneJobTouched(job: CraneJobFormValues): boolean {
  return [
    job.workDescription, job.liftLoadWeightKg, job.liftLoadDimensions, job.liftRadiusMeters,
    job.liftHeightMeters, job.liftConditions, job.liftEquipment,
  ].some((value) => value.trim() !== '')
}

// --- Derived goods summary (wave 2026-08-04 §2) ---

export interface CargoSummary {
  units: Array<[string, number]>
  weight: number | null
  volume: number | null
  pallets: number | null
}

/**
 * Client mirror of the backend's `DeriveSummaryFromCargo`. Volume aggregates per-piece volume ×
 * expected quantity (audit bug fix: "per stuk" volume was previously summed without ×aantal).
 */
export function computeCargoSummary(
  cargoItems: CargoFormRow[],
  unitOptions: Array<{ code: string; name: string }>,
): CargoSummary | null {
  if (cargoItems.length === 0) return null
  const units = new Map<string, number>()
  let weight = 0
  let hasWeight = false
  let volume = 0
  let hasVolume = false
  let pallets = 0
  let hasPallets = false
  for (const c of cargoItems) {
    const qty = c.expectedQuantity === '' ? 0 : Number(c.expectedQuantity)
    const label =
      unitOptions.find((u) => u.code === c.quantityUnitCode)?.name ??
      c.quantityUnitCode ??
      (c.quantityUnit.trim() || 'stuks')
    units.set(label, (units.get(label) ?? 0) + qty)
    if (c.totalWeightKg.trim()) {
      weight += Number(c.totalWeightKg)
      hasWeight = true
    }
    const lineVolume = c.volumeIsManual
      ? numberOrNullFrom(c.volumeM3)
      : computeVolumeM3(
          numberOrNullFrom(c.lengthMeters),
          numberOrNullFrom(c.widthMeters),
          numberOrNullFrom(c.heightMeters),
        )
    if (lineVolume !== null) {
      // Audit fix (Wave 1 §12): the line volume is per stuk — the total multiplies by quantity,
      // mirroring the backend's DeriveSummaryFromCargo.
      volume += lineVolume * qty
      hasVolume = true
    }
    if (c.palletCount.trim()) {
      pallets += Number(c.palletCount)
      hasPallets = true
    }
  }
  return {
    units: [...units.entries()],
    weight: hasWeight ? weight : null,
    volume: hasVolume ? volume : null,
    pallets: hasPallets ? Math.ceil(pallets) : null,
  }
}

// --- Validation (Wave 1 §12): targeted errors instead of 13 blind sequential messages ---

export interface OrderFormValidationError {
  /** SectionedForm section id owning the failing field. */
  section: string
  /** Field path within the form (e.g. "stops[0].city"). */
  field: string
  /** User-facing Dutch label for the validation summary. */
  label: string
  message: string
}

const SECTION_ORDER = ['algemeen', 'route', 'goederen', 'services', 'documenten', 'prijs', 'samenvatting']

/**
 * Validates the whole form and returns EVERY failing field (section-ordered), so the
 * ValidationSummary can list them and route the user to the first failing section.
 * Runs outside React — labels/messages are translated via the module-level active locale.
 */
export function validateOrderForm(values: OrderFormValues): OrderFormValidationError[] {
  const locale = getActiveLocale()
  const tr = (key: string, params?: Record<string, string | number>) => translate(locale, key, params)
  const errors: OrderFormValidationError[] = []
  const add = (section: string, field: string, label: string, message: string) =>
    errors.push({ section, field, label, message })

  if (!values.customerId) {
    add('algemeen', 'customerId', tr('transportOrders.validation.customerLabel'), tr('transportOrders.validation.customerRequired'))
  }
  if (values.dieselSurchargeOverride && !values.dieselSurchargeOverrideReason.trim()) {
    add(
      'algemeen', 'dieselSurchargeOverrideReason', tr('transportOrders.validation.dieselReasonLabel'),
      tr('transportOrders.validation.dieselReasonMessage'),
    )
  }

  // D2: an on-site lifting job is ONE work-site stop + a description of the work — no goods and
  // no loading/unloading stops (mirror of the backend rules; every other order is unchanged).
  const onSite = values.craneJob?.kind === 'OnSiteLifting'
  if (onSite) {
    if (!values.stops.some((stop) => stop.stopType === 'Site')) {
      add('route', 'siteStop', tr('stopEditor.validation.siteStopLabel'), tr('stopEditor.validation.siteStopMessage'))
    }
    if (values.stops.some((stop) => stop.stopType !== 'Site')) {
      add('route', 'stops', tr('stopEditor.validation.siteOnlyLabel'), tr('stopEditor.validation.siteOnlyMessage'))
    }
    if (!values.craneJob?.workDescription.trim()) {
      add('route', 'workDescription', tr('stopEditor.validation.workDescriptionLabel'), tr('stopEditor.validation.workDescriptionMessage'))
    }
  }

  values.stops.forEach((stop, index) => {
    const number = index + 1
    const isSite = stop.stopType === 'Site'
    // A work site is often a street without a known place yet: a place OR an address line will do.
    if (!stop.locationId && !stop.city.trim() && !(isSite && stop.address.trim())) {
      add(
        'route', `stops[${index}].city`,
        isSite ? tr('stopEditor.validation.siteAddressLabel') : tr('transportOrders.validation.stopCityLabel', { number }),
        isSite ? tr('stopEditor.validation.siteAddressMessage') : tr('transportOrders.validation.stopCityMessage'),
      )
    }
    // D3: a postal code typed in THIS session must match its country's format; stored values
    // (never touched) are left alone so existing data can always be saved again.
    const postalKey = stop.postalCodeTouched ? postalCodeErrorKey(stop.countryCode, stop.postalCode) : null
    if (postalKey) {
      add('route', `stops[${index}].postalCode`, tr('stopEditor.validation.postalCodeLabel', { number }), tr(postalKey))
    }
    const windowPairs: Array<[string, string]> = [
      // A site job may end the next day, so its window is compared date + time.
      isSite && stop.fromTime && stop.toTime
        ? [`${stop.date}T${stop.fromTime}`, `${stop.toDate || stop.date}T${stop.toTime}`]
        : [stop.fromTime, stop.toTime],
      [stop.requestedFrom, stop.requestedTo],
      [stop.confirmedFrom, stop.confirmedTo],
    ]
    if (windowPairs.some(([from, to]) => from && to && to < from)) {
      add('route', `stops[${index}].window`, tr('transportOrders.validation.stopWindowLabel', { number }), tr('transportOrders.validation.stopWindowMessage'))
    }
    if (stop.earliestAllowed && stop.latestAllowed && stop.latestAllowed < stop.earliestAllowed) {
      add('route', `stops[${index}].latestAllowed`, tr('transportOrders.validation.stopLatestLabel', { number }), tr('transportOrders.validation.stopLatestMessage'))
    }
    // §15: the simple time requirement needs the time(s) its kind uses.
    if (stop.timeRequirement === 'Before' && !stop.timeReqTo) {
      add('route', `stops[${index}].timeReqTo`, tr('transportOrders.validation.stopTimeReqLabel', { number }), tr('transportOrders.validation.beforeMessage'))
    }
    if (stop.timeRequirement === 'After' && !stop.timeReqFrom) {
      add('route', `stops[${index}].timeReqFrom`, tr('transportOrders.validation.stopTimeReqLabel', { number }), tr('transportOrders.validation.afterMessage'))
    }
    if (stop.timeRequirement === 'Window') {
      if (!stop.timeReqFrom || !stop.timeReqTo) {
        add('route', `stops[${index}].timeReqFrom`, tr('transportOrders.validation.stopTimeReqLabel', { number }), tr('transportOrders.validation.windowRequiredMessage'))
      } else if (stop.timeReqTo <= stop.timeReqFrom) {
        add('route', `stops[${index}].timeReqTo`, tr('transportOrders.validation.stopTimeReqLabel', { number }), tr('transportOrders.validation.windowOrderMessage'))
      }
    }
  })

  values.cargoItems.forEach((cargo, index) => {
    if (cargo.expectedQuantity === '' || Number(cargo.expectedQuantity) <= 0) {
      add(
        'goederen', `cargoItems[${index}].expectedQuantity`, tr('transportOrders.validation.cargoQtyLabel', { number: index + 1 }),
        tr('transportOrders.validation.cargoQtyMessage'),
      )
    }
  })

  // Wave 2026-08-04 §3: quantity + unit, a goods line or a description — any one suffices.
  const hasHeaderQuantity =
    values.quantity !== '' && Number(values.quantity) > 0 && Boolean(values.quantityUnitCode || values.quantityUnit.trim())
  // D2: an on-site lifting job moves no goods — the minimum-goods rule does not apply to it.
  if (!onSite && !hasHeaderQuantity && values.cargoItems.length === 0 && !values.goodsDescription.trim()) {
    add(
      'goederen', 'goodsDescription', tr('transportOrders.validation.goodsLabel'),
      tr('transportOrders.validation.goodsMessage'),
    )
  }

  const barcodes = values.cargoItems.map((c) => c.barcode.trim().toLowerCase()).filter(Boolean)
  if (barcodes.length !== new Set(barcodes).size) {
    add('goederen', 'cargoBarcodes', tr('transportOrders.validation.barcodeLabel'), tr('transportOrders.validation.barcodeMessage'))
  }

  if (values.priceIsManual && !values.priceOverrideReason.trim()) {
    add('prijs', 'priceOverrideReason', tr('transportOrders.validation.manualPriceLabel'), tr('transportOrders.validation.manualPriceMessage'))
  }
  if (values.pricingSource === 'OneOff' && values.oneOffFixedAmount.trim() === '') {
    add('prijs', 'oneOffFixedAmount', tr('transportOrders.validation.oneOffLabel'), tr('transportOrders.validation.oneOffMessage'))
  }

  // Present errors in section order so "jump to the first failing section" matches the list.
  return errors.sort((a, b) => SECTION_ORDER.indexOf(a.section) - SECTION_ORDER.indexOf(b.section))
}

/** First message per field path, for inline `FormField` errors + `aria-invalid`. */
export function fieldErrorMap(errors: OrderFormValidationError[]): Record<string, string> {
  const map: Record<string, string> = {}
  for (const error of errors) {
    if (!(error.field in map)) map[error.field] = error.message
  }
  return map
}
