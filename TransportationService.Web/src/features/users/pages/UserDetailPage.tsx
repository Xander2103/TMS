import { useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { UserForm } from '../components/UserForm'
import { RoleAssignmentPanel } from '../components/RoleAssignmentPanel'
import { useUser } from '../hooks/useUser'
import { useUserMutations } from '../hooks/useUserMutations'

export function UserDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const { user, isLoading, error, reload } = useUser(id)
  const { isSubmitting, setActive, setBlocked } = useUserMutations()

  async function handleToggleActive() {
    if (!user) return
    if (user.isActive && !window.confirm(`Weet u zeker dat u ${user.firstName} ${user.lastName} wilt deactiveren?`)) {
      return
    }
    const saved = await setActive(user.id, !user.isActive)
    if (saved) reload()
  }

  async function handleToggleBlocked() {
    if (!user) return
    if (!user.isBlocked && !window.confirm(`Weet u zeker dat u ${user.firstName} ${user.lastName} wilt blokkeren?`)) {
      return
    }
    const saved = await setBlocked(user.id, !user.isBlocked)
    if (saved) reload()
  }

  if (isLoading) {
    return <LoadingState message="Gebruiker laden..." />
  }

  if (error || !user) {
    return <ErrorState message={error ?? 'Gebruiker niet gevonden.'} />
  }

  return (
    <>
      <PageHeader title={`${user.firstName} ${user.lastName}`} />

      <section>
        <h3>Gegevens</h3>
        <UserForm user={user} onSaved={() => reload()} />
      </section>

      <section>
        <h3>Status</h3>
        <div className="form-actions">
          <button type="button" className="primary-button" onClick={handleToggleActive} disabled={isSubmitting}>
            {user.isActive ? 'Deactiveren' : 'Activeren'}
          </button>
          <button type="button" className="primary-button" onClick={handleToggleBlocked} disabled={isSubmitting}>
            {user.isBlocked ? 'Deblokkeren' : 'Blokkeren'}
          </button>
        </div>
      </section>

      <section>
        <h3>Rollen</h3>
        <RoleAssignmentPanel key={user.roles.map((role) => role.id).join(',')} user={user} onSaved={() => reload()} />
      </section>
    </>
  )
}
