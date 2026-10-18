import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '../ui/Button'
import { useLocale } from '../../i18n/localeContext'
import { isForbiddenError, isNotFoundError, isRetryableError } from '../../api/notFound'
import { AccessDeniedState } from './AccessDeniedState'
import './Feedback.css'

interface ErrorStateProps {
  message: string
  /**
   * The error the load failed with (usually an ApiError). When given, the state follows its HTTP
   * status: 403 → "Geen toegang", 404 → "Niet gevonden", and "Opnieuw proberen" only where a retry
   * can help (network, 5xx, 408, 429) — never for a refusal.
   */
  error?: unknown
  /** Re-runs the failed load; renders an "Opnieuw proberen" button when given (and retryable). */
  onRetry?: () => void
  /** Route of the list/parent to return to; renders a "Terug naar de lijst" link when given. */
  backTo?: string
  backLabel?: string
  /** Anything else that helps the user recover (a custom link, a secondary action). */
  action?: ReactNode
}

/**
 * Load failure that is never a dead end: the message, plus retry and a way back wherever the
 * caller can offer them. Existing call sites that pass only `message` render exactly as before.
 * SEC-M5: with `error`, a permission refusal or a missing record gets its own deterministic
 * screen instead of the raw server text plus a retry button that can never succeed.
 */
export function ErrorState({ message, error, onRetry, backTo, backLabel, action }: ErrorStateProps) {
  const { t } = useLocale()
  const backLink = backTo && (
    <Link className="ui-button ui-button-ghost" to={backTo}>
      {backLabel ?? t('ui.feedback.backToList')}
    </Link>
  )

  if (isForbiddenError(error)) {
    return <AccessDeniedState inline action={(backLink || action) && <>{backLink}{action}</>} />
  }

  if (isNotFoundError(error)) {
    return (
      <div className="error-state" role="alert">
        <h3 className="error-state-title">{t('ui.feedback.notFoundTitle')}</h3>
        <p className="placeholder-text">{t('ui.feedback.notFoundMessage')}</p>
        {(backLink || action) && (
          <div className="error-state-actions">
            {backLink}
            {action}
          </div>
        )}
      </div>
    )
  }

  // Without an error object the caller decides (legacy behaviour); with one, only when it helps.
  const retry = onRetry && (error === undefined || isRetryableError(error)) ? onRetry : undefined
  const hasActions = Boolean(retry || backTo || action)
  return (
    <div className={hasActions ? 'error-state' : undefined}>
      <p className="error-text" role="alert" aria-live="assertive">
        {message}
      </p>
      {hasActions && (
        <div className="error-state-actions">
          {retry && (
            <Button variant="secondary" onClick={retry}>
              {t('ui.feedback.retry')}
            </Button>
          )}
          {backLink}
          {action}
        </div>
      )}
    </div>
  )
}
