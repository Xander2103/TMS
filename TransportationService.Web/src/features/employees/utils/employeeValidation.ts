import type { CreateEmployeeInput, EmployeeFunction, EmploymentStatus, UpdateEmployeeInput } from '../types/employee'

export interface EmployeeFormValues {
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
  employmentEndDate: string
  employmentStatus: EmploymentStatus
  primaryFunction: EmployeeFunction
  emergencyContactName: string
  emergencyContactPhone: string
  notes: string
}

export type EmployeeFormErrors = Partial<Record<keyof EmployeeFormValues, string>>

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function validateEmployeeForm(values: EmployeeFormValues, isEditMode: boolean): EmployeeFormErrors {
  const errors: EmployeeFormErrors = {}

  if (!values.firstName.trim()) errors.firstName = 'Voornaam is verplicht.'
  if (!values.lastName.trim()) errors.lastName = 'Achternaam is verplicht.'
  if (!values.street.trim()) errors.street = 'Straat is verplicht.'
  if (!values.houseNumber.trim()) errors.houseNumber = 'Huisnummer is verplicht.'
  if (!values.postalCode.trim()) errors.postalCode = 'Postcode is verplicht.'
  if (!values.city.trim()) errors.city = 'Plaats is verplicht.'
  if (!values.country.trim()) errors.country = 'Land is verplicht.'
  if (!values.phoneNumber.trim()) errors.phoneNumber = 'Telefoonnummer is verplicht.'

  if (!values.email.trim()) {
    errors.email = 'E-mail is verplicht.'
  } else if (!EMAIL_PATTERN.test(values.email.trim())) {
    errors.email = 'Ongeldig e-mailadres.'
  }

  if (!values.dateOfBirth) {
    errors.dateOfBirth = 'Geboortedatum is verplicht.'
  } else if (values.dateOfBirth >= new Date().toISOString().slice(0, 10)) {
    errors.dateOfBirth = 'Geboortedatum moet in het verleden liggen.'
  }

  if (!isEditMode && !values.employmentStartDate) {
    errors.employmentStartDate = 'Datum in dienst is verplicht.'
  }

  return errors
}

export function toCreateEmployeeInput(values: EmployeeFormValues): CreateEmployeeInput {
  return {
    firstName: values.firstName.trim(),
    lastName: values.lastName.trim(),
    street: values.street.trim(),
    houseNumber: values.houseNumber.trim(),
    postalCode: values.postalCode.trim(),
    city: values.city.trim(),
    country: values.country.trim(),
    phoneNumber: values.phoneNumber.trim(),
    email: values.email.trim(),
    dateOfBirth: values.dateOfBirth,
    employmentStartDate: values.employmentStartDate,
    employmentStatus: values.employmentStatus,
    primaryFunction: values.primaryFunction,
    emergencyContactName: values.emergencyContactName.trim() || null,
    emergencyContactPhone: values.emergencyContactPhone.trim() || null,
    notes: values.notes.trim() || null,
  }
}

export function toUpdateEmployeeInput(values: EmployeeFormValues): UpdateEmployeeInput {
  return {
    firstName: values.firstName.trim(),
    lastName: values.lastName.trim(),
    street: values.street.trim(),
    houseNumber: values.houseNumber.trim(),
    postalCode: values.postalCode.trim(),
    city: values.city.trim(),
    country: values.country.trim(),
    phoneNumber: values.phoneNumber.trim(),
    email: values.email.trim(),
    dateOfBirth: values.dateOfBirth,
    employmentEndDate: values.employmentEndDate || null,
    employmentStatus: values.employmentStatus,
    primaryFunction: values.primaryFunction,
    emergencyContactName: values.emergencyContactName.trim() || null,
    emergencyContactPhone: values.emergencyContactPhone.trim() || null,
    notes: values.notes.trim() || null,
  }
}
