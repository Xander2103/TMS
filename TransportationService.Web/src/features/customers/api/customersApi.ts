import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type {
  ChangeCustomerNumberInput,
  CustomerContact,
  CustomerContactInput,
  CustomerDetail,
  CustomerInput,
  CustomerListItem,
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

/** Official company-registry lookup on VAT or enterprise number (customers.create/edit). */
export function registryLookup(number: string): Promise<RegistryLookupResponse> {
  return apiClient.postJson<RegistryLookupResponse, { number: string }>('/api/customers/registry-lookup', { number })
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
