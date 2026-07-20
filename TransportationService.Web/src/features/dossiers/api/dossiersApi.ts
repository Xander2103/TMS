import { apiClient } from '../../../api/apiClient'
import type { DossierDetail, DossierInput, DossierListItem } from '../types'

export function listDossiers(params: { search?: string; status?: string; customerId?: string } = {}): Promise<DossierListItem[]> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.status) query.set('status', params.status)
  if (params.customerId) query.set('customerId', params.customerId)
  const suffix = query.size > 0 ? `?${query.toString()}` : ''
  return apiClient.getJson<DossierListItem[]>(`/api/dossiers${suffix}`)
}

export function getDossier(id: string): Promise<DossierDetail> {
  return apiClient.getJson<DossierDetail>(`/api/dossiers/${id}`)
}

export function createDossier(input: DossierInput): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, DossierInput>('/api/dossiers', input)
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
