import type { LocationInput } from '../types'

/**
 * Single source of truth for the location form's section navigation: ordered sections,
 * which validated field keys each section owns (error badge + first-error routing), and the
 * semantic per-section status (✓ compleet / ● bevat gegevens / ○ leeg) shown in the rail.
 */
export interface LocationSectionMeta {
  id: string
  /** Vertaalsleutel (locations.sections.*) — de form vertaalt bij het bouwen van de rail. */
  label: string
  optional?: boolean
}

export const LOCATION_SECTIONS: LocationSectionMeta[] = [
  { id: 'algemeen', label: 'locations.sections.algemeen' },
  { id: 'adres', label: 'locations.sections.adres' },
  { id: 'contact', label: 'locations.sections.contact', optional: true },
  { id: 'openingstijden', label: 'locations.sections.openingstijden', optional: true },
  { id: 'operationeel', label: 'locations.sections.operationeel', optional: true },
  { id: 'instructies', label: 'locations.sections.instructies', optional: true },
  { id: 'planning', label: 'locations.sections.planning', optional: true },
]

/** Which validated field keys belong to which section (badge + first-error routing). */
export const LOCATION_SECTION_FIELD_KEYS: Record<string, string[]> = {
  algemeen: ['name', 'code', 'type', 'externalReference', 'customerId'],
  adres: ['street', 'houseNumber', 'postalCode', 'city', 'countryCode', 'latitude', 'longitude'],
  contact: ['customerContactId', 'contactName', 'contactPhone', 'contactMobile', 'contactEmail'],
  openingstijden: ['openingIntervals', 'openingHours'],
  operationeel: [
    'gate',
    'accessCode',
    'receptionPoint',
    'dock',
    'routeDescription',
    'appointmentRequired',
    'deliveryByAppointmentOnly',
    'alfapassRequired',
    'accessRestrictions',
    'heightRestrictionMeters',
    'weightRestrictionTons',
    'adrAllowed',
    'vehicleRestrictions',
    'trailerRestrictions',
    'craneRequired',
    'forkliftAvailable',
  ],
  instructies: [
    'loadingInstructions',
    'unloadingInstructions',
    'accessInstructions',
    'driverInstructions',
    'internalMemo',
    'notes',
  ],
  planning: [
    'defaultLoadingMinutes',
    'defaultUnloadingMinutes',
    'preferredArrivalFrom',
    'preferredArrivalTo',
    'earliestArrival',
    'latestArrival',
  ],
}

/** Focus target per validated field key — first invalid input receives focus after a failed submit. */
export const LOCATION_FIELD_IDS: Record<string, string> = {
  name: 'loc-name',
  code: 'loc-code',
  contactEmail: 'loc-contact-email',
  latitude: 'loc-lat',
  longitude: 'loc-lng',
  openingIntervals: 'loc-hours-editor',
}

export interface LocationSectionStatus {
  /** Required data of the section is present and valid — shown as ✓. */
  complete?: boolean
  /** Optional data present (●) or explicitly empty (○). */
  filled?: boolean
}

const has = (value: string | null | undefined) => Boolean(value && value.trim())

/**
 * Semantic section status derived from the current form value. `complete` is only claimed
 * where a section has a sensible notion of "af" (Algemeen: naam + type; Adres: minstens
 * plaats); the optional sections report `filled` so the rail shows ●/○ instead.
 */
export function computeLocationSectionStatus(
  form: LocationInput,
  mode: 'create' | 'edit',
): Record<string, LocationSectionStatus> {
  const contactFilled = has(form.contactName) || has(form.contactPhone) || has(form.contactMobile) || has(form.contactEmail)
  const openingFilled = form.openingIntervals.length > 0 || has(form.openingHours)
  const operationeelFilled =
    has(form.gate) ||
    has(form.accessCode ?? null) ||
    has(form.receptionPoint) ||
    has(form.dock) ||
    has(form.routeDescription) ||
    form.appointmentRequired ||
    form.deliveryByAppointmentOnly ||
    form.alfapassRequired ||
    has(form.accessRestrictions) ||
    form.heightRestrictionMeters != null ||
    form.weightRestrictionTons != null ||
    form.adrAllowed !== null ||
    has(form.vehicleRestrictions) ||
    has(form.trailerRestrictions) ||
    form.craneRequired ||
    form.forkliftAvailable
  const instructiesFilled =
    has(form.loadingInstructions) ||
    has(form.unloadingInstructions) ||
    has(form.accessInstructions) ||
    has(form.driverInstructions) ||
    has(form.internalMemo) ||
    has(form.notes)
  const planningFilled =
    form.defaultLoadingMinutes != null ||
    form.defaultUnloadingMinutes != null ||
    has(form.preferredArrivalFrom) ||
    has(form.preferredArrivalTo) ||
    has(form.earliestArrival) ||
    has(form.latestArrival)

  return {
    algemeen: { complete: has(form.name) && (mode === 'create' || has(form.code)) },
    adres: {
      complete: has(form.city),
      filled:
        has(form.street) ||
        has(form.houseNumber) ||
        has(form.postalCode) ||
        has(form.countryCode) ||
        form.latitude != null ||
        form.longitude != null,
    },
    contact: { filled: contactFilled },
    openingstijden: { filled: openingFilled },
    operationeel: { filled: operationeelFilled },
    instructies: { filled: instructiesFilled },
    planning: { filled: planningFilled },
  }
}
