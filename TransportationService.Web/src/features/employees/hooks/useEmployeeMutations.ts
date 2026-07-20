import { useEffect, useRef, useState } from 'react'
import { ApiError } from '../../../api/apiClient'
import { describeApiError, type FieldErrors } from '../../../api/problemDetails'
import { createEmployee, deactivateEmployee, reactivateEmployee, updateEmployee } from '../api/employeesApi'
import type { CreateEmployeeInput, EmployeeDetail, UpdateEmployeeInput } from '../types/employee'

const SUBMIT_ERROR_MESSAGE = 'De actie kon niet worden uitgevoerd. Probeer het opnieuw.'

interface UseEmployeeMutationsResult {
  isSubmitting: boolean
  error: string | null
  /** Per-field backend validation messages of the last failed action. */
  fieldErrors: FieldErrors
  create: (input: CreateEmployeeInput) => Promise<EmployeeDetail | null>
  update: (id: string, input: UpdateEmployeeInput) => Promise<EmployeeDetail | null>
  deactivate: (id: string) => Promise<boolean>
  reactivate: (id: string) => Promise<boolean>
}

export function useEmployeeMutations(): UseEmployeeMutationsResult {
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const isMountedRef = useRef(true)

  useEffect(() => {
    isMountedRef.current = true
    return () => {
      isMountedRef.current = false
    }
  }, [])

  async function run<T>(action: () => Promise<T>, fallback: T): Promise<T> {
    setIsSubmitting(true)
    setError(null)
    setFieldErrors({})

    try {
      const result = await action()
      if (isMountedRef.current) {
        setIsSubmitting(false)
      }
      return result
    } catch (err) {
      if (isMountedRef.current) {
        // Backend validation messages (400) are user-facing Dutch text; show them directly.
        const described = describeApiError(err, SUBMIT_ERROR_MESSAGE)
        setError(err instanceof ApiError && err.status === 400 ? described.message : SUBMIT_ERROR_MESSAGE)
        setFieldErrors(described.fieldErrors)
        setIsSubmitting(false)
      }
      return fallback
    }
  }

  return {
    isSubmitting,
    error,
    fieldErrors,
    create: (input) => run(() => createEmployee(input), null),
    update: (id, input) => run(() => updateEmployee(id, input), null),
    deactivate: (id) => run(async () => { await deactivateEmployee(id); return true }, false),
    reactivate: (id) => run(async () => { await reactivateEmployee(id); return true }, false),
  }
}
