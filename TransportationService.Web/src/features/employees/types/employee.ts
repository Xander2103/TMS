export type EmploymentStatus = 'Active' | 'OnLeave' | 'Suspended' | 'Terminated'

export type EmployeeFunction =
  | 'DriverB'
  | 'DriverC'
  | 'DriverCE'
  | 'CraneOperator'
  | 'WarehouseWorker'
  | 'Planner'
  | 'Dispatcher'
  | 'OfficeWorker'
  | 'Mechanic'
  | 'Other'

export const EMPLOYMENT_STATUS_LABELS: Record<EmploymentStatus, string> = {
  Active: 'Actief',
  OnLeave: 'Met verlof',
  Suspended: 'Geschorst',
  Terminated: 'Uit dienst',
}

export const EMPLOYEE_FUNCTION_LABELS: Record<EmployeeFunction, string> = {
  DriverB: 'Chauffeur (rijbewijs B)',
  DriverC: 'Chauffeur (rijbewijs C)',
  DriverCE: 'Chauffeur (rijbewijs CE)',
  CraneOperator: 'Kraanmachinist',
  WarehouseWorker: 'Magazijnmedewerker',
  Planner: 'Planner',
  Dispatcher: 'Dispatcher',
  OfficeWorker: 'Kantoormedewerker',
  Mechanic: 'Monteur',
  Other: 'Overig',
}

export interface EmployeeListItem {
  id: string
  employeeNumber: string
  firstName: string
  lastName: string
  primaryFunction: EmployeeFunction
  employmentStatus: EmploymentStatus
  isActive: boolean
}

export interface EmployeeDetail {
  id: string
  employeeNumber: string
  firstName: string
  lastName: string
  street: string
  houseNumber: string
  postalCode: string
  city: string
  country: string
  phoneNumber: string
  email: string
  dateOfBirth: string
  employmentStartDate: string
  employmentEndDate: string | null
  employmentStatus: EmploymentStatus
  primaryFunction: EmployeeFunction
  isActive: boolean
  emergencyContactName: string | null
  emergencyContactPhone: string | null
  notes: string | null
}

export interface CreateEmployeeInput {
  firstName: string
  lastName: string
  street: string
  houseNumber: string
  postalCode: string
  city: string
  country: string
  phoneNumber: string
  email: string
  dateOfBirth: string
  employmentStartDate: string
  employmentStatus: EmploymentStatus
  primaryFunction: EmployeeFunction
  emergencyContactName: string | null
  emergencyContactPhone: string | null
  notes: string | null
}

export interface UpdateEmployeeInput {
  firstName: string
  lastName: string
  street: string
  houseNumber: string
  postalCode: string
  city: string
  country: string
  phoneNumber: string
  email: string
  dateOfBirth: string
  employmentEndDate: string | null
  employmentStatus: EmploymentStatus
  primaryFunction: EmployeeFunction
  emergencyContactName: string | null
  emergencyContactPhone: string | null
  notes: string | null
}

export interface EmployeePagedResult {
  items: EmployeeListItem[]
  totalCount: number
  page: number
  pageSize: number
}
