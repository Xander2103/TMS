import { useEffect, useState } from 'react'
import { getEmployeeQualifications } from '../api/qualificationsApi'
import type { EmployeeQualification } from '../types/qualification'

interface UseEmployeeQualificationsResult {
  qualifications: EmployeeQualification[]
  isLoading: boolean
  error: string | null
  reload: () => void
}

const LOAD_ERROR_MESSAGE = 'Kwalificaties konden niet worden geladen.'

export function useEmployeeQualifications(employeeId: string): UseEmployeeQualificationsResult {
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

  return { qualifications, isLoading, error, reload }
}
