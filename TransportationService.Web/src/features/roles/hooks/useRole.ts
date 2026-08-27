import { useEffect, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { getRole } from '../api/rolesApi'
import type { Role } from '../types/role'

interface UseRoleResult {
  role: Role | null
  isLoading: boolean
  error: string | null
  reload: () => void
}

export function useRole(id: string): UseRoleResult {
  const { t } = useLocale()
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
        setError(t('usersRoles.roles.detail.loadFailed'))
        setIsLoading(false)
      })

    return () => {
      isMounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, reloadToken])

  function reload() {
    setReloadToken((token) => token + 1)
  }

  return { role, isLoading, error, reload }
}
