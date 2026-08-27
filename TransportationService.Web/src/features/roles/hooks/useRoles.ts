import { useEffect, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { getRoles } from '../api/rolesApi'
import type { Role } from '../types/role'

interface UseRolesResult {
  roles: Role[]
  isLoading: boolean
  error: string | null
  reload: () => void
}

export function useRoles(): UseRolesResult {
  const { t } = useLocale()
  const [roles, setRoles] = useState<Role[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isMounted = true

    getRoles()
      .then((data) => {
        if (!isMounted) return
        setRoles(data)
        setError(null)
        setIsLoading(false)
      })
      .catch(() => {
        if (!isMounted) return
        setError(t('usersRoles.roles.page.loadFailed'))
        setIsLoading(false)
      })

    return () => {
      isMounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reloadToken])

  function reload() {
    setReloadToken((token) => token + 1)
  }

  return { roles, isLoading, error, reload }
}
