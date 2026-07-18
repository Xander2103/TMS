import { useEffect, useState } from 'react'
import { getUsers } from '../api/usersApi'
import type { User } from '../types/user'

interface UseUsersResult {
  users: User[]
  isLoading: boolean
  error: string | null
}

const LOAD_ERROR_MESSAGE = 'Gebruikers konden niet worden geladen.'

export function useUsers(): UseUsersResult {
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
        setError(LOAD_ERROR_MESSAGE)
        setIsLoading(false)
      })

    return () => {
      isMounted = false
    }
  }, [])

  return { users, isLoading, error }
}
