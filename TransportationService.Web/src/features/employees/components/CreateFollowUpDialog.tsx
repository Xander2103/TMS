import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
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
  const failed = results.filter((r) => !r.ok)
  const succeeded = results.filter((r) => r.ok)

  return (
    <Modal
      title="Medewerker aangemaakt — bijlagen deels mislukt"
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Sluiten en naar medewerker
          </Button>
          {failed.length > 0 && (
            <Button onClick={onRetry} disabled={busy}>
              {busy ? 'Opnieuw proberen…' : `Mislukte opnieuw proberen (${failed.length})`}
            </Button>
          )}
        </>
      }
    >
      <p>
        {employeeLabel} is aangemaakt. {succeeded.length} van {results.length} bijlagen zijn verwerkt;{' '}
        {failed.length} mislukt. De medewerker en je bestanden blijven behouden.
      </p>
      <ul className="followup-results">
        {results.map((r) => (
          <li key={`${r.kind}-${r.key}`} className={r.ok ? 'followup-ok' : 'followup-failed'}>
            <span aria-hidden="true">{r.ok ? '✓' : '✗'}</span> {r.kind === 'document' ? 'Document' : 'Bedrijfsmiddel'}:{' '}
            {r.label}
            {!r.ok && r.error && <span className="ui-form-field-error"> — {r.error}</span>}
          </li>
        ))}
      </ul>
    </Modal>
  )
}
