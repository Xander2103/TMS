import { useState, type ChangeEvent, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { RolesTable } from '../components/RolesTable'
import { useRoles } from '../hooks/useRoles'
import { useRoleMutations } from '../hooks/useRoleMutations'

const NAME_MAX_LENGTH = 150

export function RolesPage() {
  const { t } = useLocale()
  const { roles, isLoading, error, reload } = useRoles()
  const { hasPermission } = useAuth()
  const [isCreating, setIsCreating] = useState(false)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [validationError, setValidationError] = useState<string | null>(null)
  const { isSubmitting, error: submitError, create } = useRoleMutations()

  function handleNameChange(event: ChangeEvent<HTMLInputElement>) {
    setName(event.target.value)
    setValidationError(null)
  }

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
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

    const created = await create({ name: trimmedName, description: description.trim() || null })
    if (created) {
      setName('')
      setDescription('')
      setIsCreating(false)
      reload()
    }
  }

  return (
    <>
      <PageHeader
        title={t('usersRoles.roles.page.title')}
        action={
          hasPermission('roles.create') && (
            <button type="button" className="primary-button" onClick={() => setIsCreating((value) => !value)}>
              {isCreating ? t('ui.actions.cancel') : t('usersRoles.roles.page.newRole')}
            </button>
          )
        }
      />

      {isCreating && (
        <form className="user-form" onSubmit={handleCreate} noValidate>
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
              aria-invalid={Boolean(validationError)}
            />
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
          {submitError && (
            <p className="form-error" role="alert">
              {submitError}
            </p>
          )}
          <div className="form-actions">
            <button type="submit" className="primary-button" disabled={isSubmitting}>
              {isSubmitting ? t('usersRoles.roles.page.creating') : t('usersRoles.roles.page.create')}
            </button>
          </div>
        </form>
      )}

      {isLoading && <LoadingState message={t('usersRoles.roles.page.loading')} />}
      {error && <ErrorState message={error} />}
      {!isLoading && !error && <RolesTable roles={roles} />}
    </>
  )
}
