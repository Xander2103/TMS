import { apiClient } from '../../../api/apiClient'
import type { CostRateSet, CostRateSetInput, TripCostPhase, TripCostType, TripCosting } from '../types'

export function getTripCosting(tripId: string): Promise<TripCosting> {
  return apiClient.getJson<TripCosting>(`/api/trips/${tripId}/costing`)
}

export function recalculateEstimate(tripId: string): Promise<TripCosting> {
  return apiClient.postJson<TripCosting, object>(`/api/trips/${tripId}/costing/recalculate-estimate`, {})
}

export function recalculateActual(tripId: string): Promise<TripCosting> {
  return apiClient.postJson<TripCosting, object>(`/api/trips/${tripId}/costing/recalculate-actual`, {})
}

export interface AddCostLineInput {
  phase: TripCostPhase
  costType: TripCostType
  description: string
  quantity: number
  unit: string
  unitRate: number
}

export function addCostLine(tripId: string, input: AddCostLineInput): Promise<TripCosting> {
  return apiClient.postJson<TripCosting, AddCostLineInput>(`/api/trips/${tripId}/costing/lines`, input)
}

export function overrideCostLine(tripId: string, lineId: string, amount: number, reason: string): Promise<TripCosting> {
  return apiClient.putJson<TripCosting, { amount: number; reason: string }>(
    `/api/trips/${tripId}/costing/lines/${lineId}/override`,
    { amount, reason },
  )
}

export function deleteCostLine(tripId: string, lineId: string): Promise<TripCosting> {
  return apiClient.deleteJson<TripCosting>(`/api/trips/${tripId}/costing/lines/${lineId}`)
}

export function updateTripActuals(
  tripId: string,
  actualDistanceKm: number | null,
  actualEmptyKm: number | null,
): Promise<TripCosting> {
  return apiClient.putJson<TripCosting, { actualDistanceKm: number | null; actualEmptyKm: number | null }>(
    `/api/trips/${tripId}/costing/actuals`,
    { actualDistanceKm, actualEmptyKm },
  )
}

export function finalizeTripCosting(tripId: string): Promise<TripCosting> {
  return apiClient.postJson<TripCosting, object>(`/api/trips/${tripId}/costing/finalize`, {})
}

export function reopenTripCosting(tripId: string): Promise<TripCosting> {
  return apiClient.postJson<TripCosting, object>(`/api/trips/${tripId}/costing/reopen`, {})
}

export function listCostRateSets(): Promise<CostRateSet[]> {
  return apiClient.getJson<CostRateSet[]>('/api/cost-rates')
}

export function createCostRateSet(input: CostRateSetInput): Promise<CostRateSet> {
  return apiClient.postJson<CostRateSet, CostRateSetInput>('/api/cost-rates', input)
}

export function updateCostRateSet(id: string, input: CostRateSetInput): Promise<CostRateSet> {
  return apiClient.putJson<CostRateSet, CostRateSetInput>(`/api/cost-rates/${id}`, input)
}

export function deleteCostRateSet(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/cost-rates/${id}`)
}
