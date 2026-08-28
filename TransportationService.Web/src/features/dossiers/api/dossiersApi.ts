import { apiClient } from '../../../api/apiClient'
import type { OrderCustomerChangeImpact } from '../../transport-orders/api/transportOrdersApi'
import type { DossierActivityInput, DossierDetail, DossierInput, DossierListItem, NewDossierInput } from '../types'

export function listDossiers(params: { search?: string; status?: string; customerId?: string } = {}): Promise<DossierListItem[]> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.status) query.set('status', params.status)
  if (params.customerId) query.set('customerId', params.customerId)
  const suffix = query.size > 0 ? `?${query.toString()}` : ''
  return apiClient.getJson<DossierListItem[]>(`/api/dossiers${suffix}`)
}

/** Dashboardtegel "Dossiers met aandacht": open dossiers met structurele aandachtspunten. */
export function getDossierAttentionCount(): Promise<number> {
  return apiClient.getJson<{ count: number }>('/api/dossiers/attention-count').then((r) => r.count)
}

export function getDossier(id: string): Promise<DossierDetail> {
  return apiClient.getJson<DossierDetail>(`/api/dossiers/${id}`)
}

export function createDossier(input: DossierInput): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, DossierInput>('/api/dossiers', input)
}

/** Fast create (§8): customer only; optional date/reference/template tile. */
export function createDossierFast(input: NewDossierInput): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, NewDossierInput>('/api/dossiers', input)
}

export function changeDossierLegalEntity(
  id: string, legalEntityId: string, version?: string, reason?: string,
): Promise<DossierDetail> {
  return apiClient.putJson<DossierDetail, { legalEntityId: string; version?: string; reason?: string }>(
    `/api/dossiers/${id}/legal-entity`,
    { legalEntityId, version, reason },
  )
}

/** Mirrors DossierLegalEntityChangeImpactDto (GET /api/dossiers/{id}/legal-entity/impact). */
export interface DossierLegalEntityChangeImpact {
  dossierId: string
  currentLegalEntityId: string | null
  targetLegalEntityId: string
  deviatesFromCustomerDefault: boolean
  blockedReason: string | null
  orders: { orderId: string; orderNumber: string; blockedReason: string | null; draftInvoiceLinesReleased: number }[]
  draftInvoiceLinesReleased: number
}

export function getDossierLegalEntityImpact(id: string, legalEntityId: string): Promise<DossierLegalEntityChangeImpact> {
  const params = new URLSearchParams({ legalEntityId })
  return apiClient.getJson<DossierLegalEntityChangeImpact>(`/api/dossiers/${id}/legal-entity/impact?${params}`)
}

/** Mirrors DossierCustomerChangeImpactDto (GET /api/dossiers/{id}/customer/impact). */
export interface DossierCustomerChangeImpact {
  dossierId: string
  dossierNumber: string
  currentCustomerId: string | null
  currentCustomerName: string | null
  newCustomerId: string
  newCustomerName: string
  blockedReason: string | null
  newLegalEntityId: string | null
  newInvoiceLanguage: string | null
  newVatTreatment: string | null
  orders: OrderCustomerChangeImpact[]
  ordersLeftOnOtherCustomer: string[]
}

export function getDossierCustomerChangeImpact(id: string, newCustomerId: string): Promise<DossierCustomerChangeImpact> {
  return apiClient.getJson<DossierCustomerChangeImpact>(
    `/api/dossiers/${id}/customer/impact?newCustomerId=${encodeURIComponent(newCustomerId)}`,
  )
}

/** Sprint 6: the dossier is the commercial authority — every linked order moves with it, in one transaction. */
export function changeDossierCustomer(id: string, newCustomerId: string, reason: string, version?: string): Promise<DossierDetail> {
  return apiClient.putJson<DossierDetail, { newCustomerId: string; reason: string; version?: string }>(
    `/api/dossiers/${id}/customer`,
    { newCustomerId, reason, version },
  )
}

export function addDossierActivity(id: string, input: DossierActivityInput): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, DossierActivityInput>(`/api/dossiers/${id}/activities`, input)
}

export function updateDossierActivity(id: string, activityId: string, input: DossierActivityInput): Promise<DossierDetail> {
  return apiClient.putJson<DossierDetail, DossierActivityInput>(`/api/dossiers/${id}/activities/${activityId}`, input)
}

export function deleteDossierActivity(id: string, activityId: string, version?: string): Promise<DossierDetail> {
  const suffix = version ? `?version=${encodeURIComponent(version)}` : ''
  return apiClient.deleteJson<DossierDetail>(`/api/dossiers/${id}/activities/${activityId}${suffix}`)
}

/** "Transportopdracht aanmaken" on an existing order-less transport activity. */
export function createOrderForActivity(id: string, activityId: string, version?: string): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, { version?: string }>(
    `/api/dossiers/${id}/activities/${activityId}/create-order`,
    { version },
  )
}

export function updateDossier(id: string, input: DossierInput): Promise<DossierDetail> {
  return apiClient.putJson<DossierDetail, DossierInput>(`/api/dossiers/${id}`, input)
}

export function closeDossier(id: string): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, Record<string, never>>(`/api/dossiers/${id}/close`, {})
}

export function reopenDossier(id: string): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, Record<string, never>>(`/api/dossiers/${id}/reopen`, {})
}

export function linkDossierOrder(id: string, transportOrderId: string): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, { transportOrderId: string }>(`/api/dossiers/${id}/orders`, { transportOrderId })
}

export function unlinkDossierOrder(id: string, transportOrderId: string): Promise<DossierDetail> {
  return apiClient.deleteJson<DossierDetail>(`/api/dossiers/${id}/orders/${transportOrderId}`)
}

export function addDossierRelation(
  id: string,
  input: { targetDossierId: string; relationType: string; notes: string | null },
): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, typeof input>(`/api/dossiers/${id}/relations`, input)
}

export function removeDossierRelation(id: string, relationId: string): Promise<DossierDetail> {
  return apiClient.deleteJson<DossierDetail>(`/api/dossiers/${id}/relations/${relationId}`)
}
