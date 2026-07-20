import { apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
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

export async function downloadProfitabilityExport(from: string, to: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/profitability/export?from=${from}&to=${to}`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    throw new Error('Export mislukt.')
  }
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `rendement-${from}-${to}.xlsx`
  anchor.click()
  URL.revokeObjectURL(url)
}
