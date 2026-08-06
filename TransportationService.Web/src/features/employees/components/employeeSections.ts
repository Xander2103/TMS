/**
 * Single source of truth for the employee create/edit section navigation: the ordered
 * core sections and, per section, the field keys it owns. Both drive the error badge on a
 * section tab and the first-error routing after a failed submit. Shared by create and edit
 * so the two modes always present the same section configuration (extra panel sections are
 * injected by the consuming page between the core sections and Notities).
 */
export interface EmployeeSectionMeta {
  id: string
  label: string
  optional?: boolean
}

/** Core scalar-field sections rendered before any injected extras. */
export const EMPLOYEE_CORE_SECTIONS: EmployeeSectionMeta[] = [
  { id: 'algemeen', label: 'Algemeen' },
  { id: 'dienstverband', label: 'Dienstverband' },
  { id: 'hr', label: 'Identiteit & bank', optional: true },
  { id: 'noodcontacten', label: 'Noodcontacten', optional: true },
]

/** Notities always closes the list, after the injected extras. */
export const EMPLOYEE_NOTITIES_SECTION: EmployeeSectionMeta = {
  id: 'notities',
  label: 'Notities',
  optional: true,
}

/** Which validated field keys belong to which section (badge + first-error routing). */
export const EMPLOYEE_SECTION_FIELD_KEYS: Record<string, string[]> = {
  algemeen: [
    'firstName',
    'lastName',
    'dateOfBirth',
    'placeOfBirth',
    'nationalityCode',
    'preferredLanguageCode',
    'email',
    'phoneNumber',
    'mobilePhone',
    'street',
    'houseNumber',
    'postalCode',
    'city',
    'countryCode',
    'civilStatus',
    'dependentChildren',
  ],
  dienstverband: [
    'employmentStartDate',
    'employmentEndDate',
    'employmentStatus',
    'departmentId',
    'contractTypeId',
    'jobFunctionIds',
    'dimonaNumber',
  ],
  hr: [
    'identityCardNumber',
    'nationalRegisterNumber',
    'iban',
    'bic',
  ],
  noodcontacten: ['emergencyContacts'],
  notities: [],
}
