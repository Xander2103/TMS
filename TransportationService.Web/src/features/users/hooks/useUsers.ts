import { useEffect, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { getUsers } from '../api/usersApi'
import type { User } from '../types/user'

interface UseUsersResult {
  users: User[]
  isLoading: boolean
  error: string | null
}

export function useUsers(): UseUsersResult {
  const { t } = useLocale()
  const [users, setUsers] = useState<User[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let isMounted = true

    getUsers()
      .then((data) => {
        if (!isMounted) return
        setUsers(data)
        setIsLoading(false)
      })
      .catch(() => {
        if (!isMounted) return
        setError(t('usersRoles.users.page.loadFailed'))
        setIsLoading(false)
      })

    return () => {
      isMounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return { users, isLoading, error }
}
