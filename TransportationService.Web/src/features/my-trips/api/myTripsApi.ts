import { apiClient } from '../../../api/apiClient'
import type { MyTrip, StopExecutionStatus, StopStatusHistoryEntry, TripExecution } from '../types'

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

export interface TransitionStopInput {
  toStatus: StopExecutionStatus
  reason?: string | null
  notes?: string | null
}

/** Generic controlled transition through the backend stop-status machine. */
export function transitionStop(tripId: string, stopId: string, input: TransitionStopInput): Promise<TripExecution> {
  return apiClient.postJson<TripExecution, TransitionStopInput>(
    `/api/trips/${tripId}/stops/${stopId}/transition`,
    input,
  )
}

export function getStopHistory(tripId: string, stopId: string): Promise<StopStatusHistoryEntry[]> {
  return apiClient.getJson<StopStatusHistoryEntry[]>(`/api/trips/${tripId}/stops/${stopId}/history`)
}

export function completeStop(
  tripId: string,
  stopId: string,
  podSignedBy: string | null,
  remarks: string | null,
  reason: string | null = null,
): Promise<TripExecution> {
  return apiClient.postJson<TripExecution, { podSignedBy: string | null; remarks: string | null; reason: string | null }>(
    `/api/trips/${tripId}/stops/${stopId}/complete`,
    { podSignedBy, remarks, reason },
  )
}

export function skipStop(tripId: string, stopId: string, remarks: string): Promise<TripExecution> {
  return apiClient.postJson<TripExecution, { remarks: string }>(`/api/trips/${tripId}/stops/${stopId}/skip`, {
    remarks,
  })
}
