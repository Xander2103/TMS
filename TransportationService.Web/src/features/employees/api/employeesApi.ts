import { apiClient } from '../../../api/apiClient'
import type { CreateEmployeeInput, EmployeeDetail, EmployeePagedResult, UpdateEmployeeInput } from '../types/employee'

export interface SearchEmployeesParams {
  search?: string
  isActive?: boolean
  page: number
  pageSize: number
}

export function searchEmployees(params: SearchEmployeesParams): Promise<EmployeePagedResult> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.isActive !== undefined) query.set('isActive', String(params.isActive))
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))

  return apiClient.getJson<EmployeePagedResult>(`/api/employees?${query.toString()}`)
}

export function getEmployee(id: string): Promise<EmployeeDetail> {
  return apiClient.getJson<EmployeeDetail>(`/api/employees/${id}`)
}

export function createEmployee(input: CreateEmployeeInput): Promise<EmployeeDetail> {
  return apiClient.postJson<EmployeeDetail, CreateEmployeeInput>('/api/employees', input)
}

export function updateEmployee(id: string, input: UpdateEmployeeInput): Promise<EmployeeDetail> {
  return apiClient.putJson<EmployeeDetail, UpdateEmployeeInput>(`/api/employees/${id}`, input)
}

export function deactivateEmployee(id: string): Promise<void> {
  return apiClient.postJson<void, Record<string, never>>(`/api/employees/${id}/deactivate`, {})
}
