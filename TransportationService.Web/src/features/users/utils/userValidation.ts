import type { CreateUserInput, UpdateUserInput } from '../types/user'

export interface UserFormValues {
  email: string
  firstName: string
  lastName: string
  employeeId: string
  customerId: string
}

export type UserFormErrors = Partial<Record<keyof UserFormValues, string>>

const MAX_LENGTHS = {
  email: 250,
  firstName: 100,
  lastName: 100,
} as const

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function validateUserForm(values: UserFormValues): UserFormErrors {
  const errors: UserFormErrors = {}

  const email = values.email.trim()
  const firstName = values.firstName.trim()
  const lastName = values.lastName.trim()

  if (!email) {
    errors.email = 'E-mail is verplicht.'
  } else if (email.length > MAX_LENGTHS.email) {
    errors.email = `E-mail mag maximaal ${MAX_LENGTHS.email} tekens bevatten.`
  } else if (!EMAIL_PATTERN.test(email)) {
    errors.email = 'Ongeldig e-mailadres.'
  }

  if (!firstName) {
    errors.firstName = 'Voornaam is verplicht.'
  } else if (firstName.length > MAX_LENGTHS.firstName) {
    errors.firstName = `Voornaam mag maximaal ${MAX_LENGTHS.firstName} tekens bevatten.`
  }

  if (!lastName) {
    errors.lastName = 'Achternaam is verplicht.'
  } else if (lastName.length > MAX_LENGTHS.lastName) {
    errors.lastName = `Achternaam mag maximaal ${MAX_LENGTHS.lastName} tekens bevatten.`
  }

  return errors
}

export function toCreateUserInput(values: UserFormValues, roleIds: string[]): CreateUserInput {
  return {
    email: values.email.trim(),
    firstName: values.firstName.trim(),
    lastName: values.lastName.trim(),
    employeeId: values.employeeId.trim() || null,
    customerId: values.customerId.trim() || null,
    roleIds,
  }
}

export function toUpdateUserInput(values: UserFormValues): UpdateUserInput {
  return {
    firstName: values.firstName.trim(),
    lastName: values.lastName.trim(),
    employeeId: values.employeeId.trim() || null,
    customerId: values.customerId.trim() || null,
  }
}
