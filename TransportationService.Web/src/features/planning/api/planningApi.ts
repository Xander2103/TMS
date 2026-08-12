import { apiClient } from '../../../api/apiClient'
import type { PlanningConflict, TripDetail, TripInput, TripListItem, TripStatus } from '../types'

export interface ListTripsParams {
  from?: string
  to?: string
  status?: TripStatus
  driverId?: string
}

export function listTrips(params: ListTripsParams = {}): Promise<TripListItem[]> {
  const query = new URLSearchParams()
  if (params.from) query.set('from', params.from)
  if (params.to) query.set('to', params.to)
  if (params.status) query.set('status', params.status)
  if (params.driverId) query.set('driverId', params.driverId)
  const suffix = query.toString()
  return apiClient.getJson<TripListItem[]>(`/api/trips${suffix ? `?${suffix}` : ''}`)
}

export function getTrip(id: string): Promise<TripDetail> {
  return apiClient.getJson<TripDetail>(`/api/trips/${id}`)
}

// --- Wave 7: transparante ritvoorstellen ---

export interface ProposalOrder {
  transportOrderId: string
  orderNumber: string
  customerName: string
  orderDate: string
  deliveryCity: string | null
  deliveryPostalCode: string | null
  weightKg: number | null
  loadingMeters: number | null
  palletCount: number | null
  overdue: boolean
}

export interface TourProposal {
  zoneCode: string
  zoneName: string
  orders: ProposalOrder[]
  totalWeightKg: number
  totalLoadingMeters: number
  totalPallets: number
  explanations: string[]
}

export interface PlanningProposals {
  date: string
  proposals: TourProposal[]
  excluded: string[]
}

export function getPlanningProposals(date: string): Promise<PlanningProposals> {
  return apiClient.getJson(`/api/planning/proposals?date=${date}`)
}

export function createTrip(input: TripInput): Promise<TripDetail> {
  return apiClient.postJson<TripDetail, TripInput>('/api/trips', input)
}

export function updateTrip(id: string, input: TripInput): Promise<TripDetail> {
  return apiClient.putJson<TripDetail, TripInput>(`/api/trips/${id}`, input)
}

/**
 * 409 carries { message, conflicts } when blocking conflicts stop the transition, or
 * { message, packageReadiness } when mandatory packages are not loaded at departure.
 */
export function changeTripStatus(
  id: string,
  status: TripStatus,
  override: boolean,
  releaseOverride = false,
  overrideReason: string | null = null,
): Promise<TripDetail> {
  return apiClient.postJson<
    TripDetail,
    { status: TripStatus; override: boolean; releaseOverride: boolean; overrideReason: string | null }
  >(`/api/trips/${id}/status`, { status, override, releaseOverride, overrideReason })
}

export function deleteTrip(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/trips/${id}`)
}

export type { PlanningConflict }
