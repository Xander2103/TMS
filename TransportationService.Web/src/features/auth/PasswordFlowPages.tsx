import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { FormField } from '../../components/ui/FormField'
import { ValidationSummary } from '../../components/ui/ValidationSummary'
import { useToast } from '../../components/ui/toastContext'
import { apiClient } from '../../api/apiClient'
import { describeApiError } from '../../api/problemDetails'
import { useLocale } from '../../i18n/localeContext'
import { useAuth } from './authContextValue'
import './LoginPage.css'

// Deze publieke flowpagina's erven de app-brede root-LocaleProvider (i18n-wave):
// ts.locale-cache -> browsertaal -> nl; persist is anoniem onmogelijk.

/** Anonymous: request a reset link. The response NEVER reveals whether the account exists. */
export function ForgotPasswordPage() {
  return <ForgotPasswordContent />
}

function ForgotPasswordContent() {
  const { t } = useLocale()
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
        <h1>{t('auth.forgot.title')}</h1>
        {done ? (
          <>
            <p>{t('auth.forgot.done')}</p>
            <Link to="/login">{t('auth.backToLogin')}</Link>
          </>
        ) : (
          <>
            <FormField label={t('auth.forgot.email')} htmlFor="fp-email" required>
              <input id="fp-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoFocus disabled={busy} />
            </FormField>
            <Button type="submit" disabled={busy || !email.trim()}>
              {busy ? t('auth.forgot.submitting') : t('auth.forgot.submit')}
            </Button>
            <Link to="/login">{t('auth.backToLogin')}</Link>
          </>
        )}
      </form>
    </div>
  )
}

/** Anonymous: complete a reset/activation with the single-use token from the link. */
export function ResetPasswordPage() {
  return <ResetPasswordContent />
}

function ResetPasswordContent() {
  const { t } = useLocale()
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
      setError(t('auth.passwordMismatch'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      await apiClient.postJson<void, { token: string; newPassword: string }>('/api/auth/reset-password', {
        token,
        newPassword: password,
      })
      toast.showSuccess(t('auth.reset.success'))
      navigate('/login')
    } catch (err) {
      setError(describeApiError(err, t('auth.reset.failed')).message)
      setBusy(false)
    }
  }

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={handleSubmit} noValidate>
        <h1>{t('auth.reset.title')}</h1>
        <ValidationSummary message={error} />
        <FormField label={t('auth.reset.newPassword')} htmlFor="rp-password" required hint={t('auth.passwordHint')}>
          <input id="rp-password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoFocus disabled={busy} />
        </FormField>
        <FormField label={t('auth.reset.confirmPassword')} htmlFor="rp-confirm" required>
          <input id="rp-confirm" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} disabled={busy} />
        </FormField>
        <Button type="submit" disabled={busy || password.length === 0}>
          {busy ? t('auth.reset.submitting') : t('auth.reset.submit')}
        </Button>
        <Link to="/login">{t('auth.backToLogin')}</Link>
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
  return <ActivateContent />
}

function ActivateContent() {
  const { t } = useLocale()
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
      setError(t('auth.activate.invalidLink'))
      return
    }
    if (password !== confirm) {
      setError(t('auth.passwordMismatch'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      await apiClient.postJson<void, { token: string; newPassword: string }>('/api/auth/reset-password', {
        token,
        newPassword: password,
      })
      toast.showSuccess(t('auth.activate.success'))
      navigate('/login')
    } catch (err) {
      setError(describeApiError(err, t('auth.activate.failed')).message)
      setBusy(false)
    }
  }

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={handleSubmit} noValidate>
        <h1>{t('auth.activate.title')}</h1>
        <p>
          {email ? `${t('auth.activate.welcome', { email })} ` : ''}
          {t('auth.activate.intro')}
        </p>
        <ValidationSummary message={error} />
        <FormField label={t('auth.activate.password')} htmlFor="act-password" required hint={t('auth.passwordHint')}>
          <input
            id="act-password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoFocus
            disabled={busy}
          />
        </FormField>
        <FormField label={t('auth.activate.confirmPassword')} htmlFor="act-confirm" required>
          <input id="act-confirm" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} disabled={busy} />
        </FormField>
        <Button type="submit" disabled={busy || password.length === 0}>
          {busy ? t('auth.activate.submitting') : t('auth.activate.submit')}
        </Button>
        <Link to="/login">{t('auth.backToLogin')}</Link>
      </form>
    </div>
  )
}

/** Forced first-login change: reachable only via the RequireAuth redirect while the flag is set. */
export function ChangePasswordPage() {
  return <ChangePasswordContent />
}

function ChangePasswordContent() {
  const { t } = useLocale()
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
      setError(t('auth.passwordMismatch'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      await apiClient.postJson<void, { currentPassword: string; newPassword: string }>('/api/me/password', {
        currentPassword,
        newPassword: password,
      })
      toast.showSuccess(t('auth.change.success'))
      await logout()
      navigate('/login')
    } catch (err) {
      setError(describeApiError(err, t('auth.change.failed')).message)
      setBusy(false)
    }
  }

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={handleSubmit} noValidate>
        <h1>{t('auth.change.title')}</h1>
        <p>{t('auth.change.intro')}</p>
        <ValidationSummary message={error} />
        <FormField label={t('auth.change.currentPassword')} htmlFor="cp-current" required>
          <input id="cp-current" type="password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} autoFocus disabled={busy} />
        </FormField>
        <FormField label={t('auth.change.newPassword')} htmlFor="cp-new" required hint={t('auth.passwordHint')}>
          <input id="cp-new" type="password" value={password} onChange={(e) => setPassword(e.target.value)} disabled={busy} />
        </FormField>
        <FormField label={t('auth.change.confirmPassword')} htmlFor="cp-confirm" required>
          <input id="cp-confirm" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} disabled={busy} />
        </FormField>
        <Button type="submit" disabled={busy || password.length === 0}>
          {busy ? t('auth.change.submitting') : t('auth.change.submit')}
        </Button>
      </form>
    </div>
  )
}
