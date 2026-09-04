import { useEffect, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { getEmployee } from '../api/employeesApi'
import type { EmployeeDetail } from '../types/employee'

interface UseEmployeeResult {
  employee: EmployeeDetail | null
  isLoading: boolean
  error: string | null
  reload: () => void
}

const LOAD_ERROR_KEY = 'employees.errors.loadEmployee'

export function useEmployee(id: string): UseEmployeeResult {
  const { t } = useLocale()
  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isMounted = true

    getEmployee(id)
      .then((data) => {
        if (!isMounted) return
        setEmployee(data)
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
  }, [id, reloadToken, t])

  function reload() {
    setReloadToken((token) => token + 1)
  }

  return { employee, isLoading, error, reload }
}
