import { useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { useRoles } from '../../roles/hooks/useRoles'
import { useUserMutations } from '../hooks/useUserMutations'
import type { User } from '../types/user'
import './RoleAssignmentPanel.css'

interface RoleAssignmentPanelProps {
  user: User
  onSaved: (user: User) => void
}

/**
 * Keyed by `user.id`/`user.roles` in the parent (see UserDetailPage) so React
 * remounts this component — and re-derives selectedRoleIds from props — instead
 * of syncing local state from a prop via an effect.
 */
export function RoleAssignmentPanel({ user, onSaved }: RoleAssignmentPanelProps) {
  const { t } = useLocale()
  const { roles, isLoading, error: loadError } = useRoles()
  const { isSubmitting, error: saveError, assignRoles } = useUserMutations()
  const [selectedRoleIds, setSelectedRoleIds] = useState<string[]>(() => user.roles.map((role) => role.id))

  function toggleRole(roleId: string) {
    setSelectedRoleIds((previous) =>
      previous.includes(roleId) ? previous.filter((id) => id !== roleId) : [...previous, roleId],
    )
  }

  async function handleSave() {
    if (isSubmitting) return
    const saved = await assignRoles(user.id, selectedRoleIds)
    if (saved) {
      onSaved(saved)
    }
  }

  if (isLoading) {
    return <p>{t('usersRoles.users.rolesPanel.loading')}</p>
  }

  if (loadError) {
    return <p className="error-text">{loadError}</p>
  }

  return (
    <div className="role-assignment-panel">
      <ul className="role-checkbox-list">
        {roles.map((role) => (
          <li key={role.id}>
            <label>
              <input
                type="checkbox"
                checked={selectedRoleIds.includes(role.id)}
                onChange={() => toggleRole(role.id)}
                disabled={isSubmitting}
              />
              {role.name}
              {role.isSystemRole && <span className="badge">{t('usersRoles.users.rolesPanel.systemRole')}</span>}
            </label>
          </li>
        ))}
      </ul>
      {saveError && (
        <p className="form-error" role="alert">
          {saveError}
        </p>
      )}
      <button type="button" className="primary-button" onClick={handleSave} disabled={isSubmitting}>
        {isSubmitting ? t('usersRoles.users.rolesPanel.saving') : t('usersRoles.users.rolesPanel.save')}
      </button>
    </div>
  )
}
