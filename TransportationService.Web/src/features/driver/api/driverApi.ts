import { apiClient } from '../../../api/apiClient'
import { actionQueue } from '../actionQueue'
import { OfflineQueuedError } from '../offlineActions'
import type { DriverDocument, DriverIncidentInput, MyDashboard } from '../types'

export function getMyDashboard(): Promise<MyDashboard> {
  return apiClient.getJson<MyDashboard>('/api/my/dashboard')
}

export function listMyDocuments(): Promise<DriverDocument[]> {
  return apiClient.getJson<DriverDocument[]>('/api/my/documents')
}

export interface MyIncident {
  id: string
  title: string
  incidentType: string
  customTypeName: string | null
  status: string
  severity: string
  createdAt: string
}

export function listMyIncidents(): Promise<MyIncident[]> {
  return apiClient.getJson<MyIncident[]>('/api/my/incidents')
}

/** Offline-aware: a network failure queues the report (idempotent via clientRequestId). */
export async function createMyIncident(input: DriverIncidentInput): Promise<unknown> {
  const body = { ...input, clientRequestId: input.clientRequestId ?? crypto.randomUUID() }
  try {
    return await apiClient.postJson('/api/my/incidents', body)
  } catch (error) {
    if (typeof (error as { status?: number }).status !== 'number') {
      actionQueue.enqueue({
        kind: 'incidentCreate',
        label: `Incident: ${input.title}`,
        clientRequestId: body.clientRequestId,
        payload: body as unknown as Record<string, unknown>,
      })
      throw new OfflineQueuedError()
    }
    throw error
  }
}
