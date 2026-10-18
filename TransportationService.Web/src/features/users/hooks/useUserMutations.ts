import { useEffect, useRef, useState } from 'react'
import { describeApiError, localizeApiError, type FieldErrors } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { assignUserRoles, createUser, setUserActive, setUserBlocked, updateUser } from '../api/usersApi'
import type { CreateUserInput, UpdateUserInput, User } from '../types/user'

interface UseUserMutationsResult {
  isSubmitting: boolean
  error: string | null
  /** Per-field backend validation messages of the last failed action (e.g. `email`). */
  fieldErrors: FieldErrors
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
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
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
    setFieldErrors({})

    try {
      const result = await action()
      if (isMountedRef.current) {
        setIsSubmitting(false)
      }
      return result
    } catch (err) {
      if (isMountedRef.current) {
        // The server's own (Dutch) message wins — e.g. the case-insensitive duplicate e-mail
        // ("Er bestaat al een gebruiker met dit e-mailadres …", field `email`). Only bodies without
        // a message (the last-administrator 409 is a bare string) fall back to our text.
        const status = (err as { status?: number }).status
        const fallback = status === 409 ? t('usersRoles.users.mutations.lastAdmin') : t('usersRoles.users.mutations.submitFailed')
        setError(localizeApiError(t, err, fallback))
        setFieldErrors(describeApiError(err, fallback).fieldErrors)
        setIsSubmitting(false)
      }
      return null
    }
  }

  return {
    isSubmitting,
    error,
    fieldErrors,
    create: (input) => run(() => createUser(input)),
    update: (id, input) => run(() => updateUser(id, input)),
    setActive: (id, isActive) => run(() => setUserActive(id, isActive)),
    setBlocked: (id, isBlocked) => run(() => setUserBlocked(id, isBlocked)),
    assignRoles: (id, roleIds) => run(() => assignUserRoles(id, roleIds)),
  }
}
