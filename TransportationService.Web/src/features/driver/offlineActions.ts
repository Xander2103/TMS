import { apiClient } from '../../api/apiClient'
import { actionQueue, type QueuedAction, type QueuedActionKind } from './actionQueue'

/**
 * Thrown after an action was safely queued because the device is offline. The message is
 * user-facing: existing error handlers surface it without knowing about the queue.
 * `translationKey` lets locale-aware consumers render the message via t(); the Dutch
 * message stays as fallback for render sites that only know Error.message.
 */
export class OfflineQueuedError extends Error {
  readonly translationKey = 'driverApp.offline.queued'

  constructor() {
    super('Geen verbinding — de actie staat in de wachtrij en wordt automatisch gesynchroniseerd.')
    this.name = 'OfflineQueuedError'
  }
}

export function queueStopAction(
  kind: QueuedActionKind,
  label: string,
  clientRequestId: string,
  payload: Record<string, unknown>,
): void {
  actionQueue.enqueue({ kind, label, clientRequestId, payload })
}

/**
 * Replays one queued action against its endpoint. The backend treats a repeated
 * clientRequestId as a no-op replay, so double delivery is harmless by design.
 */
export function executeQueuedAction(action: QueuedAction): Promise<unknown> {
  switch (action.kind) {
    case 'stopTransition': {
      const { tripId, stopId, input } = action.payload as { tripId: string; stopId: string; input: unknown }
      return apiClient.postJson(`/api/trips/${tripId}/stops/${stopId}/transition`, input)
    }
    case 'completeStop': {
      const { tripId, stopId, body } = action.payload as { tripId: string; stopId: string; body: unknown }
      return apiClient.postJson(`/api/trips/${tripId}/stops/${stopId}/complete`, body)
    }
    case 'skipStop': {
      const { tripId, stopId, body } = action.payload as { tripId: string; stopId: string; body: unknown }
      return apiClient.postJson(`/api/trips/${tripId}/stops/${stopId}/skip`, body)
    }
    case 'incidentCreate':
      return apiClient.postJson('/api/my/incidents', action.payload)
    default:
      return Promise.reject(new Error(`Onbekende actie: ${action.kind as string}`))
  }
}

/** Secure logout cleanup: no queued work or cached snapshots survive for the next user. */
export function clearDriverOfflineState(): void {
  actionQueue.clear()
  actionQueue.setUser(null)
  try {
    for (let i = window.localStorage.length - 1; i >= 0; i--) {
      const key = window.localStorage.key(i)
      if (key && (key.startsWith('ts.driverSnapshot.') || key.startsWith('ts.actionQueue.'))) {
        window.localStorage.removeItem(key)
      }
    }
  } catch {
    // ignore storage errors
  }
}
