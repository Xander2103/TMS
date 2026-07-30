import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { getMessage, replayMessage, STATUS_LABELS, STATUS_TONE, type EdiMessageDetail } from '../api/ediApi'

interface MessageDetailModalProps {
  id: string
  canRetry: boolean
  onClose: () => void
  /** Fired after a successful replay so the caller can refresh its list and close the modal. */
  onReplayed: () => void
}

const RESULT_ROUTES: Record<string, string> = {
  TransportOrder: '/transport-orders',
}

/** Structured EDI message detail: header fields, payload, result link and replay — not a raw JSON dump. */
export function MessageDetailModal({ id, canRetry, onClose, onReplayed }: MessageDetailModalProps) {
  const { showSuccess, showError } = useToast()
  const [message, setMessage] = useState<EdiMessageDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    getMessage(id)
      .then((data) => {
        if (mounted) setMessage(data)
      })
      .catch(() => {
        if (mounted) setError('Het bericht kon niet worden geopend.')
      })
    return () => {
      mounted = false
    }
  }, [id])

  async function replay() {
    setBusy(true)
    try {
      await replayMessage(id)
      showSuccess('Bericht opnieuw verwerkt.')
      onReplayed()
    } catch (err) {
      showError(describeApiError(err, 'De verwerking mislukte opnieuw.').message)
    } finally {
      setBusy(false)
    }
  }

  const canShowReplay = canRetry && message && (message.status === 'Failed' || message.status === 'DeadLettered')
  const resultRoute = message?.resultEntityType ? RESULT_ROUTES[message.resultEntityType] : undefined

  return (
    <Modal
      title="EDI-bericht"
      onClose={onClose}
      busy={busy}
      footer={
        canShowReplay ? (
          <>
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              Sluiten
            </Button>
            <Button onClick={() => void replay()} disabled={busy}>
              {busy ? 'Bezig...' : 'Opnieuw verwerken'}
            </Button>
          </>
        ) : (
          <Button variant="secondary" onClick={onClose}>
            Sluiten
          </Button>
        )
      }
    >
      {error && <p className="placeholder-text">{error}</p>}
      {!error && !message && <p className="placeholder-text">Bericht laden…</p>}
      {message && (
        <div className="edi-detail">
          <dl className="edi-detail-grid">
            <dt>Partner</dt>
            <dd>
              <code>{message.partnerCode}</code>
            </dd>
            <dt>Richting</dt>
            <dd>{message.direction === 'Inbound' ? 'Inkomend' : 'Uitgaand'}</dd>
            <dt>Status</dt>
            <dd>
              <Badge tone={STATUS_TONE[message.status]}>{STATUS_LABELS[message.status]}</Badge>
              {message.mappingIssue && <Badge tone="warning">mapping</Badge>}
            </dd>
            <dt>Poging</dt>
            <dd>{message.attemptCount} / 3</dd>
            <dt>Datum</dt>
            <dd>{message.createdAt.slice(0, 16).replace('T', ' ')}</dd>
            <dt>Externe referentie</dt>
            <dd>{message.externalReference ?? '—'}</dd>
            <dt>Fout</dt>
            <dd>{message.errorDetail ?? '—'}</dd>
          </dl>

          {message.validationErrors && message.validationErrors.length > 0 && (
            <div className="edi-detail-errors">
              <h4>Validatiefouten</h4>
              <ul>
                {message.validationErrors.map((e, i) => (
                  <li key={i}>{e}</li>
                ))}
              </ul>
            </div>
          )}

          {resultRoute && message.resultEntityId && (
            <p>
              <Link to={`${resultRoute}/${message.resultEntityId}`}>Bekijk order</Link>
            </p>
          )}

          <h4>Payload</h4>
          <pre className="edi-payload">{message.payloadJson}</pre>
        </div>
      )}
    </Modal>
  )
}
