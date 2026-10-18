import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { formatDateTime } from '../../../utils/dates'
import { getPortalReviewState, reviewPortalOrder, type PortalReviewAction, type PortalReviewState } from '../api/transportOrdersApi'
import type { TransportOrderDetail } from '../types'
import './order-portal-review-panel.css'

interface OrderPortalReviewPanelProps {
  /** Only the id and status are needed, so the dossier workspace can host it for a linked order. */
  order: Pick<TransportOrderDetail, 'id' | 'status'>
  onReviewed: (updated: TransportOrderDetail) => void
  /** Overrides the heading (the dossier workspace names the order the review is about). */
  heading?: string
}

const DIALOG_COPY: Record<Exclude<PortalReviewAction, 'Accept'>, { title: string; label: string; placeholder: string }> = {
  Reject: {
    title: 'Opdracht weigeren',
    label: 'Reden (verplicht, zichtbaar voor de klant)',
    placeholder: 'Bijvoorbeeld: onvoldoende laadcapaciteit op de gevraagde datum.',
  },
  RequestInfo: {
    title: 'Extra informatie opvragen',
    label: 'Welke informatie heeft u nodig? (verplicht, wordt als bericht naar de klant gestuurd)',
    placeholder: 'Bijvoorbeeld: kunt u het exacte laadadres bevestigen?',
  },
}

/**
 * Internal accept/reject/request-info on a customer-submitted order (POST
 * /api/transport-orders/{id}/portal-review). Hosted on the order detail AND in the dossier
 * workspace (M4) — one component, one endpoint, no duplicated review logic.
 */
export function OrderPortalReviewPanel({ order, onReviewed, heading }: OrderPortalReviewPanelProps) {
  const { showSuccess, showError } = useToast()
  const [dialog, setDialog] = useState<Exclude<PortalReviewAction, 'Accept'> | null>(null)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [state, setState] = useState<PortalReviewState | null>(null)
  const [stateToken, setStateToken] = useState(0)

  // The info-request round trip: the planner sees the question asked and the customer's answer
  // right where they decide — no switch to the messages module (which they may not even have).
  useEffect(() => {
    if (order.status !== 'Submitted') return
    let mounted = true
    getPortalReviewState(order.id)
      .then((loaded) => {
        if (mounted) setState(loaded)
      })
      .catch(() => {
        // Non-fatal: the decision buttons stay available.
      })
    return () => {
      mounted = false
    }
  }, [order.id, order.status, stateToken])

  if (order.status !== 'Submitted') {
    return null
  }

  const infoRequest = state?.infoRequest ?? null

  async function handleAccept() {
    setBusy(true)
    try {
      const updated = await reviewPortalOrder(order.id, 'Accept', null)
      onReviewed(updated)
      showSuccess('Opdracht geaccepteerd.')
    } catch {
      showError('De opdracht kon niet worden geaccepteerd.')
    } finally {
      setBusy(false)
    }
  }

  async function handleDialogSubmit() {
    if (!dialog || !reason.trim()) return
    setBusy(true)
    try {
      const updated = await reviewPortalOrder(order.id, dialog, reason.trim())
      onReviewed(updated)
      showSuccess(dialog === 'Reject' ? 'Opdracht geweigerd.' : 'Informatie opgevraagd bij de klant.')
      setDialog(null)
      setReason('')
      setStateToken((n) => n + 1)
    } catch {
      showError('De actie kon niet worden uitgevoerd.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="opr-panel" aria-label="Klantportaal-beoordeling">
      <h2>{heading ?? 'Ingediend via het klantportaal'}</h2>
      <p>Beoordeel deze opdracht: accepteer, weiger, of vraag meer informatie op bij de klant.</p>
      {infoRequest && (
        <div className="opr-info" data-testid="opr-info-request">
          <p className="opr-info-question">
            <strong>Gevraagd op {formatDateTime(infoRequest.askedAt)}:</strong> {infoRequest.question}
          </p>
          {infoRequest.answers.length === 0 ? (
            <p className="opr-info-waiting">Wacht op antwoord van de klant — de klant ziet "Informatie gevraagd".</p>
          ) : (
            <ul className="opr-info-answers">
              {infoRequest.answers.map((answer, index) => (
                <li key={index}>
                  <strong>Antwoord van de klant ({formatDateTime(answer.at)}):</strong> {answer.body}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
      <div className="opr-actions">
        <Button onClick={() => void handleAccept()} disabled={busy}>
          Accepteren
        </Button>
        <Button variant="secondary" onClick={() => setDialog('RequestInfo')} disabled={busy}>
          Info opvragen
        </Button>
        <Button variant="danger" onClick={() => setDialog('Reject')} disabled={busy}>
          Afwijzen
        </Button>
      </div>

      {dialog && (
        <Modal
          title={DIALOG_COPY[dialog].title}
          onClose={() => setDialog(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDialog(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button variant={dialog === 'Reject' ? 'danger' : 'primary'} onClick={() => void handleDialogSubmit()} disabled={busy || !reason.trim()}>
                {busy ? 'Bezig...' : 'Bevestigen'}
              </Button>
            </>
          }
        >
          <FormField label={DIALOG_COPY[dialog].label} htmlFor={`opr-reason-${order.id}`} required>
            <textarea
              id={`opr-reason-${order.id}`}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder={DIALOG_COPY[dialog].placeholder}
              rows={3}
              maxLength={4000}
            />
          </FormField>
        </Modal>
      )}
    </section>
  )
}
