import { getActiveLocale } from '../../../../i18n/activeLocale'
import { translate } from '../../../../i18n/translations'
import { fromWireDateTime, toDateTimeLocalInput } from '../../../../utils/dates'
import { computeVolumeM3 } from '../../../../utils/volume'
import type { ServiceOption } from '../../../tarification/api/pricingApi'
import type { PackageUnitType } from '../../../packages/types'
import type { StopInput, TransportOrderDetail } from '../../types'

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
  locationName: string
  address: string
  postalCode: string
  city: string
  countryCode: string
  /** §14: one date + optional from/to time replace the raw planned datetime pair. */
  date: string
  fromTime: string
  toTime: string
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
  if (value.trim() === '') return null
  const parsed = Number(value.replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : null
}

let rowKeyCounter = 0
export function nextRowKey(): string {
  rowKeyCounter += 1
  return `stop-${rowKeyCounter}`
}

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
    countryCode: 'BE',
    date: '',
    fromTime: '',
    toTime: '',
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
): Pick<StopFormRow, 'date' | 'fromTime' | 'toTime'> {
  const from = fromWireDateTime(plannedFrom)
  const to = fromWireDateTime(plannedTo)
  return {
    date: from?.date ?? to?.date ?? '',
    fromTime: from && !(from.time === '00:00' && !to) ? from.time : '',
    toTime: to?.time ?? '',
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
    locationName: s.locationId ? '' : s.locationName,
    address: s.address ?? '',
    postalCode: s.postalCode ?? '',
    city: s.city ?? '',
    countryCode: s.countryCode ?? 'BE',
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

  values.stops.forEach((stop, index) => {
    const number = index + 1
    if (!stop.locationId && !stop.city.trim()) {
      add('route', `stops[${index}].city`, tr('transportOrders.validation.stopCityLabel', { number }), tr('transportOrders.validation.stopCityMessage'))
    }
    const windowPairs: Array<[string, string]> = [
      [stop.fromTime, stop.toTime],
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
  if (!hasHeaderQuantity && values.cargoItems.length === 0 && !values.goodsDescription.trim()) {
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
