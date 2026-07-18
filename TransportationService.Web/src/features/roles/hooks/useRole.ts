import { useEffect, useState } from 'react'
import { getRole } from '../api/rolesApi'
import type { Role } from '../types/role'

interface UseRoleResult {
  role: Role | null
  isLoading: boolean
  error: string | null
  reload: () => void
}

const LOAD_ERROR_MESSAGE = 'Rol kon niet worden geladen.'

export function useRole(id: string): UseRoleResult {
  const [role, setRole] = useState<Role | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isMounted = true

    getRole(id)
      .then((data) => {
        if (!isMounted) return
        setRole(data)
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

  return { role, isLoading, error, reload }
}
