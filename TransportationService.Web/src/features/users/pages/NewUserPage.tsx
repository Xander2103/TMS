import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { startUserActivation, type StartedActivation } from '../api/usersApi'
import { ActivationLinkField } from '../components/ActivationLinkField'
import { UserForm } from '../components/UserForm'
import type { User } from '../types/user'

type Created = { user: User; activation: StartedActivation | null; failed: boolean }

/**
 * HR acceptance H7: a new account cannot sign in until its owner chooses a password, so creating
 * a user ends on its next step — the one-time activation link (same secure flow as "Account
 * aanmaken" on an employee: hashed single-use token, 72h, never a password typed by the admin).
 */
export function NewUserPage() {
  const { t } = useLocale()
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const canStartActivation = hasPermission('users.edit')
  const [created, setCreated] = useState<Created | null>(null)
  const [formKey, setFormKey] = useState(0)

  async function handleSaved(user: User) {
    if (!canStartActivation) {
      setCreated({ user, activation: null, failed: false })
      return
    }
    try {
      setCreated({ user, activation: await startUserActivation(user.id), failed: false })
    } catch {
      setCreated({ user, activation: null, failed: true })
    }
  }

  if (created) {
    const name = `${created.user.firstName} ${created.user.lastName}`.trim()
    return (
      <>
        <PageHeader title={t('usersRoles.users.activation.createdTitle')} />
        <section className="user-access-section" aria-live="polite">
          {created.activation ? (
            <>
              <p>{t('usersRoles.users.activation.createdBody', { name })}</p>
              <ActivationLinkField token={created.activation.token} />
            </>
          ) : (
            <p role={created.failed ? 'alert' : undefined}>
              {created.failed
                ? t('usersRoles.users.activation.startFailed')
                : t('usersRoles.users.activation.noPermission', { name })}
            </p>
          )}
          <div className="form-actions">
            <Button onClick={() => navigate(`/users/${created.user.id}`)}>{t('usersRoles.users.activation.toUser')}</Button>
            <Button
              variant="secondary"
              onClick={() => {
                setCreated(null)
                setFormKey((key) => key + 1)
              }}
            >
              {t('usersRoles.users.activation.another')}
            </Button>
          </div>
        </section>
      </>
    )
  }

  return (
    <>
      <PageHeader title={t('usersRoles.users.page.newUser')} />
      <UserForm
        key={formKey}
        onSaved={(user) => void handleSaved(user)}
        secondaryAction={
          <Link to="/users" className="secondary-link">
            {t('ui.actions.cancel')}
          </Link>
        }
      />
    </>
  )
}
