import type { SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import type { LabelResolver } from '../../../hooks/useResolvedLabel'
import { getCustomer, searchCustomers } from './customersApi'
import type { CustomerListItem } from '../types'

/**
 * PERF-H3: rows per picker search. Pickers used to load "the first 200 customers" and filter
 * them in the browser, so customer #201 of 2,173 could never be chosen and nobody was told.
 * Every picker now asks the server for what the user typed (name, customer number or VAT
 * number) and shows one short page of hits.
 */
export const PICKER_PAGE_SIZE = 25

export interface CustomerOptionFilters {
  isActive?: boolean
  categoryId?: string
}

/** "KL-0133 · BE0123456789 · Gent": the second line that tells two similar names apart. */
export function customerOptionSubtitle(customer: Pick<CustomerListItem, 'customerNumber' | 'vatNumber' | 'city'>): string {
  return [customer.customerNumber, customer.vatNumber ?? null, customer.city ?? null].filter(Boolean).join(' · ')
}

/** "Naam (KL-0133)" — the one-line form for places that render a customer as plain text. */
export function customerOptionLabel(customer: Pick<CustomerListItem, 'name' | 'customerNumber'>): string {
  return `${customer.name} (${customer.customerNumber})`
}

export function toCustomerOption(customer: CustomerListItem): SearchableSelectOption {
  return {
    value: customer.id,
    label: customer.name,
    title: customer.name,
    subtitle: customerOptionSubtitle(customer),
    keywords: [customer.customerNumber, customer.vatNumber ?? ''].join(' ').trim(),
  }
}

/** Async option source for `SearchableSelect onSearch`: one page of customers matching `query`. */
export async function searchCustomerOptions(
  query: string,
  signal: AbortSignal,
  filters: CustomerOptionFilters = {},
): Promise<SearchableSelectOption[]> {
  const page = await searchCustomers(
    {
      search: query.trim() || undefined,
      isActive: filters.isActive,
      categoryId: filters.categoryId,
      page: 1,
      pageSize: PICKER_PAGE_SIZE,
    },
    signal,
  )
  return page.items.map(toCustomerOption)
}

/** The most common picker: active customers only. */
export const searchActiveCustomerOptions = (query: string, signal: AbortSignal): Promise<SearchableSelectOption[]> =>
  searchCustomerOptions(query, signal, { isActive: true })

/** Label for a customer the screen only knows by id (a URL filter, a stored reference). */
export const resolveCustomerLabel: LabelResolver = async (id) => (await getCustomer(id)).name
