import { useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { UserForm } from '../components/UserForm'
import { RoleAssignmentPanel } from '../components/RoleAssignmentPanel'
import { useLocale } from '../../../i18n/localeContext'
import { useUser } from '../hooks/useUser'
import { useUserMutations } from '../hooks/useUserMutations'

export function UserDetailPage() {
  const { t } = useLocale()
  const { id = '' } = useParams<{ id: string }>()
  const { user, isLoading, error, reload } = useUser(id)
  const { isSubmitting, setActive, setBlocked } = useUserMutations()

  async function handleToggleActive() {
    if (!user) return
    if (
      user.isActive &&
      !window.confirm(t('usersRoles.users.detail.confirmDeactivate', { name: `${user.firstName} ${user.lastName}` }))
    ) {
      return
    }
    const saved = await setActive(user.id, !user.isActive)
    if (saved) reload()
  }

  async function handleToggleBlocked() {
    if (!user) return
    if (
      !user.isBlocked &&
      !window.confirm(t('usersRoles.users.detail.confirmBlock', { name: `${user.firstName} ${user.lastName}` }))
    ) {
      return
    }
    const saved = await setBlocked(user.id, !user.isBlocked)
    if (saved) reload()
  }

  if (isLoading) {
    return <LoadingState message={t('usersRoles.users.detail.loading')} />
  }

  if (error || !user) {
    return <ErrorState message={error ?? t('usersRoles.users.detail.notFound')} />
  }

  return (
    <>
      <PageHeader title={`${user.firstName} ${user.lastName}`} />

      <section>
        <h3>{t('usersRoles.users.detail.sectionData')}</h3>
        <UserForm user={user} onSaved={() => reload()} />
      </section>

      <section>
        <h3>{t('usersRoles.users.detail.sectionStatus')}</h3>
        <div className="form-actions">
          <button type="button" className="primary-button" onClick={handleToggleActive} disabled={isSubmitting}>
            {user.isActive ? t('usersRoles.users.detail.deactivate') : t('usersRoles.users.detail.activate')}
          </button>
          <button type="button" className="primary-button" onClick={handleToggleBlocked} disabled={isSubmitting}>
            {user.isBlocked ? t('usersRoles.users.detail.unblock') : t('usersRoles.users.detail.block')}
          </button>
        </div>
      </section>

      <section>
        <h3>{t('usersRoles.users.detail.sectionRoles')}</h3>
        <RoleAssignmentPanel key={user.roles.map((role) => role.id).join(',')} user={user} onSaved={() => reload()} />
      </section>
    </>
  )
}
