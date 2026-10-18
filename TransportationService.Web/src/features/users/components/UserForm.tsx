import { useEffect, useState, type ChangeEvent, type FormEvent, type ReactNode } from 'react'
import { SearchableSelect, type SearchableSelectSearchFn } from '../../../components/ui/SearchableSelect'
import { getEmployee, searchEmployees } from '../../employees/api/employeesApi'
import { getCustomer, searchCustomers } from '../../customers/api/customersApi'
import { Button } from '../../../components/ui/Button'
import { getFieldError } from '../../../api/problemDetails'
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

const searchEmployeeOptions: SearchableSelectSearchFn = async (query) => {
  const page = await searchEmployees({ search: query || undefined, isActive: true, page: 1, pageSize: 20 })
  return page.items.map((employee) => ({
    value: employee.id,
    label: `${employee.firstName} ${employee.lastName}`,
    subtitle: employee.employeeNumber,
  }))
}

const searchCustomerOptions: SearchableSelectSearchFn = async (query) => {
  const page = await searchCustomers({ search: query || undefined, isActive: true, page: 1, pageSize: 20 })
  return page.items.map((customer) => ({ value: customer.id, label: customer.name, subtitle: customer.customerNumber }))
}

export function UserForm({ user, onSaved, secondaryAction }: UserFormProps) {
  const { t } = useLocale()
  const isEditMode = Boolean(user)
  const [values, setValues] = useState<UserFormValues>(() => valuesFromUser(user))
  const [errors, setErrors] = useState<UserFormErrors>({})
  const { isSubmitting, error, fieldErrors, create, update } = useUserMutations()
  const [emailEditedSinceSubmit, setEmailEditedSinceSubmit] = useState(false)
  // Labels for the ids the user already carries; later picks carry their own label.
  const [employeeLabel, setEmployeeLabel] = useState<string | undefined>(undefined)
  const [customerLabel, setCustomerLabel] = useState<string | undefined>(undefined)
  const linkedEmployeeId = user?.employeeId ?? null
  const linkedCustomerId = user?.customerId ?? null

  useEffect(() => {
    if (!linkedEmployeeId) return
    let active = true
    getEmployee(linkedEmployeeId)
      .then((employee) => {
        if (active) setEmployeeLabel(`${employee.firstName} ${employee.lastName} (${employee.employeeNumber})`)
      })
      .catch(() => {})
    return () => {
      active = false
    }
  }, [linkedEmployeeId])

  useEffect(() => {
    if (!linkedCustomerId) return
    let active = true
    getCustomer(linkedCustomerId)
      .then((customer) => {
        if (active) setCustomerLabel(`${customer.name} (${customer.customerNumber})`)
      })
      .catch(() => {})
    return () => {
      active = false
    }
  }, [linkedCustomerId])

  function setLinked(field: 'employeeId' | 'customerId', value: string | null) {
    setValues((previous) => ({ ...previous, [field]: value ?? '' }))
  }

  function handleChange(event: ChangeEvent<HTMLInputElement>) {
    const { value } = event.target
    const field = event.target.name as keyof UserFormValues

    setValues((previous) => ({ ...previous, [field]: value }))
    setErrors((previous) => ({ ...previous, [field]: undefined }))
    if (field === 'email') setEmailEditedSinceSubmit(true)
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

    setEmailEditedSinceSubmit(false)
    const saved = isEditMode && user
      ? await update(user.id, toUpdateUserInput(values))
      : await create(toCreateUserInput(values, []))

    if (saved) {
      onSaved(saved)
    }
  }

  // The server's duplicate-e-mail message ("Er bestaat al een gebruiker met dit e-mailadres …")
  // belongs under the e-mail field; it clears as soon as the address is edited.
  const serverEmailMessage = getFieldError(fieldErrors, 'email')
  const emailError = errors.email ?? (emailEditedSinceSubmit ? undefined : serverEmailMessage)

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
          aria-invalid={Boolean(emailError)}
          aria-describedby={emailError ? 'email-error' : undefined}
        />
        {emailError && (
          <p id="email-error" className="field-error">
            {emailError}
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

      {/* B46: link by searching a name, never by pasting an id. */}
      <div className="form-field">
        <label htmlFor="employeeId">{t('usersRoles.users.form.employeeId')}</label>
        <SearchableSelect
          id="employeeId"
          value={values.employeeId || null}
          onChange={(value) => setLinked('employeeId', value)}
          options={[]}
          selectedLabel={employeeLabel}
          onSearch={searchEmployeeOptions}
          placeholder={t('usersRoles.users.form.employeePlaceholder')}
          emptyMessage={t('usersRoles.users.form.noResults')}
        />
        <p className="field-hint">{t('usersRoles.users.form.employeeHint')}</p>
      </div>

      <div className="form-field">
        <label htmlFor="customerId">{t('usersRoles.users.form.customerId')}</label>
        <SearchableSelect
          id="customerId"
          value={values.customerId || null}
          onChange={(value) => setLinked('customerId', value)}
          options={[]}
          selectedLabel={customerLabel}
          onSearch={searchCustomerOptions}
          placeholder={t('usersRoles.users.form.customerPlaceholder')}
          emptyMessage={t('usersRoles.users.form.noResults')}
        />
        <p className="field-hint">{t('usersRoles.users.form.customerHint')}</p>
      </div>

      {error && error !== serverEmailMessage && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}

      <div className="form-actions">
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting
            ? t('usersRoles.users.form.saving')
            : isEditMode
              ? t('usersRoles.users.form.saveChanges')
              : t('usersRoles.users.form.create')}
        </Button>
        {secondaryAction}
      </div>
    </form>
  )
}
