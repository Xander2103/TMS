import { ApiError } from './apiClient'

/**
 * True when a load failed because the record does not exist (HTTP 404). Detail pages then show
 * "… niet gevonden" with a way back to the list instead of "kon niet worden geladen · Opnieuw
 * proberen" — retrying a record that is not there never helps.
 */
export function isNotFoundError(err: unknown): boolean {
  return err instanceof ApiError && err.status === 404
}
