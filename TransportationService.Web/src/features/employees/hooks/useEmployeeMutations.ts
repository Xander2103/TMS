import { useEffect, useRef, useState } from 'react'
import { createEmployee, deactivateEmployee, updateEmployee } from '../api/employeesApi'
import type { CreateEmployeeInput, EmployeeDetail, UpdateEmployeeInput } from '../types/employee'

const SUBMIT_ERROR_MESSAGE = 'De actie kon niet worden uitgevoerd. Probeer het opnieuw.'

interface UseEmployeeMutationsResult {
  isSubmitting: boolean
  error: string | null
  create: (input: CreateEmployeeInput) => Promise<EmployeeDetail | null>
  update: (id: string, input: UpdateEmployeeInput) => Promise<EmployeeDetail | null>
  deactivate: (id: string) => Promise<boolean>
}

export function useEmployeeMutations(): UseEmployeeMutationsResult {
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
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

    try {
      const result = await action()
      if (isMountedRef.current) {
        setIsSubmitting(false)
      }
      return result
    } catch {
      if (isMountedRef.current) {
        setError(SUBMIT_ERROR_MESSAGE)
        setIsSubmitting(false)
      }
      return fallback
    }
  }

  return {
    isSubmitting,
    error,
    create: (input) => run(() => createEmployee(input), null),
    update: (id, input) => run(() => updateEmployee(id, input), null),
    deactivate: (id) => run(async () => { await deactivateEmployee(id); return true }, false),
  }
}
