import { apiClient } from '../../../api/apiClient'
import type { IncidentDetail, IncidentInput, IncidentListItem } from '../types'

export function listIncidents(
  params: { search?: string; status?: string; severity?: string; dossierId?: string; customerId?: string } = {},
): Promise<IncidentListItem[]> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.status) query.set('status', params.status)
  if (params.severity) query.set('severity', params.severity)
  if (params.dossierId) query.set('dossierId', params.dossierId)
  if (params.customerId) query.set('customerId', params.customerId)
  const suffix = query.size > 0 ? `?${query.toString()}` : ''
  return apiClient.getJson<IncidentListItem[]>(`/api/incidents${suffix}`)
}

export function getIncident(id: string): Promise<IncidentDetail> {
  return apiClient.getJson<IncidentDetail>(`/api/incidents/${id}`)
}

export function createIncident(input: IncidentInput): Promise<IncidentDetail> {
  return apiClient.postJson<IncidentDetail, IncidentInput>('/api/incidents', input)
}

export function updateIncident(id: string, input: IncidentInput): Promise<IncidentDetail> {
  return apiClient.putJson<IncidentDetail, IncidentInput>(`/api/incidents/${id}`, input)
}

// --- Wave 6: doorrekening + herlevering + verenigde problemenlijst ---

export function proposeIncidentCharge(id: string, amount: number, description: string): Promise<IncidentDetail> {
  return apiClient.postJson(`/api/incidents/${id}/charge/propose`, { amount, description })
}

export function decideIncidentCharge(id: string, approve: boolean): Promise<IncidentDetail> {
  return apiClient.postJson(`/api/incidents/${id}/charge/decide`, { approve })
}

export function createIncidentRedelivery(id: string): Promise<IncidentDetail> {
  return apiClient.postJson(`/api/incidents/${id}/redelivery`, {})
}

export interface ProblemListItem {
  id: string
  kind: 'Incident' | 'Exception'
  title: string
  severity: string
  status: string
  occurredAt: string
  orderNumber: string | null
  tripNumber: string | null
  tripId: string | null
  dossierNumber: string | null
  dossierId: string | null
  responsibleParty: string
  chargeDecision: string
}

export function listProblems(): Promise<ProblemListItem[]> {
  return apiClient.getJson('/api/problems')
}

export function changeIncidentStatus(
  id: string,
  input: { status: string; resolution?: string | null },
): Promise<IncidentDetail> {
  return apiClient.postJson<IncidentDetail, typeof input>(`/api/incidents/${id}/status`, input)
}
