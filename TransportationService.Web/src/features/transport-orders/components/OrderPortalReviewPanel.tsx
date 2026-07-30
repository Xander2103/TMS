import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { reviewPortalOrder, type PortalReviewAction } from '../api/transportOrdersApi'
import type { TransportOrderDetail } from '../types'
import './order-portal-review-panel.css'

interface OrderPortalReviewPanelProps {
  order: TransportOrderDetail
  onReviewed: (updated: TransportOrderDetail) => void
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

/** Internal order-detail panel: accept/reject/request-info on a customer-submitted order. */
export function OrderPortalReviewPanel({ order, onReviewed }: OrderPortalReviewPanelProps) {
  const { showSuccess, showError } = useToast()
  const [dialog, setDialog] = useState<Exclude<PortalReviewAction, 'Accept'> | null>(null)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)

  if (order.status !== 'Submitted') {
    return null
  }

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
    } catch {
      showError('De actie kon niet worden uitgevoerd.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="opr-panel" aria-label="Klantportaal-beoordeling">
      <h2>Ingediend via het klantportaal</h2>
      <p>Beoordeel deze opdracht: accepteer, weiger, of vraag meer informatie op bij de klant.</p>
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
          <FormField label={DIALOG_COPY[dialog].label} htmlFor="opr-reason" required>
            <textarea
              id="opr-reason"
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
