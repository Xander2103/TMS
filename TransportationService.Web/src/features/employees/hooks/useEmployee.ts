import { useEffect, useState } from 'react'
import { getEmployee } from '../api/employeesApi'
import type { EmployeeDetail } from '../types/employee'

interface UseEmployeeResult {
  employee: EmployeeDetail | null
  isLoading: boolean
  error: string | null
  reload: () => void
}

const LOAD_ERROR_MESSAGE = 'Medewerker kon niet worden geladen.'

export function useEmployee(id: string): UseEmployeeResult {
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
        setError(LOAD_ERROR_MESSAGE)
        setIsLoading(false)
      })

    return () => {
      isMounted = false
    }
  }, [id, reloadToken])

  function reload() {
    setReloadToken((token) => token + 1)
  }

  return { employee, isLoading, error, reload }
}
