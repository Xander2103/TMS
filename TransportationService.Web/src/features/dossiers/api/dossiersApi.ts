import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type { OrderCustomerChangeImpact } from '../../transport-orders/api/transportOrdersApi'
import type {
  DossierActivityInput,
  DossierConfirmationEvaluation,
  DossierConfirmationSource,
  DossierDetail,
  DossierInput,
  DossierInvoiceStatus,
  DossierListItem,
  DossierPriceStatusFilter,
  DossierSortKey,
  DossierStatus,
  NewDossierInput,
} from '../types'

export function listDossiers(params: { search?: string; status?: string; customerId?: string } = {}): Promise<DossierListItem[]> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.status) query.set('status', params.status)
  if (params.customerId) query.set('customerId', params.customerId)
  const suffix = query.size > 0 ? `?${query.toString()}` : ''
  return apiClient.getJson<DossierListItem[]>(`/api/dossiers${suffix}`)
}

/** Query of GET /api/dossiers/search — every filter optional, all combined with AND; dates are YYYY-MM-DD. */
export interface SearchDossiersParams {
  search?: string
  status?: DossierStatus
  customerId?: string
  dateFrom?: string
  dateTo?: string
  dossierNumber?: string
  orderNumber?: string
  customerReference?: string
  customerNumber?: string
  confirmationSource?: DossierConfirmationSource
  confirmedFrom?: string
  confirmedTo?: string
  createdFrom?: string
  createdTo?: string
  planningFrom?: string
  planningTo?: string
  driverId?: string
  vehicleId?: string
  licensePlate?: string
  trailerId?: string
  activityTypeId?: string
  loadingCity?: string
  unloadingCity?: string
  postalCode?: string
  countryCode?: string
  priceStatus?: DossierPriceStatusFilter
  invoiceStatus?: DossierInvoiceStatus
  hasCmr?: boolean
  sort?: DossierSortKey
  dir?: 'asc' | 'desc'
  page?: number
  /** Max 200 (server-side cap). */
  pageSize?: number
}

/** Paged, filterable dossier list (list redesign 2026-09-23). Empty/undefined values are omitted from the query. */
export function searchDossiers(params: SearchDossiersParams = {}): Promise<PagedResult<DossierListItem>> {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue
    query.set(key, String(value))
  }
  const suffix = query.size > 0 ? `?${query.toString()}` : ''
  return apiClient.getJson<PagedResult<DossierListItem>>(`/api/dossiers/search${suffix}`)
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
  /** Dossier-level documents published to the old customer that go back to internal. */
  documentsPublicationWithdrawn: number
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

/** D1 "Inplannen": body of `POST /api/dossiers/{id}/activities/{activityId}/plan` — every field optional. */
export interface PlanDossierActivityInput {
  tripDate?: string | null
  driverId?: string | null
  vehicleId?: string | null
  trailerId?: string | null
  vehicleSelectionSource?: 'Suggested' | 'Manual' | null
  /** Dossier concurrency token; a stale one yields 409 with the current dossier. */
  version?: string
}

/**
 * Puts the activity's ORDER on a Draft trip (idempotent: an open trip is reused untouched) and
 * returns the refreshed dossier. Driver/vehicle/trailer live only on the trip; later changes run
 * through the trip endpoints. Needs `planning.create`.
 */
export function planDossierActivity(id: string, activityId: string, input: PlanDossierActivityInput = {}): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, PlanDossierActivityInput>(`/api/dossiers/${id}/activities/${activityId}/plan`, input)
}

/**
 * Stap 13: agreed price of a standalone billable activity (Opslag, Kraan, …). `fixedAmount` null
 * clears the price; `version` is the activity price record's own token (null for the first save).
 * Returns the full dossier — the panel re-renders totals, readiness and the activity from it.
 */
export function setActivityPrice(
  id: string,
  activityId: string,
  input: { fixedAmount: number | null; version: string | null },
): Promise<DossierDetail> {
  return apiClient.putJson<DossierDetail, typeof input>(`/api/dossiers/${id}/activities/${activityId}/price`, input)
}

export function updateDossier(id: string, input: DossierInput): Promise<DossierDetail> {
  return apiClient.putJson<DossierDetail, DossierInput>(`/api/dossiers/${id}`, input)
}

// --- Lifecycle (confirmation sprint 2026-09-23) ---

export interface ConfirmDossierInput {
  reason?: string | null
  /** Must be true when the evaluation lists warnings; blockers can never be acknowledged away. */
  acknowledgeWarnings: boolean
  version?: string
}

export function getDossierConfirmation(id: string): Promise<DossierConfirmationEvaluation> {
  return apiClient.getJson<DossierConfirmationEvaluation>(`/api/dossiers/${id}/confirmation`)
}

export function confirmDossier(id: string, input: ConfirmDossierInput): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, ConfirmDossierInput>(`/api/dossiers/${id}/confirm`, input)
}

export function reopenDossier(id: string, reason: string, version?: string): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, { reason: string; version?: string }>(`/api/dossiers/${id}/reopen`, { reason, version })
}

export function cancelDossier(id: string, reason: string, version?: string): Promise<DossierDetail> {
  return apiClient.postJson<DossierDetail, { reason: string; version?: string }>(`/api/dossiers/${id}/cancel`, { reason, version })
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
