import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '../ui/Button'
import { useLocale } from '../../i18n/localeContext'
import './Feedback.css'

interface ErrorStateProps {
  message: string
  /** Re-runs the failed load; renders an "Opnieuw proberen" button when given. */
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
 */
export function ErrorState({ message, onRetry, backTo, backLabel, action }: ErrorStateProps) {
  const { t } = useLocale()
  const hasActions = Boolean(onRetry || backTo || action)
  return (
    <div className={hasActions ? 'error-state' : undefined}>
      <p className="error-text" role="alert" aria-live="assertive">
        {message}
      </p>
      {hasActions && (
        <div className="error-state-actions">
          {onRetry && (
            <Button variant="secondary" onClick={onRetry}>
              {t('ui.feedback.retry')}
            </Button>
          )}
          {backTo && (
            <Link className="ui-button ui-button-ghost" to={backTo}>
              {backLabel ?? t('ui.feedback.backToList')}
            </Link>
          )}
          {action}
        </div>
      )}
    </div>
  )
}
