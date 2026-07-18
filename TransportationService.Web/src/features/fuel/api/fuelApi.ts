import { apiClient } from '../../../api/apiClient'
import type { FuelOverview, FuelTransaction, FuelTransactionInput, FuelWarning } from '../types'

export function getFuelOverview(vehicleId: string): Promise<FuelOverview> {
  return apiClient.getJson<FuelOverview>(`/api/vehicles/${vehicleId}/fuel-transactions`)
}

export function createFuelTransaction(vehicleId: string, input: FuelTransactionInput): Promise<FuelTransaction> {
  return apiClient.postJson<FuelTransaction, FuelTransactionInput>(`/api/vehicles/${vehicleId}/fuel-transactions`, input)
}

export function updateFuelTransaction(id: string, input: FuelTransactionInput): Promise<FuelTransaction> {
  return apiClient.putJson<FuelTransaction, FuelTransactionInput>(`/api/fuel-transactions/${id}`, input)
}

export function deleteFuelTransaction(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/fuel-transactions/${id}`)
}

export function getRecentFuelWarnings(take = 10): Promise<FuelWarning[]> {
  return apiClient.getJson<FuelWarning[]>(`/api/fuel-transactions/warnings?take=${take}`)
}
