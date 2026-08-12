import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import {
  createInvoice,
  getInvoiceControl,
  snoozeInvoiceControlOrder,
  type ControlOrder,
  type InvoiceControl,
  type InvoiceProposal,
} from '../api/invoicesApi'
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
 * factuuraanmaak met precies de aangevinkte orders. De nakijkrij toont per order WAAROM die
 * nog niet klaar is; P12: per order kan de facturatie worden uitgesteld (datum + reden) —
 * uitgestelde orders staan in hun eigen sectie tot de datum verstrijkt of het uitstel wordt
 * opgeheven.
 */
export function InvoiceControlPage() {
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const [control, setControl] = useState<InvoiceControl | null>(null)
  const [busy, setBusy] = useState(false)
  // Order ids the user UNchecked — default is everything selected.
  const [deselected, setDeselected] = useState<Set<string>>(new Set())
  const [snoozeTargetId, setSnoozeTargetId] = useState<string | null>(null)
  const [snoozeUntil, setSnoozeUntil] = useState('')
  const [snoozeReason, setSnoozeReason] = useState('')

  function reload() {
    getInvoiceControl()
      .then(setControl)
      .catch(() => setControl(null))
  }

  useEffect(reload, [])

  function toggleOrder(orderId: string) {
    setDeselected((current) => {
      const next = new Set(current)
      if (next.has(orderId)) next.delete(orderId)
      else next.add(orderId)
      return next
    })
  }

  function selectedOrders(proposal: InvoiceProposal): ControlOrder[] {
    return proposal.orders.filter((o) => !deselected.has(o.transportOrderId))
  }

  async function createFromProposal(proposal: InvoiceProposal) {
    const orders = selectedOrders(proposal)
    if (orders.length === 0) {
      showError('Vink minstens één order aan voor de factuur.')
      return
    }
    setBusy(true)
    try {
      const invoice = await createInvoice({
        customerId: proposal.customerId,
        invoiceDate: null,
        orderIds: orders.map((o) => o.transportOrderId),
        manualLines: [],
        notes: null,
      })
      showSuccess(`Factuur ${invoice.invoiceNumber ?? ''} aangemaakt (${orders.length} orders).`)
      navigate(`/invoices/${invoice.id}`)
    } catch (err) {
      showError(describeApiError(err, 'De factuur kon niet worden aangemaakt.').message)
    } finally {
      setBusy(false)
    }
  }

  function openSnooze(orderId: string) {
    setSnoozeTargetId(orderId)
    setSnoozeUntil('')
    setSnoozeReason('')
  }

  async function confirmSnooze(orderId: string) {
    if (!snoozeUntil) {
      showError('Kies een datum tot wanneer de facturatie wordt uitgesteld.')
      return
    }
    setBusy(true)
    try {
      await snoozeInvoiceControlOrder(orderId, { until: snoozeUntil, reason: snoozeReason.trim() || null })
      showSuccess('Facturatie uitgesteld.')
      setSnoozeTargetId(null)
      reload()
    } catch (err) {
      showError(describeApiError(err, 'Het uitstel kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  async function clearSnooze(orderId: string) {
    setBusy(true)
    try {
      await snoozeInvoiceControlOrder(orderId, { until: null, reason: null })
      showSuccess('Uitstel opgeheven — de order telt weer mee.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'Het uitstel kon niet worden opgeheven.').message)
    } finally {
      setBusy(false)
    }
  }

  /** Inline datum+reden invoer voor het uitstellen van één order (P12). */
  function snoozeEditor(order: ControlOrder) {
    return (
      <span className="inv-snooze-editor">
        <input
          type="date"
          aria-label={`Uitstellen tot voor ${order.orderNumber}`}
          value={snoozeUntil}
          onChange={(e) => setSnoozeUntil(e.target.value)}
          disabled={busy}
        />
        <input
          aria-label={`Reden van uitstel voor ${order.orderNumber}`}
          placeholder="Reden"
          value={snoozeReason}
          onChange={(e) => setSnoozeReason(e.target.value)}
          disabled={busy}
        />
        <Button variant="secondary" disabled={busy} onClick={() => void confirmSnooze(order.transportOrderId)}>
          Bevestig uitstel
        </Button>
        <Button variant="ghost" disabled={busy} onClick={() => setSnoozeTargetId(null)}>
          Annuleren
        </Button>
      </span>
    )
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
                    {selectedOrders(proposal).length} van {proposal.orders.length} orders aangevinkt · {euro(proposal.totalAmount)}
                  </p>
                </div>
                <Button
                  variant="secondary"
                  disabled={busy || selectedOrders(proposal).length === 0}
                  onClick={() => void createFromProposal(proposal)}
                >
                  Maak factuur
                </Button>
              </div>
              <table className="issued-items-table">
                <thead>
                  <tr><th /><th>Order</th><th>Datum</th><th>Dossier</th><th>Bedrag</th><th /></tr>
                </thead>
                <tbody>
                  {proposal.orders.map((order) => (
                    <tr key={order.transportOrderId}>
                      <td>
                        <input
                          type="checkbox"
                          aria-label={`Order ${order.orderNumber} meenemen op de factuur`}
                          checked={!deselected.has(order.transportOrderId)}
                          disabled={busy}
                          onChange={() => toggleOrder(order.transportOrderId)}
                        />
                      </td>
                      <td><code>{order.orderNumber}</code></td>
                      <td>{order.orderDate}</td>
                      <td>{order.dossierNumber ?? '—'}</td>
                      <td>{order.agreedPrice !== null ? euro(order.agreedPrice) : '—'}</td>
                      <td className="issued-items-row-actions">
                        {snoozeTargetId === order.transportOrderId ? (
                          snoozeEditor(order)
                        ) : (
                          <button
                            type="button"
                            className="issued-items-link"
                            disabled={busy}
                            onClick={() => openSnooze(order.transportOrderId)}
                          >
                            Uitstellen
                          </button>
                        )}
                        <Link to={`/transport-orders/${order.transportOrderId}`}>Naar opdracht</Link>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
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
                <tr><th>Order</th><th>Datum</th><th>Dossier</th><th>Bedrag</th><th>Redenen</th><th /></tr>
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
                    <td className="issued-items-row-actions">
                      {snoozeTargetId === order.transportOrderId ? (
                        snoozeEditor(order)
                      ) : (
                        <button
                          type="button"
                          className="issued-items-link"
                          disabled={busy}
                          onClick={() => openSnooze(order.transportOrderId)}
                        >
                          Uitstellen
                        </button>
                      )}
                      <Link to={`/transport-orders/${order.transportOrderId}`}>Naar opdracht</Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      )}

      {control && control.snoozed.length > 0 && (
        <section className="ui-form-section">
          <h3>Uitgesteld ({control.snoozed.length})</h3>
          <table className="issued-items-table">
            <thead>
              <tr><th>Order</th><th>Datum</th><th>Dossier</th><th>Bedrag</th><th>Uitgesteld tot</th><th>Reden</th><th /></tr>
            </thead>
            <tbody>
              {control.snoozed.map((order) => (
                <tr key={order.transportOrderId}>
                  <td><code>{order.orderNumber}</code></td>
                  <td>{order.orderDate}</td>
                  <td>{order.dossierNumber ?? '—'}</td>
                  <td>{order.agreedPrice !== null ? euro(order.agreedPrice) : '—'}</td>
                  <td>{order.snoozedUntil ?? '—'}</td>
                  <td>{order.snoozeReason ?? '—'}</td>
                  <td className="issued-items-row-actions">
                    <button
                      type="button"
                      className="issued-items-link"
                      disabled={busy}
                      onClick={() => void clearSnooze(order.transportOrderId)}
                    >
                      Uitstel opheffen
                    </button>
                    <Link to={`/transport-orders/${order.transportOrderId}`}>Naar opdracht</Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}
    </div>
  )
}
