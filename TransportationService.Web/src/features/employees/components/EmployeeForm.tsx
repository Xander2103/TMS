import { useState, type ChangeEvent, type FormEvent, type ReactNode } from 'react'
import { useEmployeeMutations } from '../hooks/useEmployeeMutations'
import {
  toCreateEmployeeInput,
  toUpdateEmployeeInput,
  validateEmployeeForm,
  type EmployeeFormErrors,
  type EmployeeFormValues,
} from '../utils/employeeValidation'
import { EMPLOYEE_FUNCTION_LABELS, EMPLOYMENT_STATUS_LABELS, type EmployeeDetail, type EmployeeFunction, type EmploymentStatus } from '../types/employee'
import './EmployeeForm.css'

interface EmployeeFormProps {
  employee?: EmployeeDetail
  onSaved: (employee: EmployeeDetail) => void
  secondaryAction?: ReactNode
}

function valuesFromEmployee(employee?: EmployeeDetail): EmployeeFormValues {
  return {
    firstName: employee?.firstName ?? '',
    lastName: employee?.lastName ?? '',
    street: employee?.street ?? '',
    houseNumber: employee?.houseNumber ?? '',
    postalCode: employee?.postalCode ?? '',
    city: employee?.city ?? '',
    country: employee?.country ?? '',
    phoneNumber: employee?.phoneNumber ?? '',
    email: employee?.email ?? '',
    dateOfBirth: employee?.dateOfBirth ?? '',
    employmentStartDate: employee?.employmentStartDate ?? '',
    employmentEndDate: employee?.employmentEndDate ?? '',
    employmentStatus: employee?.employmentStatus ?? 'Active',
    primaryFunction: employee?.primaryFunction ?? 'DriverB',
    emergencyContactName: employee?.emergencyContactName ?? '',
    emergencyContactPhone: employee?.emergencyContactPhone ?? '',
    notes: employee?.notes ?? '',
  }
}

export function EmployeeForm({ employee, onSaved, secondaryAction }: EmployeeFormProps) {
  const isEditMode = Boolean(employee)
  const [values, setValues] = useState<EmployeeFormValues>(() => valuesFromEmployee(employee))
  const [errors, setErrors] = useState<EmployeeFormErrors>({})
  const { isSubmitting, error, create, update } = useEmployeeMutations()

  function handleChange(event: ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) {
    const { value } = event.target
    const field = event.target.name as keyof EmployeeFormValues

    setValues((previous) => ({ ...previous, [field]: value }))
    setErrors((previous) => ({ ...previous, [field]: undefined }))
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (isSubmitting) return

    const validationErrors = validateEmployeeForm(values, isEditMode)
    if (Object.keys(validationErrors).length > 0) {
      setErrors(validationErrors)
      return
    }

    const saved = isEditMode && employee
      ? await update(employee.id, toUpdateEmployeeInput(values))
      : await create(toCreateEmployeeInput(values))

    if (saved) {
      onSaved(saved)
    }
  }

  return (
    <form className="employee-form" onSubmit={handleSubmit} noValidate>
      <div className="form-field">
        <label htmlFor="firstName">Voornaam</label>
        <input id="firstName" name="firstName" type="text" required value={values.firstName} onChange={handleChange} aria-invalid={Boolean(errors.firstName)} />
        {errors.firstName && <p className="field-error">{errors.firstName}</p>}
      </div>

      <div className="form-field">
        <label htmlFor="lastName">Achternaam</label>
        <input id="lastName" name="lastName" type="text" required value={values.lastName} onChange={handleChange} aria-invalid={Boolean(errors.lastName)} />
        {errors.lastName && <p className="field-error">{errors.lastName}</p>}
      </div>

      <div className="form-field">
        <label htmlFor="street">Straat</label>
        <input id="street" name="street" type="text" required value={values.street} onChange={handleChange} aria-invalid={Boolean(errors.street)} />
        {errors.street && <p className="field-error">{errors.street}</p>}
      </div>

      <div className="form-field">
        <label htmlFor="houseNumber">Huisnummer</label>
        <input id="houseNumber" name="houseNumber" type="text" required value={values.houseNumber} onChange={handleChange} aria-invalid={Boolean(errors.houseNumber)} />
        {errors.houseNumber && <p className="field-error">{errors.houseNumber}</p>}
      </div>

      <div className="form-field">
        <label htmlFor="postalCode">Postcode</label>
        <input id="postalCode" name="postalCode" type="text" required value={values.postalCode} onChange={handleChange} aria-invalid={Boolean(errors.postalCode)} />
        {errors.postalCode && <p className="field-error">{errors.postalCode}</p>}
      </div>

      <div className="form-field">
        <label htmlFor="city">Plaats</label>
        <input id="city" name="city" type="text" required value={values.city} onChange={handleChange} aria-invalid={Boolean(errors.city)} />
        {errors.city && <p className="field-error">{errors.city}</p>}
      </div>

      <div className="form-field">
        <label htmlFor="country">Land</label>
        <input id="country" name="country" type="text" required value={values.country} onChange={handleChange} aria-invalid={Boolean(errors.country)} />
        {errors.country && <p className="field-error">{errors.country}</p>}
      </div>

      <div className="form-field">
        <label htmlFor="phoneNumber">Telefoonnummer</label>
        <input id="phoneNumber" name="phoneNumber" type="tel" required value={values.phoneNumber} onChange={handleChange} aria-invalid={Boolean(errors.phoneNumber)} />
        {errors.phoneNumber && <p className="field-error">{errors.phoneNumber}</p>}
      </div>

      <div className="form-field">
        <label htmlFor="email">E-mail</label>
        <input id="email" name="email" type="email" required value={values.email} onChange={handleChange} aria-invalid={Boolean(errors.email)} />
        {errors.email && <p className="field-error">{errors.email}</p>}
      </div>

      <div className="form-field">
        <label htmlFor="dateOfBirth">Geboortedatum</label>
        <input id="dateOfBirth" name="dateOfBirth" type="date" required value={values.dateOfBirth} onChange={handleChange} aria-invalid={Boolean(errors.dateOfBirth)} />
        {errors.dateOfBirth && <p className="field-error">{errors.dateOfBirth}</p>}
      </div>

      {isEditMode ? (
        <div className="form-field">
          <label htmlFor="employmentEndDate">Datum uit dienst</label>
          <input id="employmentEndDate" name="employmentEndDate" type="date" value={values.employmentEndDate} onChange={handleChange} />
        </div>
      ) : (
        <div className="form-field">
          <label htmlFor="employmentStartDate">Datum in dienst</label>
          <input id="employmentStartDate" name="employmentStartDate" type="date" required value={values.employmentStartDate} onChange={handleChange} aria-invalid={Boolean(errors.employmentStartDate)} />
          {errors.employmentStartDate && <p className="field-error">{errors.employmentStartDate}</p>}
        </div>
      )}

      <div className="form-field">
        <label htmlFor="employmentStatus">Dienstverbandstatus</label>
        <select id="employmentStatus" name="employmentStatus" value={values.employmentStatus} onChange={handleChange}>
          {(Object.keys(EMPLOYMENT_STATUS_LABELS) as EmploymentStatus[]).map((status) => (
            <option key={status} value={status}>
              {EMPLOYMENT_STATUS_LABELS[status]}
            </option>
          ))}
        </select>
      </div>

      <div className="form-field">
        <label htmlFor="primaryFunction">Functie</label>
        <select id="primaryFunction" name="primaryFunction" value={values.primaryFunction} onChange={handleChange}>
          {(Object.keys(EMPLOYEE_FUNCTION_LABELS) as EmployeeFunction[]).map((fn) => (
            <option key={fn} value={fn}>
              {EMPLOYEE_FUNCTION_LABELS[fn]}
            </option>
          ))}
        </select>
      </div>

      <div className="form-field">
        <label htmlFor="emergencyContactName">Noodcontact naam</label>
        <input id="emergencyContactName" name="emergencyContactName" type="text" value={values.emergencyContactName} onChange={handleChange} />
      </div>

      <div className="form-field">
        <label htmlFor="emergencyContactPhone">Noodcontact telefoon</label>
        <input id="emergencyContactPhone" name="emergencyContactPhone" type="tel" value={values.emergencyContactPhone} onChange={handleChange} />
      </div>

      <div className="form-field">
        <label htmlFor="notes">Notities</label>
        <textarea id="notes" name="notes" rows={3} value={values.notes} onChange={handleChange} />
      </div>

      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}

      <div className="form-actions">
        <button type="submit" className="primary-button" disabled={isSubmitting}>
          {isSubmitting ? 'Opslaan...' : isEditMode ? 'Wijzigingen opslaan' : 'Medewerker aanmaken'}
        </button>
        {secondaryAction}
      </div>
    </form>
  )
}
