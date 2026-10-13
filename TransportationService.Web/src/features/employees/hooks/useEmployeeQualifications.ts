import { useEffect, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { getEmployeeQualifications } from '../api/qualificationsApi'
import type { EmployeeQualification } from '../types/qualification'

interface UseEmployeeQualificationsResult {
  qualifications: EmployeeQualification[]
  isLoading: boolean
  error: string | null
  reload: () => void
}

const LOAD_ERROR_KEY = 'employees.errors.loadQualifications'

export function useEmployeeQualifications(employeeId: string): UseEmployeeQualificationsResult {
  const { t } = useLocale()
  const [qualifications, setQualifications] = useState<EmployeeQualification[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isMounted = true

    getEmployeeQualifications(employeeId)
      .then((data) => {
        if (!isMounted) return
        setQualifications(data)
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

  return { qualifications, isLoading, error, reload }
}
