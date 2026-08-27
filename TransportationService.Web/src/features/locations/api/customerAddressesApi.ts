import { apiClient } from '../../../api/apiClient'
import type { LocationType } from '../types'

/**
 * Sprint 2 — central address master. One physical address can be used by many customers, so
 * the customer-specific part of the relationship (alias, reference, role, defaults,
 * instructions) lives on the LINK, not on the address.
 */

/** How a customer uses an address. */
export type CustomerLocationRole = 'Both' | 'Loading' | 'Unloading'

export const CUSTOMER_LOCATION_ROLE_KEYS: Record<CustomerLocationRole, string> = {
  Both: 'customers.addresses.roleBoth',
  Loading: 'customers.addresses.roleLoading',
  Unloading: 'customers.addresses.roleUnloading',
}

export interface CustomerAddress {
  linkId: string
  locationId: string
  customerId: string
  code: string
  name: string
  /** Customer-specific name; falls back to `name`. */
  alias: string | null
  customerReference: string | null
  type: LocationType
  role: CustomerLocationRole
  isDefaultLoading: boolean
  isDefaultUnloading: boolean
  isDefaultBilling: boolean
  instructions: string | null
  isActive: boolean
  /** False when the physical address itself is deactivated. */
  addressIsActive: boolean
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  /** How many customers use this same physical address (including this one). */
  linkedCustomerCount: number
}

export interface LinkCustomerAddressInput {
  locationId: string
  alias: string | null
  customerReference: string | null
  role: CustomerLocationRole
  isDefaultLoading: boolean
  isDefaultUnloading: boolean
  isDefaultBilling: boolean
  instructions: string | null
}

export interface UpdateCustomerAddressLinkInput {
  alias: string | null
  customerReference: string | null
  role: CustomerLocationRole
  isDefaultLoading: boolean
  isDefaultUnloading: boolean
  isDefaultBilling: boolean
  instructions: string | null
  isActive: boolean
}

export function listCustomerAddresses(customerId: string, includeInactive = false): Promise<CustomerAddress[]> {
  const query = includeInactive ? '?includeInactive=true' : ''
  return apiClient.getJson<CustomerAddress[]>(`/api/customers/${customerId}/addresses${query}`)
}

/** Links an EXISTING central address to this customer; never creates an address. */
export function linkCustomerAddress(customerId: string, input: LinkCustomerAddressInput): Promise<CustomerAddress> {
  return apiClient.postJson<CustomerAddress, LinkCustomerAddressInput>(`/api/customers/${customerId}/addresses`, input)
}

export function updateCustomerAddressLink(
  customerId: string,
  linkId: string,
  input: UpdateCustomerAddressLinkInput,
): Promise<CustomerAddress> {
  return apiClient.putJson<CustomerAddress, UpdateCustomerAddressLinkInput>(`/api/customers/${customerId}/addresses/${linkId}`, input)
}

/** Removes the relationship only — the physical address stays available to other customers. */
export function unlinkCustomerAddress(customerId: string, linkId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/customers/${customerId}/addresses/${linkId}`)
}

// ------------------------------------------------------------ duplicates

/** Exact = same front door (needs an explicit override); SameStreet = worth showing only. */
export type AddressDuplicateMatch = 'Exact' | 'SameStreet'

export interface AddressDuplicateCandidate {
  locationId: string
  code: string
  name: string
  match: AddressDuplicateMatch
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  isActive: boolean
  /** Customers already using this address — the reason to reuse it. */
  linkedCustomers: string[]
}

export interface AddressDuplicateCheckResult {
  hasExactMatch: boolean
  candidates: AddressDuplicateCandidate[]
}

export interface AddressDuplicateCheckInput {
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  excludeLocationId?: string | null
}

export function checkAddressDuplicates(input: AddressDuplicateCheckInput): Promise<AddressDuplicateCheckResult> {
  return apiClient.postJson<AddressDuplicateCheckResult, AddressDuplicateCheckInput>('/api/addresses/duplicate-check', input)
}

// ---------------------------------------------------------------- picker

/** Ranking of an offered address; the order is the priority (customer → recent → all). */
export type AddressPickerGroup = 'CustomerAddress' | 'Recent' | 'All'

export const ADDRESS_PICKER_GROUP_KEYS: Record<AddressPickerGroup, string> = {
  CustomerAddress: 'customers.addresses.groupCustomer',
  Recent: 'customers.addresses.groupRecent',
  All: 'customers.addresses.groupAll',
}

export interface AddressPickerOption {
  locationId: string
  code: string
  name: string
  type: LocationType
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  group: AddressPickerGroup
}

/** Customer addresses first, then recently used, then the rest of the master. */
export function pickAddresses(params: { customerId?: string | null; search?: string; take?: number }): Promise<AddressPickerOption[]> {
  const query = new URLSearchParams()
  if (params.customerId) query.set('customerId', params.customerId)
  if (params.search) query.set('search', params.search)
  if (params.take) query.set('take', String(params.take))
  return apiClient.getJson<AddressPickerOption[]>(`/api/addresses/picker?${query.toString()}`)
}
