import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import {
  createInvoice,
  getInvoiceControl,
  snoozeInvoiceControlOrder,
  type ControlOrder,
  type InvoiceControl,
  type InvoiceProposal,
} from '../api/invoicesApi'
import { euro } from '../types'
import { READINESS_REASON_KEYS } from '../utils/readiness'
import './invoices.css'

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
  const { t } = useLocale()
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
      showError(t('invoices.control.needsOrder'))
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
      showSuccess(t('invoices.control.created', { number: invoice.invoiceNumber ?? '', total: orders.length }))
      navigate(`/invoices/${invoice.id}`)
    } catch (err) {
      showError(localizeApiError(t, err, t('invoices.new.createError')))
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
      showError(t('invoices.control.snoozeMissingDate'))
      return
    }
    setBusy(true)
    try {
      await snoozeInvoiceControlOrder(orderId, { until: snoozeUntil, reason: snoozeReason.trim() || null })
      showSuccess(t('invoices.control.snoozed'))
      setSnoozeTargetId(null)
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('invoices.control.snoozeError')))
    } finally {
      setBusy(false)
    }
  }

  async function clearSnooze(orderId: string) {
    setBusy(true)
    try {
      await snoozeInvoiceControlOrder(orderId, { until: null, reason: null })
      showSuccess(t('invoices.control.snoozeCleared'))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('invoices.control.snoozeClearError')))
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
          aria-label={t('invoices.control.snoozeUntilFor', { number: order.orderNumber })}
          value={snoozeUntil}
          onChange={(e) => setSnoozeUntil(e.target.value)}
          disabled={busy}
        />
        <input
          aria-label={t('invoices.control.snoozeReasonFor', { number: order.orderNumber })}
          placeholder={t('invoices.common.reason')}
          value={snoozeReason}
          onChange={(e) => setSnoozeReason(e.target.value)}
          disabled={busy}
        />
        <Button variant="secondary" disabled={busy} onClick={() => void confirmSnooze(order.transportOrderId)}>
          {t('invoices.control.confirmSnooze')}
        </Button>
        <Button variant="ghost" disabled={busy} onClick={() => setSnoozeTargetId(null)}>
          {t('ui.actions.cancel')}
        </Button>
      </span>
    )
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('invoices.list.title'), to: '/invoices' }, { label: t('invoices.control.title') }]} />
      <PageHeader
        title={t('invoices.control.title')}
        subtitle={t('invoices.control.subtitle')}
      />

      {control === null && <p className="placeholder-text">{t('invoices.control.loading')}</p>}

      {control && control.pendingCharges.length > 0 && (
        <section className="ui-form-section">
          <h3>{t('invoices.control.pendingChargesTitle')}</h3>
          {control.pendingCharges.map((line, index) => (
            <p key={index} className="inv-period-warning">{line}</p>
          ))}
        </section>
      )}

      {control && (
        <section className="ui-form-section">
          <h3>{t('invoices.control.proposalsTitle', { total: control.proposals.length })}</h3>
          {control.proposals.length === 0 && <p className="placeholder-text">{t('invoices.control.noProposals')}</p>}
          {control.proposals.map((proposal) => (
            <div key={proposal.customerId + proposal.groupLabel} className="wh-card">
              <div className="wh-card-head">
                <div>
                  <h4 style={{ margin: 0 }}>{proposal.customerName} — {proposal.groupLabel}</h4>
                  <p className="wh-muted">
                    {t('invoices.control.selectedSummary', {
                      selected: selectedOrders(proposal).length,
                      total: proposal.orders.length,
                      amount: euro(proposal.totalAmount),
                    })}
                  </p>
                </div>
                <Button
                  variant="secondary"
                  disabled={busy || selectedOrders(proposal).length === 0}
                  onClick={() => void createFromProposal(proposal)}
                >
                  {t('invoices.control.createInvoice')}
                </Button>
              </div>
              <table className="issued-items-table">
                <thead>
                  <tr><th /><th>{t('invoices.control.columns.order')}</th><th>{t('invoices.control.columns.date')}</th><th>{t('invoices.control.columns.dossier')}</th><th>{t('invoices.control.columns.amount')}</th><th /></tr>
                </thead>
                <tbody>
                  {proposal.orders.map((order) => (
                    <tr key={order.transportOrderId}>
                      <td>
                        <input
                          type="checkbox"
                          aria-label={t('invoices.control.includeOrder', { number: order.orderNumber })}
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
                            {t('invoices.control.snoozeAction')}
                          </button>
                        )}
                        <Link to={`/transport-orders/${order.transportOrderId}`}>{t('invoices.control.toOrder')}</Link>
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
          <h3>{t('invoices.control.reviewTitle', { total: control.needsReview.length })}</h3>
          {control.needsReview.length === 0 && <p className="placeholder-text">{t('invoices.control.reviewEmpty')}</p>}
          {control.needsReview.length > 0 && (
            <table className="issued-items-table">
              <thead>
                <tr><th>{t('invoices.control.columns.order')}</th><th>{t('invoices.control.columns.date')}</th><th>{t('invoices.control.columns.dossier')}</th><th>{t('invoices.control.columns.amount')}</th><th>{t('invoices.control.columns.reasons')}</th><th /></tr>
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
                        <Badge key={reason} tone="warning">
                          {READINESS_REASON_KEYS[reason] ? t(READINESS_REASON_KEYS[reason]) : reason}
                        </Badge>
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
                          {t('invoices.control.snoozeAction')}
                        </button>
                      )}
                      <Link to={`/transport-orders/${order.transportOrderId}`}>{t('invoices.control.toOrder')}</Link>
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
          <h3>{t('invoices.control.snoozedTitle', { total: control.snoozed.length })}</h3>
          <table className="issued-items-table">
            <thead>
              <tr><th>{t('invoices.control.columns.order')}</th><th>{t('invoices.control.columns.date')}</th><th>{t('invoices.control.columns.dossier')}</th><th>{t('invoices.control.columns.amount')}</th><th>{t('invoices.control.columns.snoozedUntil')}</th><th>{t('invoices.common.reason')}</th><th /></tr>
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
                      {t('invoices.control.clearSnooze')}
                    </button>
                    <Link to={`/transport-orders/${order.transportOrderId}`}>{t('invoices.control.toOrder')}</Link>
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
