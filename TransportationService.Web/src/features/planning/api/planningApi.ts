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

export function createTrip(input: TripInput): Promise<TripDetail> {
  return apiClient.postJson<TripDetail, TripInput>('/api/trips', input)
}

export function updateTrip(id: string, input: TripInput): Promise<TripDetail> {
  return apiClient.putJson<TripDetail, TripInput>(`/api/trips/${id}`, input)
}

/** 409 carries { message, conflicts } when blocking conflicts stop the transition. */
export function changeTripStatus(id: string, status: TripStatus, override: boolean): Promise<TripDetail> {
  return apiClient.postJson<TripDetail, { status: TripStatus; override: boolean }>(`/api/trips/${id}/status`, {
    status,
    override,
  })
}

export function deleteTrip(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/trips/${id}`)
}

export type { PlanningConflict }
