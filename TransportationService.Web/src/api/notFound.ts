import { ApiError } from './apiClient'

/**
 * True when a load failed because the record does not exist (HTTP 404). Detail pages then show
 * "… niet gevonden" with a way back to the list instead of "kon niet worden geladen · Opnieuw
 * proberen" — retrying a record that is not there never helps.
 */
export function isNotFoundError(err: unknown): boolean {
  return err instanceof ApiError && err.status === 404
}

/**
 * SEC-M5: true when the server refused the load for lack of rights (HTTP 403). The screen then
 * says "Geen toegang" instead of the raw server text plus "Opnieuw proberen" — a permission
 * refusal never changes by retrying.
 */
export function isForbiddenError(err: unknown): boolean {
  return err instanceof ApiError && err.status === 403
}

/**
 * Whether "Opnieuw proberen" can help: network failures (no status), server errors (5xx),
 * timeouts (408) and rate limiting (429). Any other 4xx is a refusal or a bad request that
 * answers the same on every retry.
 */
export function isRetryableError(err: unknown): boolean {
  if (!(err instanceof ApiError) || err.status === undefined) return true
  return err.status >= 500 || err.status === 408 || err.status === 429
}
