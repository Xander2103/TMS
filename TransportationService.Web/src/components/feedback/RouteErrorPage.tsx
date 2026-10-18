import { useEffect } from 'react'
import { isRouteErrorResponse, useLocation, useNavigate, useRouteError } from 'react-router-dom'
import { Button } from '../ui/Button'
import { useLocale } from '../../i18n/localeContext'
import { isForbiddenError, isNotFoundError } from '../../api/notFound'
import { AccessDeniedState } from './AccessDeniedState'
import { NotFoundPage } from './NotFoundPage'
import './Feedback.css'

/**
 * Route-level error boundary (`errorElement`). A render or loader crash anywhere below it shows
 * a calm, recoverable screen instead of React Router's developer stack trace. The technical
 * detail goes to the console (and only to the screen in development); the user gets three ways
 * out: retry (a full reload of the same page, which also resets any broken in-memory state),
 * back, or the start screen. Works outside a LocaleProvider too (useLocale falls back to Dutch),
 * so it is safe as the root boundary as well.
 */
export function RouteErrorPage() {
  const error = useRouteError()
  const navigate = useNavigate()
  const location = useLocation()
  const { t } = useLocale()

  useEffect(() => {
    // Internal logging: path + error + stack. Replace with a client-log endpoint when one exists.
    console.error(`[route-error] ${location.pathname}${location.search}`, error)
  }, [error, location.pathname, location.search])

  // SEC-M5: a refusal or a missing record that escapes a page never gets "Opnieuw proberen".
  if ((isRouteErrorResponse(error) && error.status === 403) || isForbiddenError(error)) {
    return <AccessDeniedState />
  }
  if ((isRouteErrorResponse(error) && error.status === 404) || isNotFoundError(error)) {
    return <NotFoundPage />
  }

  const detail = error instanceof Error ? `${error.name}: ${error.message}\n${error.stack ?? ''}` : String(error)

  return (
    <div className="route-error" role="alert">
      <h2 className="route-error-title">{t('ui.errorPage.title')}</h2>
      <p className="route-error-message">{t('ui.errorPage.message')}</p>
      <div className="route-error-actions">
        <Button onClick={() => navigate(0)}>{t('ui.errorPage.retry')}</Button>
        <Button variant="secondary" onClick={() => navigate(-1)}>{t('ui.errorPage.back')}</Button>
        <Button variant="ghost" onClick={() => navigate('/')}>{t('ui.errorPage.home')}</Button>
      </div>
      {import.meta.env.DEV && (
        <details className="route-error-details">
          <summary>{t('ui.errorPage.details')}</summary>
          <pre>{detail}</pre>
        </details>
      )}
    </div>
  )
}
