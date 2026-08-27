import { useEffect, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { getUser } from '../api/usersApi'
import type { User } from '../types/user'

interface UseUserResult {
  user: User | null
  isLoading: boolean
  error: string | null
  reload: () => void
}

export function useUser(id: string): UseUserResult {
  const { t } = useLocale()
  const [user, setUser] = useState<User | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let isMounted = true

    getUser(id)
      .then((data) => {
        if (!isMounted) return
        setUser(data)
        setError(null)
        setIsLoading(false)
      })
      .catch(() => {
        if (!isMounted) return
        setError(t('usersRoles.users.detail.loadFailed'))
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

  return { user, isLoading, error, reload }
}
