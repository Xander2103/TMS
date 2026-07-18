import { apiClient } from '../../../api/apiClient'
import type { MyTrip, TripExecution } from '../types'

export function listMyTrips(from?: string, to?: string): Promise<MyTrip[]> {
  const query = new URLSearchParams()
  if (from) query.set('from', from)
  if (to) query.set('to', to)
  const suffix = query.toString()
  return apiClient.getJson<MyTrip[]>(`/api/my/trips${suffix ? `?${suffix}` : ''}`)
}

export function getTripExecution(tripId: string): Promise<TripExecution> {
  return apiClient.getJson<TripExecution>(`/api/trips/${tripId}/execution`)
}

export function arriveAtStop(tripId: string, stopId: string): Promise<TripExecution> {
  return apiClient.postJson<TripExecution, Record<string, never>>(`/api/trips/${tripId}/stops/${stopId}/arrive`, {})
}

export function completeStop(
  tripId: string,
  stopId: string,
  podSignedBy: string | null,
  remarks: string | null,
): Promise<TripExecution> {
  return apiClient.postJson<TripExecution, { podSignedBy: string | null; remarks: string | null }>(
    `/api/trips/${tripId}/stops/${stopId}/complete`,
    { podSignedBy, remarks },
  )
}

export function skipStop(tripId: string, stopId: string, remarks: string): Promise<TripExecution> {
  return apiClient.postJson<TripExecution, { remarks: string }>(`/api/trips/${tripId}/stops/${stopId}/skip`, {
    remarks,
  })
}
