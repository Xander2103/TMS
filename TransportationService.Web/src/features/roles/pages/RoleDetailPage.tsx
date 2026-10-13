import { useState, type ChangeEvent, type FormEvent } from 'react'
import { useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { PermissionMatrix } from '../components/PermissionMatrix'
import { useLocale } from '../../../i18n/localeContext'
import { useRole } from '../hooks/useRole'
import { useRoleMutations } from '../hooks/useRoleMutations'
import './RoleDetailPage.css'

const NAME_MAX_LENGTH = 150

export function RoleDetailPage() {
  const { t } = useLocale()
  const { id = '' } = useParams<{ id: string }>()
  const { role, isLoading, error, reload } = useRole(id)

  if (isLoading) {
    return <LoadingState message={t('usersRoles.roles.detail.loading')} />
  }

  if (error || !role) {
    return <ErrorState message={error ?? t('usersRoles.roles.detail.notFound')} />
  }

  return (
    <>
      <PageHeader title={role.name} />

      <section>
        <h3>{t('usersRoles.roles.detail.sectionData')}</h3>
        <RoleDetailsForm
          roleId={role.id}
          isSystemRole={role.isSystemRole}
          initialName={role.name}
          initialDescription={role.description ?? ''}
          onSaved={reload}
        />
      </section>

      <section>
        <h3>{t('usersRoles.roles.detail.sectionPermissions')}</h3>
        <PermissionMatrix key={role.permissionCodes.join(',')} role={role} onSaved={reload} />
      </section>

      <section>
        <h3>{t('usersRoles.roles.detail.sectionDeactivate')}</h3>
        <DeactivateControl roleId={role.id} isSystemRole={role.isSystemRole} isActive={role.isActive} onSaved={reload} />
      </section>
    </>
  )
}

interface RoleDetailsFormProps {
  roleId: string
  isSystemRole: boolean
  initialName: string
  initialDescription: string
  onSaved: () => void
}

function RoleDetailsForm({ roleId, isSystemRole, initialName, initialDescription, onSaved }: RoleDetailsFormProps) {
  const { t } = useLocale()
  const [name, setName] = useState(initialName)
  const [description, setDescription] = useState(initialDescription)
  const [validationError, setValidationError] = useState<string | null>(null)
  const { isSubmitting, error, update } = useRoleMutations()

  function handleNameChange(event: ChangeEvent<HTMLInputElement>) {
    setName(event.target.value)
    setValidationError(null)
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmedName = name.trim()
    if (!trimmedName) {
      setValidationError(t('usersRoles.roles.page.nameRequired'))
      return
    }
    if (trimmedName.length > NAME_MAX_LENGTH) {
      setValidationError(t('usersRoles.roles.page.nameMax', { max: NAME_MAX_LENGTH }))
      return
    }

    const saved = await update(roleId, { name: trimmedName, description: description.trim() || null })
    if (saved) {
      onSaved()
    }
  }

  return (
    <form className="user-form" onSubmit={handleSubmit} noValidate>
      <div className="form-field">
        <label htmlFor="roleName">{t('usersRoles.roles.page.name')}</label>
        <input
          id="roleName"
          name="roleName"
          type="text"
          autoComplete="off"
          required
          maxLength={NAME_MAX_LENGTH}
          value={name}
          onChange={handleNameChange}
          disabled={isSystemRole}
          aria-invalid={Boolean(validationError)}
        />
        {isSystemRole && <p className="field-hint">{t('usersRoles.roles.detail.systemRoleRenameHint')}</p>}
        {validationError && (
          <p className="field-error" role="alert">
            {validationError}
          </p>
        )}
      </div>
      <div className="form-field">
        <label htmlFor="roleDescription">{t('usersRoles.roles.page.description')}</label>
        <input
          id="roleDescription"
          name="roleDescription"
          type="text"
          autoComplete="off"
          maxLength={2000}
          value={description}
          onChange={(event) => setDescription(event.target.value)}
        />
      </div>
      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="primary-button" disabled={isSubmitting}>
          {isSubmitting ? t('usersRoles.roles.detail.saving') : t('usersRoles.roles.detail.saveChanges')}
        </button>
      </div>
    </form>
  )
}

interface DeactivateControlProps {
  roleId: string
  isSystemRole: boolean
  isActive: boolean
  onSaved: () => void
}

function DeactivateControl({ roleId, isSystemRole, isActive, onSaved }: DeactivateControlProps) {
  const { t } = useLocale()
  const { isSubmitting, error, deactivate } = useRoleMutations()

  async function handleDeactivate() {
    if (!window.confirm(t('usersRoles.roles.detail.confirmDeactivate'))) {
      return
    }
    const saved = await deactivate(roleId)
    if (saved) {
      onSaved()
    }
  }

  if (!isActive) {
    return <p className="muted-text">{t('usersRoles.roles.detail.alreadyInactive')}</p>
  }

  return (
    <div>
      <button type="button" className="primary-button" onClick={handleDeactivate} disabled={isSystemRole || isSubmitting}>
        {t('usersRoles.roles.detail.deactivateRole')}
      </button>
      {isSystemRole && <p className="field-hint">{t('usersRoles.roles.detail.systemRoleDeactivateHint')}</p>}
      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}
