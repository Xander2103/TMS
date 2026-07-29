import { apiClient } from '../../../api/apiClient'

export interface EmployeeHistoryChange {
  field: string
  before: string | null
  after: string | null
}

export interface EmployeeHistoryEntry {
  id: string
  timestamp: string
  userName: string | null
  action: string
  actionLabel: string
  category: string
  changes: EmployeeHistoryChange[]
  summary: string
}

export interface EmployeeHistoryPage {
  items: EmployeeHistoryEntry[]
  totalCount: number
  page: number
  pageSize: number
}

export const getEmployeeHistory = (
  employeeId: string,
  page = 1,
  pageSize = 25,
  category?: string | null,
): Promise<EmployeeHistoryPage> => {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
  if (category) {
    params.set('category', category)
  }
  return apiClient.getJson(`/api/employees/${employeeId}/history?${params.toString()}`)
}
