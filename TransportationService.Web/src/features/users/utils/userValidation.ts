import type { TranslateFn } from '../../../i18n/localeContext'
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

export function validateUserForm(t: TranslateFn, values: UserFormValues): UserFormErrors {
  const errors: UserFormErrors = {}

  const email = values.email.trim()
  const firstName = values.firstName.trim()
  const lastName = values.lastName.trim()

  if (!email) {
    errors.email = t('usersRoles.users.validation.emailRequired')
  } else if (email.length > MAX_LENGTHS.email) {
    errors.email = t('usersRoles.users.validation.emailMax', { max: MAX_LENGTHS.email })
  } else if (!EMAIL_PATTERN.test(email)) {
    errors.email = t('usersRoles.users.validation.emailInvalid')
  }

  if (!firstName) {
    errors.firstName = t('usersRoles.users.validation.firstNameRequired')
  } else if (firstName.length > MAX_LENGTHS.firstName) {
    errors.firstName = t('usersRoles.users.validation.firstNameMax', { max: MAX_LENGTHS.firstName })
  }

  if (!lastName) {
    errors.lastName = t('usersRoles.users.validation.lastNameRequired')
  } else if (lastName.length > MAX_LENGTHS.lastName) {
    errors.lastName = t('usersRoles.users.validation.lastNameMax', { max: MAX_LENGTHS.lastName })
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
