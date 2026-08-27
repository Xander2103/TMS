import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import type { LocationFollowUpResult } from '../utils/preparedLocations'

interface CustomerCreateFollowUpDialogProps {
  customerLabel: string
  results: LocationFollowUpResult[]
  busy: boolean
  onRetry: () => void
  onClose: () => void
}

/**
 * Shown after customer creation when some staged locations failed to persist. The customer
 * IS created (never lost); the failed locations stay retryable.
 */
export function CustomerCreateFollowUpDialog({ customerLabel, results, busy, onRetry, onClose }: CustomerCreateFollowUpDialogProps) {
  const { t } = useLocale()
  const failed = results.filter((r) => !r.ok)
  const succeeded = results.filter((r) => r.ok)

  return (
    <Modal
      title={t('customers.followUp.title')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('customers.followUp.closeAndGo')}
          </Button>
          {failed.length > 0 && (
            <Button onClick={onRetry} disabled={busy}>
              {busy ? t('customers.followUp.retryBusy') : t('customers.followUp.retryFailed', { count: failed.length })}
            </Button>
          )}
        </>
      }
    >
      <p>
        {t('customers.followUp.summary', {
          customer: customerLabel,
          succeeded: succeeded.length,
          total: results.length,
          failed: failed.length,
        })}
      </p>
      <ul className="followup-results">
        {results.map((r) => (
          <li key={r.key} className={r.ok ? 'followup-ok' : 'followup-failed'}>
            <span aria-hidden="true">{r.ok ? '✓' : '✗'}</span> {t('customers.followUp.locationLine', { label: r.label })}
            {!r.ok && r.error && <span className="ui-form-field-error"> — {r.error}</span>}
          </li>
        ))}
      </ul>
    </Modal>
  )
}
