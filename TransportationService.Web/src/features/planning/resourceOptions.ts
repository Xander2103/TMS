import type { SearchableSelectOption } from '../../components/ui/SearchableSelect'
import type { TranslateFn } from '../../i18n/localeContext'
import { getDriver } from '../drivers/api/driversApi'
import type { DriverListItem } from '../drivers/types'
import type { TrailerOption } from '../trailers/types'
import type { VehicleOption } from '../vehicles/types'

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

/** Driver: first/last name (any order), driver number and personnel number. */
export function buildDriverOptions(drivers: DriverListItem[], t: TranslateFn): SearchableSelectOption[] {
  return drivers.map((driver) => ({
    value: driver.id,
    label: `${driver.fullName} (${driver.driverNumber})`,
    subtitle: joinParts(
      [
        driver.employeeNumber ? t('planning.detail.employeeNumber', { number: driver.employeeNumber }) : null,
        driver.categoryName,
      ],
      ' · ',
    ),
    keywords: joinParts([driver.employeeNumber, driver.categoryName], ' '),
  }))
}

/** Vehicle: internal number, licence plate (with or without separators) and brand/model. */
export function buildVehicleOptions(vehicles: VehicleOption[]): SearchableSelectOption[] {
  return vehicles.map((vehicle) => ({
    value: vehicle.id,
    label: `${vehicle.internalNumber} (${vehicle.licensePlate})`,
    subtitle: joinParts([vehicle.brand, vehicle.model], ' '),
    keywords: joinParts([compactPlate(vehicle.licensePlate), vehicle.brand, vehicle.model], ' '),
  }))
}

/** Trailer: internal number and licence plate (with or without separators). */
export function buildTrailerOptions(trailers: TrailerOption[]): SearchableSelectOption[] {
  return trailers.map((trailer) => ({
    value: trailer.id,
    label: `${trailer.internalNumber} (${trailer.licensePlate})`,
    subtitle: joinParts([trailer.brand, trailer.model], ' '),
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
