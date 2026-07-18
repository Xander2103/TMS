import { useEffect, useRef, useState } from 'react'
import { assignUserRoles, createUser, setUserActive, setUserBlocked, updateUser } from '../api/usersApi'
import type { CreateUserInput, UpdateUserInput, User } from '../types/user'

const SUBMIT_ERROR_MESSAGE = 'De actie kon niet worden uitgevoerd. Probeer het opnieuw.'
const LAST_ADMIN_ERROR_MESSAGE = 'Dit is de laatste actieve beheerder en kan niet worden gewijzigd.'

interface UseUserMutationsResult {
  isSubmitting: boolean
  error: string | null
  create: (input: CreateUserInput) => Promise<User | null>
  update: (id: string, input: UpdateUserInput) => Promise<User | null>
  setActive: (id: string, isActive: boolean) => Promise<User | null>
  setBlocked: (id: string, isBlocked: boolean) => Promise<User | null>
  assignRoles: (id: string, roleIds: string[]) => Promise<User | null>
}

export function useUserMutations(): UseUserMutationsResult {
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const isMountedRef = useRef(true)

  useEffect(() => {
    isMountedRef.current = true
    return () => {
      isMountedRef.current = false
    }
  }, [])

  async function run<T>(action: () => Promise<T>): Promise<T | null> {
    setIsSubmitting(true)
    setError(null)

    try {
      const result = await action()
      if (isMountedRef.current) {
        setIsSubmitting(false)
      }
      return result
    } catch (err) {
      if (isMountedRef.current) {
        const status = (err as { status?: number }).status
        setError(status === 409 ? LAST_ADMIN_ERROR_MESSAGE : SUBMIT_ERROR_MESSAGE)
        setIsSubmitting(false)
      }
      return null
    }
  }

  return {
    isSubmitting,
    error,
    create: (input) => run(() => createUser(input)),
    update: (id, input) => run(() => updateUser(id, input)),
    setActive: (id, isActive) => run(() => setUserActive(id, isActive)),
    setBlocked: (id, isBlocked) => run(() => setUserBlocked(id, isBlocked)),
    assignRoles: (id, roleIds) => run(() => assignUserRoles(id, roleIds)),
  }
}
