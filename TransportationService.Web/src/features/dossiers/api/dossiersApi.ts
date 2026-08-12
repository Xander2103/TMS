import { apiClient } from '../../../api/apiClient'
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

export function changeDossierLegalEntity(id: string, legalEntityId: string, version?: string): Promise<DossierDetail> {
  return apiClient.putJson<DossierDetail, { legalEntityId: string; version?: string }>(
    `/api/dossiers/${id}/legal-entity`,
    { legalEntityId, version },
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
