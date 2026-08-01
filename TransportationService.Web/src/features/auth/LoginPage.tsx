import { useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate, type Location } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { FormField } from '../../components/ui/FormField'
import { LocaleProvider } from '../../i18n/LocaleProvider'
import { useLocale } from '../../i18n/localeContext'
import { useAuth } from './authContextValue'
import { LoginError } from './authApi'
import './LoginPage.css'

interface LocationState {
  from?: Location
}

/**
 * Public entry point, also used by customer-portal users. Before signing in there is no saved
 * language preference, so a standalone LocaleProvider starts from the browser language
 * (nl/fr/en prefixes, otherwise Dutch); persisting is impossible while anonymous. The internal app
 * behind this page stays Dutch.
 */
export function LoginPage() {
  return (
    <LocaleProvider>
      <LoginPageContent />
    </LocaleProvider>
  )
}

function LoginPageContent() {
  const { login, status } = useAuth()
  const { t } = useLocale()
  const navigate = useNavigate()
  const location = useLocation()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const from = (location.state as LocationState | null)?.from?.pathname ?? '/'

  // Already signed in (e.g. navigated to /login manually) — bounce to the app.
  if (status === 'authenticated') {
    return <Navigate to={from} replace />
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)

    if (!email.trim() || !password) {
      setError(t('auth.login.missingFields'))
      return
    }

    setSubmitting(true)
    try {
      await login(email.trim(), password)
      navigate(from, { replace: true })
    } catch (err) {
      setError(err instanceof LoginError ? err.message : t('auth.login.failed'))
      setSubmitting(false)
    }
  }

  return (
    <div className="login-page">
      <div className="login-card">
        <div className="login-brand">
          <span className="login-brand-mark" aria-hidden="true" />
          <h1 className="login-title">Transportation Service</h1>
          <p className="login-subtitle">{t('auth.login.subtitle')}</p>
        </div>

        <form className="login-form" onSubmit={handleSubmit} noValidate>
          {error && (
            <div className="login-error" role="alert">
              {error}
            </div>
          )}

          <FormField label={t('auth.login.email')} htmlFor="login-email" required>
            <input
              id="login-email"
              type="email"
              autoComplete="username"
              autoFocus
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              disabled={submitting}
            />
          </FormField>

          <FormField label={t('auth.login.password')} htmlFor="login-password" required>
            <input
              id="login-password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              disabled={submitting}
            />
          </FormField>

          <Button type="submit" variant="primary" className="login-submit" disabled={submitting}>
            {submitting ? t('auth.login.submitting') : t('auth.login.submit')}
          </Button>
          <Link to="/forgot-password" className="login-forgot">
            {t('auth.login.forgot')}
          </Link>
        </form>
      </div>
    </div>
  )
}
