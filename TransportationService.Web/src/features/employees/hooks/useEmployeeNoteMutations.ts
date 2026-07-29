import { useRef, useState } from 'react'
import { describeApiError, type FieldErrors } from '../../../api/problemDetails'
import {
  createEmployeeNote,
  deleteEmployeeNote,
  pinEmployeeNote,
  unpinEmployeeNote,
  updateEmployeeNote,
  type EmployeeNote,
} from '../api/employeeNotesApi'

interface UseEmployeeNoteMutationsResult {
  isSubmitting: boolean
  error: string | null
  fieldErrors: FieldErrors
  create: (employeeId: string, text: string) => Promise<EmployeeNote | null>
  update: (employeeId: string, noteId: string, text: string) => Promise<EmployeeNote | null>
  remove: (employeeId: string, noteId: string) => Promise<boolean>
  setPinned: (employeeId: string, noteId: string, pinned: boolean) => Promise<EmployeeNote | null>
}

const GENERIC_ERROR = 'De actie kon niet worden uitgevoerd.'

export function useEmployeeNoteMutations(): UseEmployeeNoteMutationsResult {
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const isMounted = useRef(true)

  async function run<T>(action: () => Promise<T>, fallback: T): Promise<T> {
    setIsSubmitting(true)
    setError(null)
    setFieldErrors({})
    try {
      return await action()
    } catch (err) {
      if (isMounted.current) {
        const described = describeApiError(err, GENERIC_ERROR)
        setError(described.message)
        setFieldErrors(described.fieldErrors)
      }
      return fallback
    } finally {
      if (isMounted.current) setIsSubmitting(false)
    }
  }

  return {
    isSubmitting,
    error,
    fieldErrors,
    create: (employeeId, text) => run(() => createEmployeeNote(employeeId, text), null),
    update: (employeeId, noteId, text) => run(() => updateEmployeeNote(employeeId, noteId, text), null),
    remove: (employeeId, noteId) => run(async () => (await deleteEmployeeNote(employeeId, noteId), true), false),
    setPinned: (employeeId, noteId, pinned) =>
      run(() => (pinned ? pinEmployeeNote(employeeId, noteId) : unpinEmployeeNote(employeeId, noteId)), null),
  }
}
