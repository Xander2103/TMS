import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
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
  // B03: deactivating and blocking ask through the app's own dialog, not window.confirm.
  const [confirm, setConfirm] = useState<'deactivate' | 'block' | null>(null)

  async function toggleActive() {
    if (!user) return
    const saved = await setActive(user.id, !user.isActive)
    if (saved) reload()
  }

  async function toggleBlocked() {
    if (!user) return
    const saved = await setBlocked(user.id, !user.isBlocked)
    if (saved) reload()
  }

  if (isLoading) {
    return <LoadingState message={t('usersRoles.users.detail.loading')} />
  }

  if (error || !user) {
    return <ErrorState message={error ?? t('usersRoles.users.detail.notFound')} onRetry={reload} backTo="/users" />
  }

  const fullName = `${user.firstName} ${user.lastName}`

  return (
    <>
      <PageHeader title={fullName} />

      <section>
        <h3>{t('usersRoles.users.detail.sectionData')}</h3>
        <UserForm user={user} onSaved={() => reload()} />
      </section>

      <section>
        <h3>{t('usersRoles.users.detail.sectionStatus')}</h3>
        <div className="form-actions">
          <Button
            variant="secondary"
            onClick={() => (user.isActive ? setConfirm('deactivate') : void toggleActive())}
            disabled={isSubmitting}
          >
            {user.isActive ? t('usersRoles.users.detail.deactivate') : t('usersRoles.users.detail.activate')}
          </Button>
          <Button
            variant="secondary"
            onClick={() => (user.isBlocked ? void toggleBlocked() : setConfirm('block'))}
            disabled={isSubmitting}
          >
            {user.isBlocked ? t('usersRoles.users.detail.unblock') : t('usersRoles.users.detail.block')}
          </Button>
        </div>
      </section>

      <section>
        <h3>{t('usersRoles.users.detail.sectionRoles')}</h3>
        <RoleAssignmentPanel key={user.roles.map((role) => role.id).join(',')} user={user} onSaved={() => reload()} />
      </section>

      {confirm === 'deactivate' && (
        <ConfirmDialog
          title={t('usersRoles.users.detail.deactivate')}
          message={t('usersRoles.users.detail.confirmDeactivate', { name: fullName })}
          confirmLabel={t('usersRoles.users.detail.deactivate')}
          destructive
          busy={isSubmitting}
          onConfirm={() => void toggleActive().finally(() => setConfirm(null))}
          onCancel={() => setConfirm(null)}
        />
      )}
      {confirm === 'block' && (
        <ConfirmDialog
          title={t('usersRoles.users.detail.block')}
          message={t('usersRoles.users.detail.confirmBlock', { name: fullName })}
          confirmLabel={t('usersRoles.users.detail.block')}
          destructive
          busy={isSubmitting}
          onConfirm={() => void toggleBlocked().finally(() => setConfirm(null))}
          onCancel={() => setConfirm(null)}
        />
      )}
    </>
  )
}
