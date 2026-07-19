import { useState, type ChangeEvent, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useAuth } from '../../auth/authContextValue'
import { RolesTable } from '../components/RolesTable'
import { useRoles } from '../hooks/useRoles'
import { useRoleMutations } from '../hooks/useRoleMutations'

const NAME_MAX_LENGTH = 150

export function RolesPage() {
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
      setValidationError('Naam is verplicht.')
      return
    }
    if (trimmedName.length > NAME_MAX_LENGTH) {
      setValidationError(`Naam mag maximaal ${NAME_MAX_LENGTH} tekens bevatten.`)
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
        title="Rollen en rechten"
        action={
          hasPermission('roles.create') && (
            <button type="button" className="primary-button" onClick={() => setIsCreating((value) => !value)}>
              {isCreating ? 'Annuleren' : 'Nieuwe rol'}
            </button>
          )
        }
      />

      {isCreating && (
        <form className="user-form" onSubmit={handleCreate} noValidate>
          <div className="form-field">
            <label htmlFor="roleName">Naam</label>
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
            <label htmlFor="roleDescription">Omschrijving</label>
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
              {isSubmitting ? 'Aanmaken...' : 'Rol aanmaken'}
            </button>
          </div>
        </form>
      )}

      {isLoading && <LoadingState message="Rollen laden..." />}
      {error && <ErrorState message={error} />}
      {!isLoading && !error && <RolesTable roles={roles} />}
    </>
  )
}
