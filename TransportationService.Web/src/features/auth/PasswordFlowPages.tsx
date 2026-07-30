import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { FormField } from '../../components/ui/FormField'
import { ValidationSummary } from '../../components/ui/ValidationSummary'
import { useToast } from '../../components/ui/toastContext'
import { apiClient } from '../../api/apiClient'
import { describeApiError } from '../../api/problemDetails'
import { useAuth } from './authContextValue'
import './LoginPage.css'

/** Anonymous: request a reset link. The response NEVER reveals whether the account exists. */
export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    try {
      await apiClient.postJson<void, { email: string }>('/api/auth/forgot-password', { email: email.trim() })
    } finally {
      // Always the same outcome — no account enumeration.
      setDone(true)
      setBusy(false)
    }
  }

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={handleSubmit} noValidate>
        <h1>Wachtwoord vergeten</h1>
        {done ? (
          <>
            <p>
              Als er een account bestaat voor dit e-mailadres, is er een herstellink verstuurd. De link is 2 uur
              geldig.
            </p>
            <Link to="/login">Terug naar aanmelden</Link>
          </>
        ) : (
          <>
            <FormField label="E-mailadres" htmlFor="fp-email" required>
              <input id="fp-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoFocus disabled={busy} />
            </FormField>
            <Button type="submit" disabled={busy || !email.trim()}>
              {busy ? 'Versturen…' : 'Herstellink aanvragen'}
            </Button>
            <Link to="/login">Terug naar aanmelden</Link>
          </>
        )}
      </form>
    </div>
  )
}

/** Anonymous: complete a reset/activation with the single-use token from the link. */
export function ResetPasswordPage() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const toast = useToast()
  const token = searchParams.get('token') ?? ''
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (password !== confirm) {
      setError('De wachtwoorden komen niet overeen.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await apiClient.postJson<void, { token: string; newPassword: string }>('/api/auth/reset-password', {
        token,
        newPassword: password,
      })
      toast.showSuccess('Je wachtwoord is ingesteld. Meld je aan met je nieuwe wachtwoord.')
      navigate('/login')
    } catch (err) {
      setError(describeApiError(err, 'Het wachtwoord kon niet worden ingesteld.').message)
      setBusy(false)
    }
  }

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={handleSubmit} noValidate>
        <h1>Nieuw wachtwoord instellen</h1>
        <ValidationSummary message={error} />
        <FormField label="Nieuw wachtwoord" htmlFor="rp-password" required hint="Minstens 8 tekens.">
          <input id="rp-password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoFocus disabled={busy} />
        </FormField>
        <FormField label="Bevestig wachtwoord" htmlFor="rp-confirm" required>
          <input id="rp-confirm" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} disabled={busy} />
        </FormField>
        <Button type="submit" disabled={busy || password.length === 0}>
          {busy ? 'Opslaan…' : 'Wachtwoord instellen'}
        </Button>
        <Link to="/login">Terug naar aanmelden</Link>
      </form>
    </div>
  )
}

/**
 * Public activation page for customer-portal invites: /activeren?token=...&email=...
 * Completes with the SAME backend endpoint as password reset — UserAccountFlowService.
 * CompleteWithTokenAsync consumes any usable UserSecurityToken regardless of Activation vs.
 * PasswordReset kind, so no separate activation endpoint exists (see AuthController.ResetPassword).
 */
export function ActivatePage() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const toast = useToast()
  const token = searchParams.get('token') ?? ''
  const email = searchParams.get('email') ?? ''
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!token) {
      setError('Deze activeringslink is ongeldig.')
      return
    }
    if (password !== confirm) {
      setError('De wachtwoorden komen niet overeen.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await apiClient.postJson<void, { token: string; newPassword: string }>('/api/auth/reset-password', {
        token,
        newPassword: password,
      })
      toast.showSuccess('Uw account is geactiveerd. Meld u aan met uw nieuwe wachtwoord.')
      navigate('/login')
    } catch (err) {
      setError(describeApiError(err, 'Het account kon niet worden geactiveerd.').message)
      setBusy(false)
    }
  }

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={handleSubmit} noValidate>
        <h1>Klantportaal activeren</h1>
        <p>
          {email ? `Welkom, ${email}. ` : ''}
          Stel hieronder uw wachtwoord in om uw account te activeren.
        </p>
        <ValidationSummary message={error} />
        <FormField label="Wachtwoord" htmlFor="act-password" required hint="Minstens 8 tekens.">
          <input
            id="act-password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoFocus
            disabled={busy}
          />
        </FormField>
        <FormField label="Bevestig wachtwoord" htmlFor="act-confirm" required>
          <input id="act-confirm" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} disabled={busy} />
        </FormField>
        <Button type="submit" disabled={busy || password.length === 0}>
          {busy ? 'Bezig...' : 'Account activeren'}
        </Button>
        <Link to="/login">Terug naar aanmelden</Link>
      </form>
    </div>
  )
}

/** Forced first-login change: reachable only via the RequireAuth redirect while the flag is set. */
export function ChangePasswordPage() {
  const navigate = useNavigate()
  const toast = useToast()
  const { logout } = useAuth()
  const [currentPassword, setCurrentPassword] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (password !== confirm) {
      setError('De wachtwoorden komen niet overeen.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await apiClient.postJson<void, { currentPassword: string; newPassword: string }>('/api/me/password', {
        currentPassword,
        newPassword: password,
      })
      toast.showSuccess('Je wachtwoord is gewijzigd. Meld je opnieuw aan.')
      await logout()
      navigate('/login')
    } catch (err) {
      setError(describeApiError(err, 'Het wachtwoord kon niet worden gewijzigd.').message)
      setBusy(false)
    }
  }

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={handleSubmit} noValidate>
        <h1>Kies je eigen wachtwoord</h1>
        <p>Je gebruikt een tijdelijk wachtwoord. Kies eerst een eigen wachtwoord om verder te gaan.</p>
        <ValidationSummary message={error} />
        <FormField label="Tijdelijk (huidig) wachtwoord" htmlFor="cp-current" required>
          <input id="cp-current" type="password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} autoFocus disabled={busy} />
        </FormField>
        <FormField label="Nieuw wachtwoord" htmlFor="cp-new" required hint="Minstens 8 tekens.">
          <input id="cp-new" type="password" value={password} onChange={(e) => setPassword(e.target.value)} disabled={busy} />
        </FormField>
        <FormField label="Bevestig nieuw wachtwoord" htmlFor="cp-confirm" required>
          <input id="cp-confirm" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} disabled={busy} />
        </FormField>
        <Button type="submit" disabled={busy || password.length === 0}>
          {busy ? 'Opslaan…' : 'Wachtwoord wijzigen'}
        </Button>
      </form>
    </div>
  )
}
