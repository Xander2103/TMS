import { useState, type ChangeEvent, type FormEvent } from 'react'
import { useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { PermissionMatrix } from '../components/PermissionMatrix'
import { useRole } from '../hooks/useRole'
import { useRoleMutations } from '../hooks/useRoleMutations'
import './RoleDetailPage.css'

const NAME_MAX_LENGTH = 150

export function RoleDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const { role, isLoading, error, reload } = useRole(id)

  if (isLoading) {
    return <LoadingState message="Rol laden..." />
  }

  if (error || !role) {
    return <ErrorState message={error ?? 'Rol niet gevonden.'} />
  }

  return (
    <>
      <PageHeader title={role.name} />

      <section>
        <h3>Gegevens</h3>
        <RoleDetailsForm
          roleId={role.id}
          isSystemRole={role.isSystemRole}
          initialName={role.name}
          initialDescription={role.description ?? ''}
          onSaved={reload}
        />
      </section>

      <section>
        <h3>Rechten</h3>
        <PermissionMatrix key={role.permissionCodes.join(',')} role={role} onSaved={reload} />
      </section>

      <section>
        <h3>Deactiveren</h3>
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
      setValidationError('Naam is verplicht.')
      return
    }
    if (trimmedName.length > NAME_MAX_LENGTH) {
      setValidationError(`Naam mag maximaal ${NAME_MAX_LENGTH} tekens bevatten.`)
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
          disabled={isSystemRole}
          aria-invalid={Boolean(validationError)}
        />
        {isSystemRole && <p className="field-hint">Systeemrollen kunnen niet worden hernoemd.</p>}
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
      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="primary-button" disabled={isSubmitting}>
          {isSubmitting ? 'Opslaan...' : 'Wijzigingen opslaan'}
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
  const { isSubmitting, error, deactivate } = useRoleMutations()

  async function handleDeactivate() {
    if (!window.confirm('Weet u zeker dat u deze rol wilt deactiveren?')) {
      return
    }
    const saved = await deactivate(roleId)
    if (saved) {
      onSaved()
    }
  }

  if (!isActive) {
    return <p className="muted-text">Deze rol is al inactief.</p>
  }

  return (
    <div>
      <button type="button" className="primary-button" onClick={handleDeactivate} disabled={isSystemRole || isSubmitting}>
        Rol deactiveren
      </button>
      {isSystemRole && <p className="field-hint">Systeemrollen kunnen niet worden gedeactiveerd.</p>}
      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}
