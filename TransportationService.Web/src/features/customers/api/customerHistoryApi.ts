import { apiClient } from '../../../api/apiClient'

export interface CustomerHistoryChange {
  field: string
  before: string | null
  after: string | null
}

export interface CustomerHistoryEntry {
  id: string
  timestamp: string
  userName: string | null
  action: string
  actionLabel: string
  category: string
  changes: CustomerHistoryChange[]
  summary: string
}

/** Mirrors CustomerHistoryPageDto (GET /api/customers/{id}/history, customers.view). */
export interface CustomerHistoryPage {
  items: CustomerHistoryEntry[]
  totalCount: number
  page: number
  pageSize: number
}

export const getCustomerHistory = (
  customerId: string,
  page = 1,
  pageSize = 25,
  category?: string | null,
): Promise<CustomerHistoryPage> => {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
  if (category) {
    params.set('category', category)
  }
  return apiClient.getJson(`/api/customers/${customerId}/history?${params.toString()}`)
}
