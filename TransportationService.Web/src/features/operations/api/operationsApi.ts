import { apiClient } from '../../../api/apiClient'
import type { AlertSeverity, AlertStatus, OperationalAlert, OperationsOverview } from '../types'

export function getOperationsOverview(): Promise<OperationsOverview> {
  return apiClient.getJson<OperationsOverview>('/api/operations/overview')
}

export function listAlerts(params: { status?: AlertStatus; severity?: AlertSeverity; category?: string } = {}): Promise<OperationalAlert[]> {
  const query = new URLSearchParams()
  if (params.status) query.set('status', params.status)
  if (params.severity) query.set('severity', params.severity)
  if (params.category) query.set('category', params.category)
  const suffix = query.toString()
  return apiClient.getJson<OperationalAlert[]>(`/api/operations/alerts${suffix ? `?${suffix}` : ''}`)
}

export function acknowledgeAlert(id: string): Promise<OperationalAlert> {
  return apiClient.postJson<OperationalAlert, Record<string, never>>(`/api/operations/alerts/${id}/acknowledge`, {})
}

export function resolveAlert(id: string): Promise<OperationalAlert> {
  return apiClient.postJson<OperationalAlert, Record<string, never>>(`/api/operations/alerts/${id}/resolve`, {})
}
