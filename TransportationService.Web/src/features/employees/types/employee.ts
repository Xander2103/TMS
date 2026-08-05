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

export type CivilStatus =
  | 'Married'
  | 'Unmarried'
  | 'Widowed'
  | 'Divorced'
  | 'LegallyCohabiting'
  | 'Single'
  | 'Other'

export const CIVIL_STATUS_LABELS: Record<CivilStatus, string> = {
  Married: 'Gehuwd',
  Unmarried: 'Ongehuwd',
  Widowed: 'Weduwe/weduwnaar',
  Divorced: 'Gescheiden',
  LegallyCohabiting: 'Wettelijk samenwonend',
  Single: 'Alleenstaand',
  Other: 'Andere',
}

/** Emergency contact as returned in EmployeeDetail (EmployeeEmergencyContactDto). */
export interface EmployeeEmergencyContact {
  id: string
  name: string
  relationship: string | null
  phone: string | null
  mobilePhone: string | null
  notes: string | null
  priority: number
}

/** Emergency contact payload (EmployeeEmergencyContactInput); existing contacts keep their id. */
export interface EmployeeEmergencyContactInput {
  id?: string | null
  name: string
  relationship: string | null
  phone: string | null
  mobilePhone: string | null
  notes: string | null
  priority: number
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
  /** Populated only in the "Chauffeurs" view (rows with a driver profile). */
  driverIsBlocked?: boolean | null
  driverAvailability?: DriverAvailabilityStatus | null
}

export type DriverAvailabilityStatus = 'Available' | 'Unavailable' | 'OnLeave' | 'OnTrip'

export const DRIVER_AVAILABILITY_LABELS: Record<DriverAvailabilityStatus, string> = {
  Available: 'Beschikbaar',
  Unavailable: 'Niet beschikbaar',
  OnLeave: 'Afwezig',
  OnTrip: 'Onderweg',
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
  civilStatus: CivilStatus | null
  dependentChildren: number | null
  dimonaNumber: string | null
  // Confidential — null when the user lacks employees.view_confidential.
  identityCardNumber: string | null
  emergencyContacts: EmployeeEmergencyContact[]
}

export interface EmployeeInput {
  firstName: string
  lastName: string
  // Alleen voor- en achternaam blokkeren; alle andere velden zijn optioneel (spec §13).
  dateOfBirth: string | null
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  phoneNumber: string | null
  email: string | null
  employmentStartDate: string | null
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
  nationalRegisterNumber: string | null
  iban: string | null
  bic: string | null
  notes: string | null
  civilStatus: CivilStatus | null
  dependentChildren: number | null
  dimonaNumber: string | null
  identityCardNumber: string | null
  /** Wholesale sync — the backend derives the legacy single emergency name/phone from this. */
  emergencyContacts: EmployeeEmergencyContactInput[]
}

export interface CreateEmployeeQualificationInput {
  qualificationTypeId: string
  documentNumber: string | null
  obtainedDate: string
  expiryDate: string | null
  notes: string | null
}

export interface CreateEmployeeInput extends EmployeeInput {
  driverProfile?: { driverCategoryIds: string[]; notes: string | null } | null
  /** Optional qualifications created atomically with the employee. */
  qualifications?: CreateEmployeeQualificationInput[] | null
}

export type UpdateEmployeeInput = EmployeeInput

export interface EmployeePagedResult {
  items: EmployeeListItem[]
  totalCount: number
  page: number
  pageSize: number
}
