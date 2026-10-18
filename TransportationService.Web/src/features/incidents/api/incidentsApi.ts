import { apiClient } from '../../../api/apiClient'
import type { AssignableUser, IncidentDetail, IncidentInput, IncidentListItem, IncidentUpdate, LinkedDamageSeverity } from '../types'

export function listIncidents(
  params: {
    search?: string; status?: string; severity?: string; dossierId?: string; customerId?: string
    transportOrderId?: string; tripId?: string
    mine?: boolean; driverId?: string; vehicleId?: string; from?: string; to?: string; openOnly?: boolean
  } = {},
): Promise<IncidentListItem[]> {
  const query = new URLSearchParams()
  if (params.mine) query.set('mine', 'true')
  if (params.driverId) query.set('driverId', params.driverId)
  if (params.vehicleId) query.set('vehicleId', params.vehicleId)
  if (params.from) query.set('from', params.from)
  if (params.to) query.set('to', params.to)
  if (params.openOnly) query.set('openOnly', 'true')
  if (params.search) query.set('search', params.search)
  if (params.status) query.set('status', params.status)
  if (params.severity) query.set('severity', params.severity)
  if (params.dossierId) query.set('dossierId', params.dossierId)
  if (params.customerId) query.set('customerId', params.customerId)
  if (params.transportOrderId) query.set('transportOrderId', params.transportOrderId)
  if (params.tripId) query.set('tripId', params.tripId)
  const suffix = query.size > 0 ? `?${query.toString()}` : ''
  return apiClient.getJson<IncidentListItem[]>(`/api/incidents${suffix}`)
}

export function getIncident(id: string): Promise<IncidentDetail> {
  return apiClient.getJson<IncidentDetail>(`/api/incidents/${id}`)
}

export function createIncident(input: IncidentInput): Promise<IncidentDetail> {
  return apiClient.postJson<IncidentDetail, IncidentInput>('/api/incidents', input)
}

/** EX-01: section-wise update — only the sections sent are changed. */
export function updateIncident(id: string, update: IncidentUpdate): Promise<IncidentDetail> {
  return apiClient.putJson<IncidentDetail, IncidentUpdate>(`/api/incidents/${id}`, update)
}

/** EX-07: wie verantwoordelijk kan worden gesteld (veilige velden, geen users.view nodig). */
export function getAssignableUsers(): Promise<AssignableUser[]> {
  return apiClient.getJson<AssignableUser[]>('/api/incidents/assignable-users')
}

/** EX-03: de gekoppelde schademelding voor het wagenpark. */
export function createIncidentDamageReport(
  id: string, input: { severity: LinkedDamageSeverity; description?: string | null; location?: string | null },
): Promise<IncidentDetail> {
  return apiClient.postJson(`/api/incidents/${id}/damage-report`, input)
}

export interface OrderDeliveryContext {
  deliveryOutcome: string | null
  redeliveryOrderId: string | null
  redeliveryOrderNumber: string | null
  redeliveryOfOrderId: string | null
  redeliveryOfOrderNumber: string | null
  sourceIncidentId: string | null
  sourceIncidentTitle: string | null
}

/** EX-08: wat de geadresseerde echt ontving + de herleveringsrelatie in beide richtingen. */
export function getOrderDeliveryContext(orderId: string): Promise<OrderDeliveryContext> {
  return apiClient.getJson<OrderDeliveryContext>(`/api/transport-orders/${orderId}/delivery-context`)
}

export interface VehicleSafetyIssue {
  id: string
  kind: 'Incident' | 'Exception'
  typeLabel: string
  title: string
  severity: string
  status: string
  occurredAt: string
  tripNumber: string | null
  orderNumber: string | null
  route: string | null
  driverName: string | null
  damageReportId: string | null
  safetyIssue: boolean
}

/** EX-03: open problemen op een voertuig, voor het wagenpark (geen klant-, kost- of HR-gegevens). */
export function getVehicleSafetyIssues(vehicleId: string): Promise<VehicleSafetyIssue[]> {
  return apiClient.getJson<VehicleSafetyIssue[]>(`/api/vehicles/${vehicleId}/safety-issues`)
}

// --- Wave 6: doorrekening + herlevering + verenigde problemenlijst ---

export function proposeIncidentCharge(id: string, amount: number, description: string): Promise<IncidentDetail> {
  return apiClient.postJson(`/api/incidents/${id}/charge/propose`, { amount, description })
}

export function decideIncidentCharge(id: string, approve: boolean): Promise<IncidentDetail> {
  return apiClient.postJson(`/api/incidents/${id}/charge/decide`, { approve })
}

export function createIncidentRedelivery(
  id: string, input: { quantity?: number | null; free?: boolean; freeReason?: string | null } = {},
): Promise<IncidentDetail> {
  return apiClient.postJson(`/api/incidents/${id}/redelivery`, input)
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
  /** Machinecode + Nederlands label van het type (nooit een ruwe enum op het scherm). */
  typeCode?: string
  typeLabel?: string
  responsibleName?: string | null
  customerName?: string | null
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
