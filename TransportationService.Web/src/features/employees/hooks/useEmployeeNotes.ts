import { useEffect, useState } from 'react'
import { listEmployeeNotes, type EmployeeNote } from '../api/employeeNotesApi'

interface UseEmployeeNotesResult {
  notes: EmployeeNote[]
  isLoading: boolean
  error: string | null
  reload: () => void
}

const LOAD_ERROR_MESSAGE = 'Notities konden niet worden geladen.'

export function useEmployeeNotes(employeeId: string): UseEmployeeNotesResult {
  const [notes, setNotes] = useState<EmployeeNote[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isMounted = true

    listEmployeeNotes(employeeId)
      .then((data) => {
        if (!isMounted) return
        setNotes(data)
        setError(null)
        setIsLoading(false)
      })
      .catch(() => {
        if (!isMounted) return
        setError(LOAD_ERROR_MESSAGE)
        setIsLoading(false)
      })

    return () => {
      isMounted = false
    }
  }, [employeeId, reloadToken])

  function reload() {
    setReloadToken((token) => token + 1)
  }

  return { notes, isLoading, error, reload }
}
