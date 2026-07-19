export type EmploymentStatus = 'Active' | 'OnLeave' | 'Suspended' | 'Terminated'

export const EMPLOYMENT_STATUS_LABELS: Record<EmploymentStatus, string> = {
  Active: 'In dienst',
  OnLeave: 'Met verlof',
  Suspended: 'Geschorst',
  Terminated: 'Uit dienst',
}

export const EMPLOYMENT_STATUS_TONES: Record<EmploymentStatus, 'success' | 'warning' | 'danger' | 'neutral' | 'info'> = {
  Active: 'success',
  OnLeave: 'info',
  Suspended: 'warning',
  Terminated: 'neutral',
}

export interface EmployeeListItem {
  id: string
  employeeNumber: string
  firstName: string
  lastName: string
  functionNames: string[]
  departmentName: string | null
  employmentStatus: EmploymentStatus
  isActive: boolean
  isDriver: boolean
}

export interface EmployeeDetail {
  id: string
  employeeNumber: string
  firstName: string
  lastName: string
  dateOfBirth: string
  placeOfBirth: string | null
  nationalityCode: string | null
  preferredLanguageCode: string | null
  email: string
  phoneNumber: string
  mobilePhone: string | null
  street: string
  houseNumber: string
  postalCode: string
  city: string
  countryCode: string | null
  emergencyContactName: string | null
  emergencyContactPhone: string | null
  employmentStartDate: string
  employmentEndDate: string | null
  employmentStatus: EmploymentStatus
  departmentId: string | null
  departmentName: string | null
  contractTypeId: string | null
  contractTypeName: string | null
  jobFunctionIds: string[]
  functionNames: string[]
  isActive: boolean
  notes: string | null
  driverId: string | null
  // Confidential — null when the user lacks employees.view_confidential.
  nationalRegisterNumber: string | null
  iban: string | null
  bic: string | null
}

export interface EmployeeInput {
  firstName: string
  lastName: string
  dateOfBirth: string
  street: string
  houseNumber: string
  postalCode: string
  city: string
  phoneNumber: string
  email: string
  employmentStartDate: string
  employmentStatus: EmploymentStatus
  employmentEndDate?: string | null
  countryCode: string | null
  placeOfBirth: string | null
  nationalityCode: string | null
  preferredLanguageCode: string | null
  mobilePhone: string | null
  departmentId: string | null
  contractTypeId: string | null
  jobFunctionIds: string[]
  emergencyContactName: string | null
  emergencyContactPhone: string | null
  nationalRegisterNumber: string | null
  iban: string | null
  bic: string | null
  notes: string | null
}

export interface CreateEmployeeInput extends EmployeeInput {
  driverProfile?: { driverCategoryId: string | null; notes: string | null } | null
}

export type UpdateEmployeeInput = EmployeeInput

export interface EmployeePagedResult {
  items: EmployeeListItem[]
  totalCount: number
  page: number
  pageSize: number
}
