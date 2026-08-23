import { useEffect, useRef, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { ApiError } from '../../../api/apiClient'
import {
  createEmployeeQualification,
  suspendQualification,
  updateEmployeeQualification,
  verifyQualification,
} from '../api/qualificationsApi'
import type { CreateEmployeeQualificationInput, EmployeeQualification, UpdateEmployeeQualificationInput } from '../types/qualification'

const SUBMIT_ERROR_KEY = 'employees.errors.actionFailedRetry'

interface UseQualificationMutationsResult {
  isSubmitting: boolean
  error: string | null
  create: (employeeId: string, input: CreateEmployeeQualificationInput) => Promise<EmployeeQualification | null>
  update: (employeeId: string, id: string, input: UpdateEmployeeQualificationInput) => Promise<EmployeeQualification | null>
  verify: (employeeId: string, id: string) => Promise<EmployeeQualification | null>
  suspend: (employeeId: string, id: string) => Promise<EmployeeQualification | null>
}

export function useQualificationMutations(): UseQualificationMutationsResult {
  const { t } = useLocale()
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const isMountedRef = useRef(true)

  useEffect(() => {
    isMountedRef.current = true
    return () => {
      isMountedRef.current = false
    }
  }, [])

  async function run(action: () => Promise<EmployeeQualification>): Promise<EmployeeQualification | null> {
    setIsSubmitting(true)
    setError(null)

    try {
      const result = await action()
      if (isMountedRef.current) {
        setIsSubmitting(false)
      }
      return result
    } catch (err) {
      if (isMountedRef.current) {
        setError(err instanceof ApiError && err.status === 400 ? err.message : t(SUBMIT_ERROR_KEY))
        setIsSubmitting(false)
      }
      return null
    }
  }

  return {
    isSubmitting,
    error,
    create: (employeeId, input) => run(() => createEmployeeQualification(employeeId, input)),
    update: (employeeId, id, input) => run(() => updateEmployeeQualification(employeeId, id, input)),
    verify: (employeeId, id) => run(() => verifyQualification(employeeId, id)),
    suspend: (employeeId, id) => run(() => suspendQualification(employeeId, id)),
  }
}
