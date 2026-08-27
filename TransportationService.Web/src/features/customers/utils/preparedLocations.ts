import { createLocation } from '../../locations/api/locationsApi'
import { EMPTY_LOCATION_INPUT, type LocationInput, type LocationType } from '../../locations/types'
import { ApiError } from '../../../api/apiClient'

/**
 * Locations a user prepared while CREATING a customer, held in client-side form state until
 * the customer exists (same pattern as the employees' prepared follow-ups). Nothing is
 * persisted for a not-yet-created customer; after creation succeeds each staged row is
 * POSTed to /api/locations with the new customerId. On partial failure the customer and the
 * remaining staged rows are never lost — the failed rows stay retryable.
 */
export interface PreparedLocation {
  /** Stable client-side identity for React keys and retry bookkeeping. */
  key: string
  name: string
  type: LocationType
  street: string
  houseNumber: string
  postalCode: string
  city: string
  countryCode: string | null
  contactPhone: string
}

export interface LocationFollowUpResult {
  key: string
  label: string
  ok: boolean
  error?: string
}

function newKey(): string {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `row-${Math.random().toString(36).slice(2)}`
}

export function createPreparedLocation(): PreparedLocation {
  return {
    key: newKey(),
    name: '',
    type: 'CustomerLocation',
    street: '',
    houseNumber: '',
    postalCode: '',
    city: '',
    countryCode: 'BE',
    contactPhone: '',
  }
}

function nullable(value: string): string | null {
  const trimmed = value.trim()
  return trimmed ? trimmed : null
}

/** Full POST /api/locations payload for one staged row (code blank = generated server-side). */
export function preparedLocationToInput(row: PreparedLocation, customerId: string): LocationInput {
  return {
    ...EMPTY_LOCATION_INPUT,
    code: '',
    name: row.name.trim(),
    type: row.type,
    street: nullable(row.street),
    houseNumber: nullable(row.houseNumber),
    postalCode: nullable(row.postalCode),
    city: nullable(row.city),
    countryCode: row.countryCode,
    contactPhone: nullable(row.contactPhone),
    isActive: true,
    customerId,
  }
}

function describe(err: unknown, fallback: string): string {
  if (err instanceof ApiError) return err.message
  if (err instanceof Error && err.message) return err.message
  return fallback
}

/**
 * Creates each staged location for a freshly-created customer, sequentially; never throws —
 * returns a per-row result so the caller can offer retry for the `!ok` rows.
 *
 * i18n: dit is geen React-code, dus de aanroeper geeft de vertaalde fallbackteksten mee
 * (customers.staged.fallbackLabel / customers.staged.createFailed); de Nederlandse defaults
 * blijven het pre-i18n-gedrag voor aanroepers zonder locale-context.
 */
export async function createPreparedLocations(
  customerId: string,
  rows: PreparedLocation[],
  fallbacks?: { label?: string; error?: string },
): Promise<LocationFollowUpResult[]> {
  const fallbackLabel = fallbacks?.label ?? 'Locatie'
  const fallbackError = fallbacks?.error ?? 'De locatie kon niet worden aangemaakt.'
  const results: LocationFollowUpResult[] = []
  for (const row of rows) {
    try {
      await createLocation(preparedLocationToInput(row, customerId))
      results.push({ key: row.key, label: row.name || fallbackLabel, ok: true })
    } catch (err) {
      results.push({
        key: row.key,
        label: row.name || fallbackLabel,
        ok: false,
        error: describe(err, fallbackError),
      })
    }
  }
  return results
}
