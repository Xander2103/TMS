import { useEffect, useRef, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { assignRolePermissions, createRole, deactivateRole, updateRole } from '../api/rolesApi'
import type { CreateRoleInput, Role, UpdateRoleInput } from '../types/role'

interface UseRoleMutationsResult {
  isSubmitting: boolean
  error: string | null
  create: (input: CreateRoleInput) => Promise<Role | null>
  update: (id: string, input: UpdateRoleInput) => Promise<Role | null>
  deactivate: (id: string) => Promise<Role | null>
  assignPermissions: (id: string, permissionCodes: string[]) => Promise<Role | null>
}

export function useRoleMutations(): UseRoleMutationsResult {
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
        setError(
          status === 409 ? t('usersRoles.roles.mutations.systemRoleConflict') : t('usersRoles.roles.mutations.submitFailed'),
        )
        setIsSubmitting(false)
      }
      return null
    }
  }

  return {
    isSubmitting,
    error,
    create: (input) => run(() => createRole(input)),
    update: (id, input) => run(() => updateRole(id, input)),
    deactivate: (id) => run(() => deactivateRole(id)),
    assignPermissions: (id, permissionCodes) => run(() => assignRolePermissions(id, permissionCodes)),
  }
}
