import { apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import type { PagedResult } from '../../../api/types'
import type {
  ChangeCustomerNumberInput,
  CustomerContact,
  CustomerContactInput,
  CustomerDetail,
  CustomerInput,
  CustomerListItem,
  CustomerPeppolVerifyResult,
  PeppolScheme,
  RegistryLookupResponse,
  UpdateCustomerInput,
  VatTreatmentInfo,
} from '../types'

export interface SearchCustomersParams {
  search?: string
  isActive?: boolean
  categoryId?: string
  page: number
  pageSize: number
}

export function searchCustomers(params: SearchCustomersParams): Promise<PagedResult<CustomerListItem>> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.isActive !== undefined) query.set('isActive', String(params.isActive))
  if (params.categoryId) query.set('categoryId', params.categoryId)
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return apiClient.getJson<PagedResult<CustomerListItem>>(`/api/customers?${query.toString()}`)
}

/** The coherent VAT-treatment catalog (labels, rates, legal texts); backend is authoritative. */
export function getVatTreatments(): Promise<VatTreatmentInfo[]> {
  return apiClient.getJson<VatTreatmentInfo[]>('/api/customers/vat-treatments')
}

export function getPeppolSchemes(): Promise<PeppolScheme[]> {
  return apiClient.getJson<PeppolScheme[]>('/api/customers/peppol-schemes')
}

/** Official company-registry lookup on VAT or enterprise number (customers.create/edit). */
export function registryLookup(number: string): Promise<RegistryLookupResponse> {
  return apiClient.postJson<RegistryLookupResponse, { number: string }>('/api/customers/registry-lookup', { number })
}

/** Checks the customer's Peppol identity at the provider directory (peppol.validate). */
export function verifyCustomerPeppol(id: string): Promise<CustomerPeppolVerifyResult> {
  return apiClient.postJson<CustomerPeppolVerifyResult, Record<string, never>>(`/api/customers/${id}/peppol/verify`, {})
}

export function getCustomer(id: string): Promise<CustomerDetail> {
  return apiClient.getJson<CustomerDetail>(`/api/customers/${id}`)
}

export function createCustomer(input: CustomerInput): Promise<CustomerDetail> {
  return apiClient.postJson<CustomerDetail, CustomerInput>('/api/customers', input)
}

export function updateCustomer(id: string, input: UpdateCustomerInput): Promise<CustomerDetail> {
  return apiClient.putJson<CustomerDetail, UpdateCustomerInput>(`/api/customers/${id}`, input)
}

export function deleteCustomer(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/customers/${id}`)
}

/** Manual customer-number change (customers.override_number); reason is audited. */
export function changeCustomerNumber(id: string, input: ChangeCustomerNumberInput): Promise<CustomerDetail> {
  return apiClient.postJson<CustomerDetail, ChangeCustomerNumberInput>(`/api/customers/${id}/number`, input)
}

export function setCustomerActive(id: string, isActive: boolean): Promise<void> {
  return apiClient.postJson<void, { isActive: boolean }>(`/api/customers/${id}/active`, { isActive })
}

export function setCustomerBlocked(id: string, isBlocked: boolean, reason: string | null): Promise<void> {
  return apiClient.postJson<void, { isBlocked: boolean; reason: string | null }>(
    `/api/customers/${id}/blocked`,
    { isBlocked, reason },
  )
}

/** Eén orderregel in de documentvoorvertoning per dag. */
export interface CustomerDayDocumentRow {
  orderId: string
  orderNumber: string
  unloadingCity: string | null
  kind: 'DeliveryNote' | 'Cmr' | null
  source: string
  reason: string
  usesCustomerDocument: boolean
  noneRequired: boolean
  undecided: boolean
}

/** Voorvertoning van de te genereren documenten voor één leveringsdag (orders.view). */
export interface CustomerDayDocumentsPreview {
  date: string
  totalOrders: number
  ownDeliveryNotes: number
  ownCmrs: number
  customerDocuments: number
  noneRequired: number
  undecided: number
  rows: CustomerDayDocumentRow[]
}

export function getCustomerDayDocumentsPreview(id: string, date: string): Promise<CustomerDayDocumentsPreview> {
  return apiClient.getJson<CustomerDayDocumentsPreview>(`/api/customers/${id}/documents/preview?date=${date}`)
}

/** Batch-PDF (leveringsbonnen of CMR's) van één leveringsdag, zelfde blob-idioom als Wave 9. */
export async function downloadCustomerDayDocuments(
  id: string,
  kind: 'delivery-note' | 'cmr',
  date: string,
): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/customers/${id}/documents/${kind}?date=${date}`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  // Bewust zonder message: de aanroepende component toont zijn eigen vertaalde foutmelding.
  if (!response.ok) throw new Error()
  const blob = await response.blob()
  const disposition = response.headers.get('content-disposition')
  const fileName = disposition?.match(/filename="?([^";]+)"?/)?.[1] ?? `${kind}-${date}.pdf`
  const objectUrl = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = objectUrl
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(objectUrl)
}

export function addCustomerContact(customerId: string, input: CustomerContactInput): Promise<CustomerContact> {
  return apiClient.postJson<CustomerContact, CustomerContactInput>(`/api/customers/${customerId}/contacts`, input)
}

export function updateCustomerContact(
  customerId: string,
  contactId: string,
  input: CustomerContactInput,
): Promise<CustomerContact> {
  return apiClient.putJson<CustomerContact, CustomerContactInput>(
    `/api/customers/${customerId}/contacts/${contactId}`,
    input,
  )
}

export function removeCustomerContact(customerId: string, contactId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/customers/${customerId}/contacts/${contactId}`)
}
