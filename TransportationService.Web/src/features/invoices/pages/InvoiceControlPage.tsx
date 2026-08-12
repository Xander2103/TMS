import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { createInvoice, getInvoiceControl, type InvoiceControl, type InvoiceProposal } from '../api/invoicesApi'
import { euro } from '../types'
import './invoices.css'

const READINESS_REASON_TEXT: Record<string, string> = {
  'pricing.none': 'nog geen prijs',
  'pricing.coverage.partial': 'niet alle onderdelen geprijsd',
  'pricing.coverage.none': 'geen onderdeel volledig geprijsd',
  'pricing.stale': 'prijs verouderd — herbereken',
  'pod.missing': 'afleverbewijs ontbreekt',
}

/**
 * Wave 10: de facturatiecontrole-werkplek. Voorstellen volgen de groeperingsvoorkeur van de
 * klant (per dossier / week / maand / referentie); "Maak factuur" gebruikt de bestaande
 * factuuraanmaak. De nakijkrij toont per order WAAROM die nog niet klaar is; goedgekeurde
 * maar niet-geboekte doorrekeningen staan er expliciet bij — gebruikers werken uitzonderingen.
 */
export function InvoiceControlPage() {
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const [control, setControl] = useState<InvoiceControl | null>(null)
  const [busy, setBusy] = useState(false)

  function reload() {
    getInvoiceControl()
      .then(setControl)
      .catch(() => setControl(null))
  }

  useEffect(reload, [])

  async function createFromProposal(proposal: InvoiceProposal) {
    setBusy(true)
    try {
      const invoice = await createInvoice({
        customerId: proposal.customerId,
        invoiceDate: null,
        orderIds: proposal.orders.map((o) => o.transportOrderId),
        manualLines: [],
        notes: null,
      })
      showSuccess(`Factuur ${invoice.invoiceNumber ?? ''} aangemaakt (${proposal.orders.length} orders).`)
      navigate(`/invoices/${invoice.id}`)
    } catch (err) {
      showError(describeApiError(err, 'De factuur kon niet worden aangemaakt.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Facturen', to: '/invoices' }, { label: 'Facturatiecontrole' }]} />
      <PageHeader
        title="Facturatiecontrole"
        subtitle="Voorstellen volgens de klantvoorkeur, en per order waarom die nog niet factureerbaar is."
      />

      {control === null && <p className="placeholder-text">Werkplek laden…</p>}

      {control && control.pendingCharges.length > 0 && (
        <section className="ui-form-section">
          <h3>Goedgekeurde doorrekeningen — handmatig toe te voegen</h3>
          {control.pendingCharges.map((line, index) => (
            <p key={index} className="inv-period-warning">{line}</p>
          ))}
        </section>
      )}

      {control && (
        <section className="ui-form-section">
          <h3>Factuurvoorstellen ({control.proposals.length})</h3>
          {control.proposals.length === 0 && <p className="placeholder-text">Geen orders klaar voor facturatie.</p>}
          {control.proposals.map((proposal) => (
            <div key={proposal.customerId + proposal.groupLabel} className="wh-card">
              <div className="wh-card-head">
                <div>
                  <h4 style={{ margin: 0 }}>{proposal.customerName} — {proposal.groupLabel}</h4>
                  <p className="wh-muted">
                    {proposal.orders.map((o) => o.orderNumber).join(', ')} · {euro(proposal.totalAmount)}
                  </p>
                </div>
                <Button variant="secondary" disabled={busy} onClick={() => void createFromProposal(proposal)}>
                  Maak factuur
                </Button>
              </div>
            </div>
          ))}
        </section>
      )}

      {control && (
        <section className="ui-form-section">
          <h3>Nakijken vóór facturatie ({control.needsReview.length})</h3>
          {control.needsReview.length === 0 && <p className="placeholder-text">Niets na te kijken — alles is klaar of nog onderweg.</p>}
          {control.needsReview.length > 0 && (
            <table className="issued-items-table">
              <thead>
                <tr><th>Order</th><th>Datum</th><th>Dossier</th><th>Bedrag</th><th>Redenen</th></tr>
              </thead>
              <tbody>
                {control.needsReview.map((order) => (
                  <tr key={order.transportOrderId}>
                    <td><code>{order.orderNumber}</code></td>
                    <td>{order.orderDate}</td>
                    <td>{order.dossierNumber ?? '—'}</td>
                    <td>{order.agreedPrice !== null ? euro(order.agreedPrice) : '—'}</td>
                    <td>
                      {order.reasons.map((reason) => (
                        <Badge key={reason} tone="warning">{READINESS_REASON_TEXT[reason] ?? reason}</Badge>
                      ))}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      )}
    </div>
  )
}
