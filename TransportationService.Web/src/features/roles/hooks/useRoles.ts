import { useEffect, useState } from 'react'
import { getRoles } from '../api/rolesApi'
import type { Role } from '../types/role'

interface UseRolesResult {
  roles: Role[]
  isLoading: boolean
  error: string | null
}

const LOAD_ERROR_MESSAGE = 'Rollen konden niet worden geladen.'

export function useRoles(): UseRolesResult {
  const [roles, setRoles] = useState<Role[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let isMounted = true

    getRoles()
      .then((data) => {
        if (!isMounted) return
        setRoles(data)
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

  return { roles, isLoading, error }
}
