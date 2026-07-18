import { useEffect, useState } from 'react'
import { getUser } from '../api/usersApi'
import type { User } from '../types/user'

interface UseUserResult {
  user: User | null
  isLoading: boolean
  error: string | null
  reload: () => void
}

const LOAD_ERROR_MESSAGE = 'Gebruiker kon niet worden geladen.'

export function useUser(id: string): UseUserResult {
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

  return { user, isLoading, error, reload }
}
