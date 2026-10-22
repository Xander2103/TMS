import { apiClient, LONG_RUNNING_TIMEOUT_MS } from '../../../api/apiClient'
import type { ProfitabilityDimension, ProfitabilityGroup, ProfitabilityOverview, TripExplanation } from '../types'

export function getProfitabilityOverview(from: string, to: string): Promise<ProfitabilityOverview> {
  return apiClient.getJson<ProfitabilityOverview>(`/api/profitability/trips?from=${from}&to=${to}`)
}

export function getProfitabilityGrouped(
  dimension: ProfitabilityDimension, from: string, to: string,
): Promise<ProfitabilityGroup[]> {
  return apiClient.getJson<ProfitabilityGroup[]>(
    `/api/profitability/grouped?dimension=${dimension}&from=${from}&to=${to}`)
}

export function getTripExplanation(tripId: string): Promise<TripExplanation> {
  return apiClient.getJson<TripExplanation>(`/api/profitability/trips/${tripId}/explanation`)
}

export function downloadProfitabilityExport(from: string, to: string): Promise<void> {
  // PERF-H7: through apiClient (bearer + refresh) with the long-running deadline; the calling
  // component shows its own translated fallback on failure.
  return apiClient.downloadFile(`/api/profitability/export?from=${from}&to=${to}`, `rendement-${from}-${to}.xlsx`, {
    timeoutMs: LONG_RUNNING_TIMEOUT_MS,
  })
}
