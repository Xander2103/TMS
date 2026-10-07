import { ApiError } from '../../api/apiClient'
import type { DossierDetail } from './types'

/**
 * The current dossier a 409 carries when the sent `version` is stale (someone else changed the
 * dossier in the meantime), or null for any other error. The ONE test for "version conflict",
 * shared by the detail page and the list's row actions.
 */
export function dossierVersionConflict(err: unknown): DossierDetail | null {
  if (err instanceof ApiError && err.status === 409 && err.body && typeof err.body === 'object' && 'dossierNumber' in err.body) {
    return err.body as DossierDetail
  }
  return null
}
