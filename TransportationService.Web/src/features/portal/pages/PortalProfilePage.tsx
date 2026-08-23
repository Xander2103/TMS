import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { useLocale } from '../../../i18n/localeContext'
import { changeMyPassword, getMyProfile } from '../api/portalApi'
import { MyLeaveBalanceCard } from '../../leave-balance/components/MyLeaveBalanceCard'
import type { MyProfile } from '../types'
import './portal.css'

/** Own profile (read-only; HR beheert wijzigingen) plus self-service password change. */
export function PortalProfilePage() {
  const { showSuccess, showError } = useToast()
  const { t } = useLocale()

  const [profile, setProfile] = useState<MyProfile | null>(null)
  const [loadError, setLoadError] = useState(false)

  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    getMyProfile()
      .then((data) => {
        if (!mounted) return
        setProfile(data)
        setLoadError(false)
      })
      .catch(() => {
        if (mounted) setLoadError(true)
      })
    return () => {
      mounted = false
    }
  }, [])

  async function handlePasswordChange(event: FormEvent) {
    event.preventDefault()
    if (newPassword.length < 8) {
      showError(t('portalHome.profile.passwordTooShort'))
      return
    }
    if (newPassword !== confirmPassword) {
      showError(t('portalHome.profile.passwordMismatch'))
      return
    }
    setBusy(true)
    try {
      await changeMyPassword(currentPassword, newPassword)
      showSuccess(t('portalHome.profile.passwordChanged'))
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('portalHome.profile.passwordChangeFailed'))
    } finally {
      setBusy(false)
    }
  }

  if (loadError) return <ErrorState message={t('portalHome.profile.loadFailed')} />
  if (!profile) return <LoadingState message={t('portalHome.profile.loading')} />

  return (
    <div>
      <PageHeader
        title={`${profile.firstName} ${profile.lastName}`}
        subtitle={`${profile.employeeNumber}${profile.departmentName ? ` · ${profile.departmentName}` : ''}`}
        action={profile.isDriver ? <Badge tone="info">{t('portalHome.profile.driverBadge', { number: profile.driverNumber ?? '' })}</Badge> : undefined}
      />

      <MyLeaveBalanceCard />

      <section className="to-section">
        <h2>{t('portalHome.profile.contactSection')}</h2>
        <dl className="to-facts">
          <div>
            <dt>{t('portalHome.profile.email')}</dt>
            <dd>{profile.email}</dd>
          </div>
          <div>
            <dt>{t('portalHome.profile.phone')}</dt>
            <dd>
              {profile.phoneNumber}
              {profile.mobilePhone && ` · ${profile.mobilePhone}`}
            </dd>
          </div>
          <div>
            <dt>{t('portalHome.profile.address')}</dt>
            <dd>
              {profile.street} {profile.houseNumber}, {profile.postalCode} {profile.city}
            </dd>
          </div>
          <div>
            <dt>{t('portalHome.profile.emergencyContact')}</dt>
            <dd>
              {profile.emergencyContactName ?? '—'}
              {profile.emergencyContactPhone && ` · ${profile.emergencyContactPhone}`}
            </dd>
          </div>
          <div>
            <dt>{t('portalHome.profile.employedSince')}</dt>
            <dd>{profile.employmentStartDate}</dd>
          </div>
          <div>
            <dt>{t('portalHome.profile.jobFunctions')}</dt>
            <dd>{profile.jobFunctions.length > 0 ? profile.jobFunctions.join(', ') : '—'}</dd>
          </div>
        </dl>
        <p className="portal-profile-note">{t('portalHome.profile.note')}</p>
      </section>

      <section className="to-section">
        <h2>{t('portalHome.profile.passwordSection')}</h2>
        <form className="portal-form portal-password-form" onSubmit={handlePasswordChange} noValidate>
          <FormField label={t('portalHome.profile.currentPassword')} htmlFor="pw-current" required>
            <input id="pw-current" type="password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} disabled={busy} autoComplete="current-password" />
          </FormField>
          <FormField label={t('portalHome.profile.newPassword')} htmlFor="pw-new" required hint={t('portalHome.profile.newPasswordHint')}>
            <input id="pw-new" type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} disabled={busy} autoComplete="new-password" />
          </FormField>
          <FormField label={t('portalHome.profile.confirmPassword')} htmlFor="pw-confirm" required>
            <input id="pw-confirm" type="password" value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)} disabled={busy} autoComplete="new-password" />
          </FormField>
          <div>
            <Button type="submit" disabled={busy}>
              {busy ? t('portalHome.profile.submitting') : t('portalHome.profile.submit')}
            </Button>
          </div>
        </form>
      </section>
    </div>
  )
}
