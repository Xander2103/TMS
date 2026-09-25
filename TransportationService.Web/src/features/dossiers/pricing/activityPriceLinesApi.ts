import { apiClient } from '../../../api/apiClient'
import type { DossierDetail } from '../types'
import type { SaveActivityPriceLinesInput } from './types'

/**
 * D5: replaces the sales lines of a standalone billable activity (needs `dossiers.price`). The
 * server computes every line amount and the total and returns the whole dossier, so totals,
 * readiness and the activity re-render from the authoritative state. 409 on a stale `version`.
 */
export function saveActivityPriceLines(dossierId: string, activityId: string, input: SaveActivityPriceLinesInput): Promise<DossierDetail> {
  return apiClient.putJson<DossierDetail, SaveActivityPriceLinesInput>(
    `/api/dossiers/${dossierId}/activities/${activityId}/price-lines`, input)
}
