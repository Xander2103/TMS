import type { SearchableSelectOption } from '../../components/ui/SearchableSelect'
import type { TranslateFn } from '../../i18n/localeContext'
import { getDriver } from '../drivers/api/driversApi'
import type { DriverListItem } from '../drivers/types'
import type { TrailerOption } from '../trailers/types'
import type { VehicleOption } from '../vehicles/types'
import { formatInteger } from '../../utils/numbers'
import { readinessHint } from '../fleet-readiness/readiness'
import type { ReadinessLevel } from '../fleet-readiness/types'

/**
 * Picker options for driver, vehicle and trailer — shared by the trip detail page and the dossier
 * activity planning block, so both search the same way. `label` is what lands in the field,
 * `keywords` everything the planner may type (SearchableSelect matches every word against label +
 * keywords). Only fields the list endpoints really return are used.
 */

/** Licence plates are typed with or without separators: "1-ABC-123" must match "1abc123". */
export function compactPlate(plate: string): string {
  return plate.replace(/[^0-9a-z]/gi, '')
}

export function joinParts(parts: Array<string | null | undefined>, separator: string): string {
  return parts.filter((part): part is string => Boolean(part)).join(separator)
}

/**
 * Why a driver is not plainly available, shown in the option BEFORE selection (HR acceptance
 * P2-7): blocked, absent on the requested day (approved absence), or not deployable (HR employment
 * status / profile unavailable). Qualification problems still surface in the conflict check.
 */
export function driverAvailabilityNote(driver: DriverListItem, t: TranslateFn): string | null {
  if (driver.isBlocked) return t('planning.driverPicker.blocked')
  if (driver.availabilityStatus === 'OnLeave') return t('planning.driverPicker.absent')
  if (driver.availabilityStatus === 'Unavailable') return t('planning.driverPicker.unavailable')
  if (driver.hasQualificationIssue) return t('planning.driverPicker.qualification')
  return null
}

/** Driver: first/last name (any order), driver number and personnel number. */
export function buildDriverOptions(drivers: DriverListItem[], t: TranslateFn): SearchableSelectOption[] {
  return drivers.map((driver) => ({
    value: driver.id,
    label: `${driver.fullName} (${driver.driverNumber})`,
    subtitle: joinParts(
      [
        driverAvailabilityNote(driver, t),
        driver.employeeNumber ? t('planning.detail.employeeNumber', { number: driver.employeeNumber }) : null,
        driver.categoryName,
      ],
      ' · ',
    ),
    keywords: joinParts([driver.employeeNumber, driver.categoryName], ' '),
  }))
}

/**
 * M1: why a vehicle/trailer is not plainly available — in maintenance or out of service (the
 * conflict check blocks those), or "in gebruik" (a warning). Available shows nothing.
 */
export function equipmentStatusNote(status: string | null | undefined, t: TranslateFn): string | null {
  switch (status) {
    case 'InMaintenance':
      return t('planning.vehiclePicker.inMaintenance')
    case 'OutOfService':
      return t('planning.vehiclePicker.outOfService')
    case 'InUse':
      return t('planning.vehiclePicker.inUse')
    default:
      return null
  }
}

/**
 * FL-H1/H2/H4: the server's fleet readiness for the trip day wins — "⛔ Onderhoud gepland …",
 * "⚠ Tachograafijking vervallen". Without readiness (older payload) the operational-status note
 * of M1 stays; "in gebruik" is kept next to a deployable verdict because it is no blocker.
 */
export function equipmentNote(
  option: { operationalStatus?: string | null; readiness?: ReadinessLevel | null; readinessSummary?: string | null },
  t: TranslateFn,
): string | null {
  if (option.readiness) {
    return readinessHint(option) ?? (option.operationalStatus === 'InUse' ? t('planning.vehiclePicker.inUse') : null)
  }
  return equipmentStatusNote(option.operationalStatus, t)
}

/** Vehicle: internal number, licence plate (with or without separators), brand/model, plus the
 * status, payload and equipment the planner needs to judge suitability before choosing (M1). */
export function buildVehicleOptions(vehicles: VehicleOption[], t?: TranslateFn): SearchableSelectOption[] {
  return vehicles.map((vehicle) => ({
    value: vehicle.id,
    label: `${vehicle.internalNumber} (${vehicle.licensePlate})`,
    subtitle: joinParts(
      [
        t ? equipmentNote(vehicle, t) : readinessHint(vehicle),
        joinParts([vehicle.brand, vehicle.model], ' '),
        t && vehicle.payloadKg ? t('planning.vehiclePicker.payload', { kg: formatInteger(vehicle.payloadKg) }) : null,
        t && vehicle.hasCrane ? t('planning.vehiclePicker.crane') : null,
        t && vehicle.adrSuitable ? t('planning.vehiclePicker.adr') : null,
      ],
      ' · ',
    ),
    keywords: joinParts(
      [compactPlate(vehicle.licensePlate), vehicle.brand, vehicle.model, vehicle.hasCrane && t ? t('planning.vehiclePicker.crane') : null],
      ' ',
    ),
  }))
}

/** Trailer: internal number and licence plate (with or without separators), plus status/ADR (M1). */
export function buildTrailerOptions(trailers: TrailerOption[], t?: TranslateFn): SearchableSelectOption[] {
  return trailers.map((trailer) => ({
    value: trailer.id,
    label: `${trailer.internalNumber} (${trailer.licensePlate})`,
    subtitle: joinParts(
      [
        t ? equipmentNote(trailer, t) : readinessHint(trailer),
        joinParts([trailer.brand, trailer.model], ' '),
        t && trailer.adrSuitable ? t('planning.vehiclePicker.adr') : null,
      ],
      ' · ',
    ),
    keywords: joinParts([compactPlate(trailer.licensePlate), trailer.brand, trailer.model], ' '),
  }))
}

/**
 * The fixed vehicle of a driver. `GET /api/drivers` carries `fixedVehicleId` — then no extra call
 * is made; an older payload without the field falls back to the driver detail. Only a vehicle the
 * planner could also pick from the list is proposed; a failed lookup simply means no suggestion.
 */
export async function lookupFixedVehicleId(
  drivers: DriverListItem[],
  vehicles: VehicleOption[],
  driverId: string,
): Promise<string | null> {
  const listed = drivers.find((driver) => driver.id === driverId)
  let fixedId: string | null
  if (listed && listed.fixedVehicleId !== undefined) {
    fixedId = listed.fixedVehicleId
  } else {
    try {
      fixedId = (await getDriver(driverId)).fixedVehicle?.id ?? null
    } catch {
      return null
    }
  }
  return fixedId !== null && vehicles.some((vehicle) => vehicle.id === fixedId) ? fixedId : null
}
