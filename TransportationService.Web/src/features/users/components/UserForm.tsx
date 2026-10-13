import { useState, type ChangeEvent, type FormEvent, type ReactNode } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { useUserMutations } from '../hooks/useUserMutations'
import {
  toCreateUserInput,
  toUpdateUserInput,
  validateUserForm,
  type UserFormErrors,
  type UserFormValues,
} from '../utils/userValidation'
import type { User } from '../types/user'
import './UserForm.css'

interface UserFormProps {
  user?: User
  onSaved: (user: User) => void
  secondaryAction?: ReactNode
}

function valuesFromUser(user?: User): UserFormValues {
  return {
    email: user?.email ?? '',
    firstName: user?.firstName ?? '',
    lastName: user?.lastName ?? '',
    employeeId: user?.employeeId ?? '',
    customerId: user?.customerId ?? '',
  }
}

export function UserForm({ user, onSaved, secondaryAction }: UserFormProps) {
  const { t } = useLocale()
  const isEditMode = Boolean(user)
  const [values, setValues] = useState<UserFormValues>(() => valuesFromUser(user))
  const [errors, setErrors] = useState<UserFormErrors>({})
  const { isSubmitting, error, create, update } = useUserMutations()

  function handleChange(event: ChangeEvent<HTMLInputElement>) {
    const { value } = event.target
    const field = event.target.name as keyof UserFormValues

    setValues((previous) => ({ ...previous, [field]: value }))
    setErrors((previous) => ({ ...previous, [field]: undefined }))
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (isSubmitting) {
      return
    }

    const validationErrors = validateUserForm(t, values)
    if (Object.keys(validationErrors).length > 0) {
      setErrors(validationErrors)
      return
    }

    const saved = isEditMode && user
      ? await update(user.id, toUpdateUserInput(values))
      : await create(toCreateUserInput(values, []))

    if (saved) {
      onSaved(saved)
    }
  }

  return (
    <form className="user-form" onSubmit={handleSubmit} noValidate>
      <div className="form-field">
        <label htmlFor="email">{t('usersRoles.users.form.email')}</label>
        <input
          id="email"
          name="email"
          type="email"
          autoComplete="email"
          required
          maxLength={250}
          value={values.email}
          onChange={handleChange}
          disabled={isEditMode}
          aria-invalid={Boolean(errors.email)}
          aria-describedby={errors.email ? 'email-error' : undefined}
        />
        {errors.email && (
          <p id="email-error" className="field-error">
            {errors.email}
          </p>
        )}
      </div>

      <div className="form-field">
        <label htmlFor="firstName">{t('usersRoles.users.form.firstName')}</label>
        <input
          id="firstName"
          name="firstName"
          type="text"
          autoComplete="given-name"
          required
          maxLength={100}
          value={values.firstName}
          onChange={handleChange}
          aria-invalid={Boolean(errors.firstName)}
          aria-describedby={errors.firstName ? 'firstName-error' : undefined}
        />
        {errors.firstName && (
          <p id="firstName-error" className="field-error">
            {errors.firstName}
          </p>
        )}
      </div>

      <div className="form-field">
        <label htmlFor="lastName">{t('usersRoles.users.form.lastName')}</label>
        <input
          id="lastName"
          name="lastName"
          type="text"
          autoComplete="family-name"
          required
          maxLength={100}
          value={values.lastName}
          onChange={handleChange}
          aria-invalid={Boolean(errors.lastName)}
          aria-describedby={errors.lastName ? 'lastName-error' : undefined}
        />
        {errors.lastName && (
          <p id="lastName-error" className="field-error">
            {errors.lastName}
          </p>
        )}
      </div>

      <div className="form-field">
        <label htmlFor="employeeId">{t('usersRoles.users.form.employeeId')}</label>
        <input
          id="employeeId"
          name="employeeId"
          type="text"
          autoComplete="off"
          value={values.employeeId}
          onChange={handleChange}
        />
      </div>

      <div className="form-field">
        <label htmlFor="customerId">{t('usersRoles.users.form.customerId')}</label>
        <input
          id="customerId"
          name="customerId"
          type="text"
          autoComplete="off"
          value={values.customerId}
          onChange={handleChange}
        />
      </div>

      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}

      <div className="form-actions">
        <button type="submit" className="primary-button" disabled={isSubmitting}>
          {isSubmitting
            ? t('usersRoles.users.form.saving')
            : isEditMode
              ? t('usersRoles.users.form.saveChanges')
              : t('usersRoles.users.form.create')}
        </button>
        {secondaryAction}
      </div>
    </form>
  )
}
