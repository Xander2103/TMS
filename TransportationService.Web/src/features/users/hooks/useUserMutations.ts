import { useEffect, useRef, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { assignUserRoles, createUser, setUserActive, setUserBlocked, updateUser } from '../api/usersApi'
import type { CreateUserInput, UpdateUserInput, User } from '../types/user'

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
  const { t } = useLocale()
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
        setError(status === 409 ? t('usersRoles.users.mutations.lastAdmin') : t('usersRoles.users.mutations.submitFailed'))
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
