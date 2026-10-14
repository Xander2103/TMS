import { useLocale } from '../../i18n/localeContext'
import { formatTime } from '../../utils/dates'
import './SaveStatus.css'

/**
 * B31 — feedback policy for IN-PLACE saves (the panel stays on screen, repeated edits expected):
 * show the state where the user is looking, and keep the evidence after the toast would be gone.
 *   saving → "Opslaan…"   saved → "✓ Opgeslagen · 14:22"   error → "Opslaan mislukt · Opnieuw proberen"
 * A toast stays the right tool when the user leaves the context, creates an object, completes a
 * destructive action or a background job, or must act on something — never both for one save.
 */
export type SaveState = { kind: 'idle' } | { kind: 'saving' } | { kind: 'saved'; at: string } | { kind: 'error'; message?: string }

interface SaveStatusProps {
  state: SaveState
  /** Offered on error; re-runs the failed save. */
  onRetry?: () => void
  /** Unsaved edits are pending (explicit-save forms); wins over an older "Opgeslagen" (not over a failure). */
  dirty?: boolean
  className?: string
}

export function SaveStatus({ state, onRetry, dirty = false, className }: SaveStatusProps) {
  const { t } = useLocale()
  const classes = ['ui-save-status', className].filter(Boolean).join(' ')
  // A failed save outranks "unsaved": the edits are still pending AND the user must know why.
  if (dirty && state.kind !== 'saving' && state.kind !== 'error') {
    return <span className={`${classes} ui-save-status-dirty`} role="status">{t('ui.save.dirty')}</span>
  }
  switch (state.kind) {
    case 'saving':
      return <span className={`${classes} ui-save-status-saving`} role="status">{t('ui.save.saving')}</span>
    case 'saved':
      return <span className={`${classes} ui-save-status-saved`} role="status">✓ {t('ui.save.savedAt', { time: formatTime(state.at) })}</span>
    case 'error':
      return (
        <span className={`${classes} ui-save-status-error`} role="alert">
          {state.message ?? t('ui.save.failed')}
          {onRetry && (
            <>
              {' · '}
              <button type="button" className="ui-save-status-retry" onClick={onRetry}>
                {t('ui.save.retry')}
              </button>
            </>
          )}
        </span>
      )
    default:
      return <span className={classes} role="status" />
  }
}

