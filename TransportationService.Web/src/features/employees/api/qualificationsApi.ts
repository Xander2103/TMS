import { apiClient } from '../../../api/apiClient'
import type {
  CreateEmployeeQualificationInput,
  EmployeeQualification,
  QualificationType,
  UpdateEmployeeQualificationInput,
} from '../types/qualification'

export function getEmployeeQualifications(employeeId: string): Promise<EmployeeQualification[]> {
  return apiClient.getJson<EmployeeQualification[]>(`/api/employees/${employeeId}/qualifications`)
}

export function createEmployeeQualification(
  employeeId: string,
  input: CreateEmployeeQualificationInput,
): Promise<EmployeeQualification> {
  return apiClient.postJson<EmployeeQualification, CreateEmployeeQualificationInput>(
    `/api/employees/${employeeId}/qualifications`,
    input,
  )
}

export function updateEmployeeQualification(
  employeeId: string,
  id: string,
  input: UpdateEmployeeQualificationInput,
): Promise<EmployeeQualification> {
  return apiClient.putJson<EmployeeQualification, UpdateEmployeeQualificationInput>(
    `/api/employees/${employeeId}/qualifications/${id}`,
    input,
  )
}

export function verifyQualification(employeeId: string, id: string): Promise<EmployeeQualification> {
  return apiClient.postJson<EmployeeQualification, Record<string, never>>(
    `/api/employees/${employeeId}/qualifications/${id}/verify`,
    {},
  )
}

export function suspendQualification(employeeId: string, id: string): Promise<EmployeeQualification> {
  return apiClient.postJson<EmployeeQualification, Record<string, never>>(
    `/api/employees/${employeeId}/qualifications/${id}/suspend`,
    {},
  )
}

export function getQualificationTypes(): Promise<QualificationType[]> {
  return apiClient.getJson<QualificationType[]>('/api/qualification-types')
}

export function getExpiringQualifications(days: number): Promise<EmployeeQualification[]> {
  return apiClient.getJson<EmployeeQualification[]>(`/api/qualifications/expiring?days=${days}`)
}
