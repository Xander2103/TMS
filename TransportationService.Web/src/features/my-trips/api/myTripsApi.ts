import { apiClient } from '../../../api/apiClient'
import { OfflineQueuedError, queueStopAction } from '../../driver/offlineActions'
import type { MyTrip, StopExecutionStatus, StopStatusHistoryEntry, TripExecution } from '../types'

/** Network failure (no HTTP status) vs a server verdict; server verdicts always propagate. */
function isNetworkError(error: unknown): boolean {
  return typeof (error as { status?: number }).status !== 'number'
}

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
  /** Offline-replay idempotency key; generated automatically when omitted. */
  clientRequestId?: string
}

/**
 * Generic controlled transition through the backend stop-status machine. Offline-aware:
 * a network failure queues the action (idempotent replay via clientRequestId) and throws
 * OfflineQueuedError so the caller can tell the user; server verdicts propagate unchanged.
 */
export async function transitionStop(tripId: string, stopId: string, input: TransitionStopInput): Promise<TripExecution> {
  const body = { ...input, clientRequestId: input.clientRequestId ?? crypto.randomUUID() }
  try {
    return await apiClient.postJson<TripExecution, TransitionStopInput>(
      `/api/trips/${tripId}/stops/${stopId}/transition`,
      body,
    )
  } catch (error) {
    if (isNetworkError(error)) {
      queueStopAction('stopTransition', `Stopstatus → ${input.toStatus}`, body.clientRequestId,
        { tripId, stopId, input: body })
      throw new OfflineQueuedError()
    }
    throw error
  }
}

export function getStopHistory(tripId: string, stopId: string): Promise<StopStatusHistoryEntry[]> {
  return apiClient.getJson<StopStatusHistoryEntry[]>(`/api/trips/${tripId}/stops/${stopId}/history`)
}

export async function completeStop(
  tripId: string,
  stopId: string,
  podSignedBy: string | null,
  remarks: string | null,
  reason: string | null = null,
  packageOverrideReason: string | null = null,
): Promise<TripExecution> {
  const body = { podSignedBy, remarks, reason, packageOverrideReason, clientRequestId: crypto.randomUUID() }
  try {
    return await apiClient.postJson<TripExecution, typeof body>(
      `/api/trips/${tripId}/stops/${stopId}/complete`,
      body,
    )
  } catch (error) {
    if (isNetworkError(error)) {
      queueStopAction('completeStop', 'Stop afronden', body.clientRequestId, { tripId, stopId, body })
      throw new OfflineQueuedError()
    }
    throw error
  }
}

export async function skipStop(tripId: string, stopId: string, remarks: string): Promise<TripExecution> {
  const body = { remarks, clientRequestId: crypto.randomUUID() }
  try {
    return await apiClient.postJson<TripExecution, typeof body>(`/api/trips/${tripId}/stops/${stopId}/skip`, body)
  } catch (error) {
    if (isNetworkError(error)) {
      queueStopAction('skipStop', 'Stop overslaan', body.clientRequestId, { tripId, stopId, body })
      throw new OfflineQueuedError()
    }
    throw error
  }
}
