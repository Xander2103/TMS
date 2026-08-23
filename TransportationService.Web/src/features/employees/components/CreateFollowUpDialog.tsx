import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import type { FollowUpResult } from '../utils/preparedFollowUp'

interface CreateFollowUpDialogProps {
  employeeLabel: string
  results: FollowUpResult[]
  busy: boolean
  onRetry: () => void
  onClose: () => void
}

/**
 * Shown after employee creation when some prepared documents / assets failed to persist.
 * The employee IS created (never lost); the failed follow-ups stay retryable.
 */
export function CreateFollowUpDialog({ employeeLabel, results, busy, onRetry, onClose }: CreateFollowUpDialogProps) {
  const { t } = useLocale()
  const failed = results.filter((r) => !r.ok)
  const succeeded = results.filter((r) => r.ok)

  return (
    <Modal
      title={t('employees.create.followUpTitle')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('employees.create.followUpClose')}
          </Button>
          {failed.length > 0 && (
            <Button onClick={onRetry} disabled={busy}>
              {busy ? t('employees.create.followUpRetrying') : t('employees.create.followUpRetry', { count: failed.length })}
            </Button>
          )}
        </>
      }
    >
      <p>
        {t('employees.create.followUpBody', {
          employeeLabel,
          succeeded: succeeded.length,
          total: results.length,
          failed: failed.length,
        })}
      </p>
      <ul className="followup-results">
        {results.map((r) => (
          <li key={`${r.kind}-${r.key}`} className={r.ok ? 'followup-ok' : 'followup-failed'}>
            <span aria-hidden="true">{r.ok ? '✓' : '✗'}</span>{' '}
            {r.kind === 'document' ? t('employees.create.followUpDocument') : t('employees.create.followUpIssuedItem')}:{' '}
            {r.label}
            {!r.ok && r.error && <span className="ui-form-field-error"> — {r.error}</span>}
          </li>
        ))}
      </ul>
    </Modal>
  )
}
