import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import type { FleetFollowUpResult } from '../utils/preparedFleetDocs'

interface FleetCreateFollowUpDialogProps {
  /** e.g. "Voertuig VRT-0007" or "Oplegger OPL-0003". */
  entityLabel: string
  results: FleetFollowUpResult[]
  busy: boolean
  onRetry: () => void
  onClose: () => void
}

/**
 * Shown after vehicle/trailer creation when some prepared documents failed to persist.
 * The asset IS created (never lost); the failed uploads stay retryable.
 */
export function FleetCreateFollowUpDialog({ entityLabel, results, busy, onRetry, onClose }: FleetCreateFollowUpDialogProps) {
  const { t } = useLocale()
  const failed = results.filter((r) => !r.ok)
  const succeeded = results.filter((r) => r.ok)

  return (
    <Modal
      title={t('fleet.docs.followUp.title')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('fleet.docs.followUp.close')}
          </Button>
          {failed.length > 0 && (
            <Button onClick={onRetry} disabled={busy}>
              {busy ? t('fleet.docs.followUp.retrying') : t('fleet.docs.followUp.retry', { count: failed.length })}
            </Button>
          )}
        </>
      }
    >
      <p>
        {t('fleet.docs.followUp.body', {
          entity: entityLabel,
          succeeded: succeeded.length,
          total: results.length,
          failed: failed.length,
        })}
      </p>
      <ul className="followup-results">
        {results.map((r) => (
          <li key={r.key} className={r.ok ? 'followup-ok' : 'followup-failed'}>
            <span aria-hidden="true">{r.ok ? '✓' : '✗'}</span> {t('fleet.docs.followUp.docLabel', { label: r.label })}
            {!r.ok && r.error && <span className="ui-form-field-error"> — {r.error}</span>}
          </li>
        ))}
      </ul>
    </Modal>
  )
}
