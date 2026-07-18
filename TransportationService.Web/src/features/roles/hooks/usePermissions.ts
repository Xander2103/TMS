import { useEffect, useState } from 'react'
import { getPermissions } from '../api/rolesApi'
import type { Permission } from '../types/role'

interface UsePermissionsResult {
  permissions: Permission[]
  isLoading: boolean
  error: string | null
}

const LOAD_ERROR_MESSAGE = 'Rechten konden niet worden geladen.'

export function usePermissions(): UsePermissionsResult {
  const [permissions, setPermissions] = useState<Permission[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let isMounted = true

    getPermissions()
      .then((data) => {
        if (!isMounted) return
        setPermissions(data)
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
  }, [])

  return { permissions, isLoading, error }
}
