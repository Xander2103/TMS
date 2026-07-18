import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate, type Location } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { FormField } from '../../components/ui/FormField'
import { useAuth } from './AuthContext'
import { LoginError } from './authApi'
import './LoginPage.css'

interface LocationState {
  from?: Location
}

export function LoginPage() {
  const { login, status } = useAuth()
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
      setError('Vul zowel je e-mailadres als wachtwoord in.')
      return
    }

    setSubmitting(true)
    try {
      await login(email.trim(), password)
      navigate(from, { replace: true })
    } catch (err) {
      setError(err instanceof LoginError ? err.message : 'Inloggen is mislukt. Probeer het opnieuw.')
      setSubmitting(false)
    }
  }

  return (
    <div className="login-page">
      <div className="login-card">
        <div className="login-brand">
          <span className="login-brand-mark" aria-hidden="true" />
          <h1 className="login-title">Transportation Service</h1>
          <p className="login-subtitle">Meld je aan om verder te gaan</p>
        </div>

        <form className="login-form" onSubmit={handleSubmit} noValidate>
          {error && (
            <div className="login-error" role="alert">
              {error}
            </div>
          )}

          <FormField label="E-mailadres" htmlFor="login-email" required>
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

          <FormField label="Wachtwoord" htmlFor="login-password" required>
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
            {submitting ? 'Bezig met aanmelden…' : 'Inloggen'}
          </Button>
        </form>
      </div>
    </div>
  )
}
