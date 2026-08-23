/**
 * Single source of truth for the employee create/edit section navigation: the ordered
 * core sections and, per section, the field keys it owns. Both drive the error badge on a
 * section tab and the first-error routing after a failed submit. Shared by create and edit
 * so the two modes always present the same section configuration (extra panel sections are
 * injected by the consuming page between the core sections and Notities).
 *
 * `labelKey` is an i18n key (employees.sections.*); consumers translate it via t() when
 * building the SectionDefs for SectionedForm.
 */
export interface EmployeeSectionMeta {
  id: string
  labelKey: string
  optional?: boolean
}

/** Core scalar-field sections rendered before any injected extras. */
export const EMPLOYEE_CORE_SECTIONS: EmployeeSectionMeta[] = [
  { id: 'algemeen', labelKey: 'employees.sections.algemeen' },
  { id: 'dienstverband', labelKey: 'employees.sections.dienstverband' },
  { id: 'hr', labelKey: 'employees.sections.hr', optional: true },
  { id: 'noodcontacten', labelKey: 'employees.sections.noodcontacten', optional: true },
]

/** Notities always closes the list, after the injected extras. */
export const EMPLOYEE_NOTITIES_SECTION: EmployeeSectionMeta = {
  id: 'notities',
  labelKey: 'employees.sections.notities',
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
