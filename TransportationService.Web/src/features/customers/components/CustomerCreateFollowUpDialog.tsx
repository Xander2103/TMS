import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
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
  const failed = results.filter((r) => !r.ok)
  const succeeded = results.filter((r) => r.ok)

  return (
    <Modal
      title="Klant aangemaakt — locaties deels mislukt"
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Sluiten en naar klant
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
        {customerLabel} is aangemaakt. {succeeded.length} van {results.length} locaties zijn verwerkt;{' '}
        {failed.length} mislukt. De klant en je ingevoerde locaties blijven behouden.
      </p>
      <ul className="followup-results">
        {results.map((r) => (
          <li key={r.key} className={r.ok ? 'followup-ok' : 'followup-failed'}>
            <span aria-hidden="true">{r.ok ? '✓' : '✗'}</span> Locatie: {r.label}
            {!r.ok && r.error && <span className="ui-form-field-error"> — {r.error}</span>}
          </li>
        ))}
      </ul>
    </Modal>
  )
}
