import { useEffect, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { listEmployeeNotes, type EmployeeNote } from '../api/employeeNotesApi'

interface UseEmployeeNotesResult {
  notes: EmployeeNote[]
  isLoading: boolean
  error: string | null
  reload: () => void
}

const LOAD_ERROR_KEY = 'employees.errors.loadNotes'

export function useEmployeeNotes(employeeId: string): UseEmployeeNotesResult {
  const { t } = useLocale()
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
        setError(t(LOAD_ERROR_KEY))
        setIsLoading(false)
      })

    return () => {
      isMounted = false
    }
  }, [employeeId, reloadToken, t])

  function reload() {
    setReloadToken((token) => token + 1)
  }

  return { notes, isLoading, error, reload }
}
