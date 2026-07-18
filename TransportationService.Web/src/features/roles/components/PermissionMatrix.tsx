import { useState } from 'react'
import { usePermissions } from '../hooks/usePermissions'
import { useRoleMutations } from '../hooks/useRoleMutations'
import type { Role } from '../types/role'
import './PermissionMatrix.css'

interface PermissionMatrixProps {
  role: Role
  onSaved: (role: Role) => void
}

/**
 * Keyed by `role.permissionCodes` in the parent (see RoleDetailPage) so React
 * remounts this component — and re-derives checkedCodes from props — instead
 * of syncing local state from a prop via an effect.
 */
export function PermissionMatrix({ role, onSaved }: PermissionMatrixProps) {
  const { permissions, isLoading, error: loadError } = usePermissions()
  const { isSubmitting, error: saveError, assignPermissions } = useRoleMutations()
  const [checkedCodes, setCheckedCodes] = useState<string[]>(() => role.permissionCodes)

  function toggleCode(code: string) {
    setCheckedCodes((previous) => (previous.includes(code) ? previous.filter((c) => c !== code) : [...previous, code]))
  }

  function toggleModule(moduleCodes: string[], allChecked: boolean) {
    setCheckedCodes((previous) =>
      allChecked ? previous.filter((code) => !moduleCodes.includes(code)) : [...new Set([...previous, ...moduleCodes])],
    )
  }

  async function handleSave() {
    if (isSubmitting) return
    const saved = await assignPermissions(role.id, checkedCodes)
    if (saved) {
      onSaved(saved)
    }
  }

  if (isLoading) {
    return <p>Rechten laden...</p>
  }

  if (loadError) {
    return <p className="error-text">{loadError}</p>
  }

  const modules = new Map<string, typeof permissions>()
  for (const permission of permissions) {
    const group = modules.get(permission.module) ?? []
    group.push(permission)
    modules.set(permission.module, group)
  }

  return (
    <div className="permission-matrix">
      {[...modules.entries()].map(([module, modulePermissions]) => {
        const moduleCodes = modulePermissions.map((p) => p.code)
        const allChecked = moduleCodes.every((code) => checkedCodes.includes(code))

        return (
          <div key={module} className="permission-module">
            <label className="permission-module-header">
              <input
                type="checkbox"
                checked={allChecked}
                onChange={() => toggleModule(moduleCodes, allChecked)}
                disabled={isSubmitting}
              />
              <strong>{module}</strong>
            </label>
            <ul className="permission-list">
              {modulePermissions.map((permission) => (
                <li key={permission.code}>
                  <label>
                    <input
                      type="checkbox"
                      checked={checkedCodes.includes(permission.code)}
                      onChange={() => toggleCode(permission.code)}
                      disabled={isSubmitting}
                    />
                    {permission.description}
                  </label>
                </li>
              ))}
            </ul>
          </div>
        )
      })}

      {saveError && (
        <p className="form-error" role="alert">
          {saveError}
        </p>
      )}

      <button type="button" className="primary-button" onClick={handleSave} disabled={isSubmitting}>
        {isSubmitting ? 'Opslaan...' : 'Rechten opslaan'}
      </button>
    </div>
  )
}
