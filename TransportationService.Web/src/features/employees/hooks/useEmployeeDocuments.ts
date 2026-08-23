import { useEffect, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { describeApiError } from '../../../api/problemDetails'
import { listEmployeeDocuments, type EmployeeDocument } from '../api/employeeDocumentsApi'

interface UseEmployeeDocumentsResult {
  documents: EmployeeDocument[]
  isLoading: boolean
  error: string | null
  reload: () => void
}

const LOAD_ERROR_KEY = 'employees.errors.loadDocuments'

export function useEmployeeDocuments(employeeId: string, includeArchived: boolean): UseEmployeeDocumentsResult {
  const { t } = useLocale()
  const [documents, setDocuments] = useState<EmployeeDocument[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isMounted = true

    listEmployeeDocuments(employeeId, includeArchived)
      .then((data) => {
        if (!isMounted) return
        setDocuments(data)
        setError(null)
        setIsLoading(false)
      })
      .catch((err) => {
        if (!isMounted) return
        setError(describeApiError(err, t(LOAD_ERROR_KEY)).message)
        setIsLoading(false)
      })

    return () => {
      isMounted = false
    }
  }, [employeeId, includeArchived, reloadToken])

  function reload() {
    setReloadToken((token) => token + 1)
  }

  return { documents, isLoading, error, reload }
}
