import { useEffect, useRef, useState } from 'react'
import {
  createEmployeeQualification,
  suspendQualification,
  updateEmployeeQualification,
  verifyQualification,
} from '../api/qualificationsApi'
import type { CreateEmployeeQualificationInput, EmployeeQualification, UpdateEmployeeQualificationInput } from '../types/qualification'

const SUBMIT_ERROR_MESSAGE = 'De actie kon niet worden uitgevoerd. Probeer het opnieuw.'

interface UseQualificationMutationsResult {
  isSubmitting: boolean
  error: string | null
  create: (employeeId: string, input: CreateEmployeeQualificationInput) => Promise<EmployeeQualification | null>
  update: (employeeId: string, id: string, input: UpdateEmployeeQualificationInput) => Promise<EmployeeQualification | null>
  verify: (employeeId: string, id: string) => Promise<EmployeeQualification | null>
  suspend: (employeeId: string, id: string) => Promise<EmployeeQualification | null>
}

export function useQualificationMutations(): UseQualificationMutationsResult {
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
    } catch {
      if (isMountedRef.current) {
        setError(SUBMIT_ERROR_MESSAGE)
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
