export type EmploymentStatus = 'Active' | 'OnLeave' | 'Suspended' | 'Terminated'

/** i18n-keys (employees.status.*) — render via t(EMPLOYMENT_STATUS_LABELS[status]). */
export const EMPLOYMENT_STATUS_LABELS: Record<EmploymentStatus, string> = {
  Active: 'employees.status.Active',
  OnLeave: 'employees.status.OnLeave',
  Suspended: 'employees.status.Suspended',
  Terminated: 'employees.status.Terminated',
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

/** i18n-keys (employees.civilStatus.*) — render via t(CIVIL_STATUS_LABELS[status]). */
export const CIVIL_STATUS_LABELS: Record<CivilStatus, string> = {
  Married: 'employees.civilStatus.Married',
  Unmarried: 'employees.civilStatus.Unmarried',
  Widowed: 'employees.civilStatus.Widowed',
  Divorced: 'employees.civilStatus.Divorced',
  LegallyCohabiting: 'employees.civilStatus.LegallyCohabiting',
  Single: 'employees.civilStatus.Single',
  Other: 'employees.civilStatus.Other',
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

/** One missing dossier-completeness requirement (HR maturity wave §2.1). `section` maps to an
 * `employeeSections` id (algemeen/dienstverband/hr/noodcontacten/documenten). */
export interface EmployeeCompletenessItem {
  code: string
  label: string
  section: string
}

/** Dossier-completeness snapshot (`EmployeeCompletenessDto`). `isComplete` is equivalent to
 * `missingItems.length === 0`. */
export interface EmployeeCompleteness {
  percentage: number
  isComplete: boolean
  missingItems: EmployeeCompletenessItem[]
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
  /** Dossier-completeness percentage (HR maturity wave §2.1). */
  completenessPercentage: number
  employmentEndDate: string | null
}

export type EmployeeSortOption =
  | 'name_asc'
  | 'name_desc'
  | 'number'
  | 'recent'
  | 'department'
  | 'function'
  | 'status'

export type DriverAvailabilityStatus = 'Available' | 'Unavailable' | 'OnLeave' | 'OnTrip'

/** i18n-keys (employees.driverAvailability.*) — render via t(DRIVER_AVAILABILITY_LABELS[s]). */
export const DRIVER_AVAILABILITY_LABELS: Record<DriverAvailabilityStatus, string> = {
  Available: 'employees.driverAvailability.Available',
  Unavailable: 'employees.driverAvailability.Unavailable',
  OnLeave: 'employees.driverAvailability.OnLeave',
  OnTrip: 'employees.driverAvailability.OnTrip',
}

export interface EmployeeDetail {
  id: string
  employeeNumber: string
  firstName: string
  lastName: string
  // Only the name is guaranteed: a minimal dossier (spec §13) leaves the rest null.
  dateOfBirth: string | null
  placeOfBirth: string | null
  nationalityCode: string | null
  preferredLanguageCode: string | null
  email: string | null
  phoneNumber: string | null
  mobilePhone: string | null
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  emergencyContactName: string | null
  emergencyContactPhone: string | null
  employmentStartDate: string | null
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
  /** Null only if the backend didn't compute it (should not happen); absent-safe in the UI. */
  completeness: EmployeeCompleteness | null
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
  // NB: no `notes` — the legacy Employee.Notes column is read-only historical data;
  // notes are managed through the EmployeeNote endpoints (EmployeeNotesPanel).
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
