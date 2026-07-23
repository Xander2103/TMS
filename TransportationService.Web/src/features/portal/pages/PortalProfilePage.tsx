import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { changeMyPassword, getMyProfile } from '../api/portalApi'
import { MyLeaveBalanceCard } from '../../leave-balance/components/MyLeaveBalanceCard'
import type { MyProfile } from '../types'
import './portal.css'

/** Own profile (read-only; HR beheert wijzigingen) plus self-service password change. */
export function PortalProfilePage() {
  const { showSuccess, showError } = useToast()

  const [profile, setProfile] = useState<MyProfile | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

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
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Je profiel kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  async function handlePasswordChange(event: FormEvent) {
    event.preventDefault()
    if (newPassword.length < 8) {
      showError('Het nieuwe wachtwoord moet minstens 8 tekens lang zijn.')
      return
    }
    if (newPassword !== confirmPassword) {
      showError('De bevestiging komt niet overeen met het nieuwe wachtwoord.')
      return
    }
    setBusy(true)
    try {
      await changeMyPassword(currentPassword, newPassword)
      showSuccess('Je wachtwoord is gewijzigd.')
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'Het wachtwoord kon niet worden gewijzigd.')
    } finally {
      setBusy(false)
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!profile) return <LoadingState message="Profiel laden..." />

  return (
    <div>
      <PageHeader
        title={`${profile.firstName} ${profile.lastName}`}
        subtitle={`${profile.employeeNumber}${profile.departmentName ? ` · ${profile.departmentName}` : ''}`}
        action={profile.isDriver ? <Badge tone="info">Chauffeur {profile.driverNumber}</Badge> : undefined}
      />

      <MyLeaveBalanceCard />

      <section className="to-section">
        <h2>Contact & adres</h2>
        <dl className="to-facts">
          <div>
            <dt>E-mail</dt>
            <dd>{profile.email}</dd>
          </div>
          <div>
            <dt>Telefoon</dt>
            <dd>
              {profile.phoneNumber}
              {profile.mobilePhone && ` · ${profile.mobilePhone}`}
            </dd>
          </div>
          <div>
            <dt>Adres</dt>
            <dd>
              {profile.street} {profile.houseNumber}, {profile.postalCode} {profile.city}
            </dd>
          </div>
          <div>
            <dt>Noodcontact</dt>
            <dd>
              {profile.emergencyContactName ?? '—'}
              {profile.emergencyContactPhone && ` · ${profile.emergencyContactPhone}`}
            </dd>
          </div>
          <div>
            <dt>In dienst sinds</dt>
            <dd>{profile.employmentStartDate}</dd>
          </div>
          <div>
            <dt>Functies</dt>
            <dd>{profile.jobFunctions.length > 0 ? profile.jobFunctions.join(', ') : '—'}</dd>
          </div>
        </dl>
        <p className="portal-profile-note">Klopt er iets niet? Meld het aan HR — profielwijzigingen lopen via hen.</p>
      </section>

      <section className="to-section">
        <h2>Wachtwoord wijzigen</h2>
        <form className="portal-form portal-password-form" onSubmit={handlePasswordChange} noValidate>
          <FormField label="Huidig wachtwoord" htmlFor="pw-current" required>
            <input id="pw-current" type="password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} disabled={busy} autoComplete="current-password" />
          </FormField>
          <FormField label="Nieuw wachtwoord" htmlFor="pw-new" required hint="Minstens 8 tekens.">
            <input id="pw-new" type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} disabled={busy} autoComplete="new-password" />
          </FormField>
          <FormField label="Bevestig nieuw wachtwoord" htmlFor="pw-confirm" required>
            <input id="pw-confirm" type="password" value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)} disabled={busy} autoComplete="new-password" />
          </FormField>
          <div>
            <Button type="submit" disabled={busy}>
              {busy ? 'Bezig…' : 'Wachtwoord wijzigen'}
            </Button>
          </div>
        </form>
      </section>
    </div>
  )
}
