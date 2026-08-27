import { useEffect, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { getPermissions } from '../api/rolesApi'
import type { Permission } from '../types/role'

interface UsePermissionsResult {
  permissions: Permission[]
  isLoading: boolean
  error: string | null
}

export function usePermissions(): UsePermissionsResult {
  const { t } = useLocale()
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
        setError(t('usersRoles.roles.matrix.loadFailed'))
        setIsLoading(false)
      })

    return () => {
      isMounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return { permissions, isLoading, error }
}
