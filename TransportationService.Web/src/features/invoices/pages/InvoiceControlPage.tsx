import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import {
  createInvoice,
  getInvoiceControl,
  snoozeInvoiceControlOrder,
  type ControlActivity,
  type ControlOrder,
  type InvoiceControl,
  type InvoiceProposal,
} from '../api/invoicesApi'
import { euro } from '../types'
import { READINESS_REASON_KEYS } from '../utils/readiness'
import './invoices.css'
// The link-style row actions (Uitstellen, Naar opdracht) reuse .issued-items-link; without this
// import they rendered as native light buttons until the issued-items screen was visited.
import '../../issued-items/issued-items.css'
import { formatDate } from '../../../utils/dates'

const ACTIVITY_REASON_KEYS: Record<string, string> = {
  'activity.price_missing': 'invoices.control.activityReasons.activity_price_missing',
  'activity.price_zero_unconfirmed': 'invoices.control.activityReasons.activity_price_zero_unconfirmed',
}

/** "Kraanwerk ter plaatse — Kraan werf B". */
function activityName(activity: ControlActivity): string {
  return activity.label ? `${activity.activityTypeName} — ${activity.label}` : activity.activityTypeName
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
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission, hasAnyPermission } = useAuth()
  // ACC-M8: a viewer (Management) reads the workspace; only the rights behind the endpoints show
  // the actions (POST api/invoices → invoices.create, snooze → invoices.edit|create).
  const canCreate = hasPermission('invoices.create')
  const canSnooze = hasAnyPermission(['invoices.edit', 'invoices.create'])
  const readOnly = !canCreate && !canSnooze
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

  /** T-H8: ready standalone activities share the proposal (and the same opt-out set of ids). */
  function selectedActivities(proposal: InvoiceProposal): ControlActivity[] {
    return (proposal.activities ?? []).filter((a) => !deselected.has(a.dossierActivityId))
  }

  function selectedCount(proposal: InvoiceProposal): number {
    return selectedOrders(proposal).length + selectedActivities(proposal).length
  }

  async function createFromProposal(proposal: InvoiceProposal) {
    const orders = selectedOrders(proposal)
    const activities = selectedActivities(proposal)
    if (orders.length + activities.length === 0) {
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
        ...(activities.length > 0 ? { dossierActivityIds: activities.map((a) => a.dossierActivityId) } : {}),
      })
      // ACC-H3: a fresh draft carries no legal number yet — the toast names its draft reference.
      const created = invoice as { invoiceNumber?: string | null; displayNumber?: string | null }
      showSuccess(t('invoices.control.created', { number: created.displayNumber ?? created.invoiceNumber ?? '', total: orders.length + activities.length }))
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

      {readOnly && <p className="placeholder-text">{t('invoices.control.readOnlyNote')}</p>}

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
                      selected: selectedCount(proposal),
                      total: proposal.orders.length + (proposal.activities?.length ?? 0),
                      amount: euro(proposal.totalAmount),
                    })}
                  </p>
                </div>
                {canCreate && (
                  <Button
                    variant="secondary"
                    disabled={busy || selectedCount(proposal) === 0}
                    onClick={() => void createFromProposal(proposal)}
                  >
                    {t('invoices.control.createInvoice')}
                  </Button>
                )}
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
                          disabled={busy || !canCreate}
                          onChange={() => toggleOrder(order.transportOrderId)}
                        />
                      </td>
                      <td><code>{order.orderNumber}</code></td>
                      <td className="nowrap">{formatDate(order.orderDate)}</td>
                      <td>{order.dossierNumber ?? '—'}</td>
                      <td>{order.agreedPrice !== null ? euro(order.agreedPrice) : '—'}</td>
                      <td className="issued-items-row-actions">
                        {canSnooze && (snoozeTargetId === order.transportOrderId ? (
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
                        ))}
                        <Link to={`/transport-orders/${order.transportOrderId}`}>{t('invoices.control.toOrder')}</Link>
                      </td>
                    </tr>
                  ))}
                  {(proposal.activities ?? []).map((activity) => (
                    <tr key={activity.dossierActivityId}>
                      <td>
                        <input
                          type="checkbox"
                          aria-label={t('invoices.control.includeActivity', { name: activityName(activity) })}
                          checked={!deselected.has(activity.dossierActivityId)}
                          disabled={busy || !canCreate}
                          onChange={() => toggleOrder(activity.dossierActivityId)}
                        />
                      </td>
                      <td>{activityName(activity)}</td>
                      <td className="nowrap">{activity.date ? formatDate(activity.date) : '—'}</td>
                      <td>{activity.dossierNumber}</td>
                      <td>{activity.isFree ? t('invoices.control.activityFree') : activity.agreedPrice !== null ? euro(activity.agreedPrice) : '—'}</td>
                      <td className="issued-items-row-actions">
                        <Link to={`/dossiers/${activity.dossierId}`}>{t('invoices.control.toDossier')}</Link>
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
                    <td className="nowrap">{formatDate(order.orderDate)}</td>
                    <td>{order.dossierNumber ?? '—'}</td>
                    <td>{order.agreedPrice !== null ? euro(order.agreedPrice) : '—'}</td>
                    <td className="inv-control-reasons">
                      {order.reasons.map((reason) => (
                        <Badge key={reason} tone="warning">
                          {READINESS_REASON_KEYS[reason] ? t(READINESS_REASON_KEYS[reason], { number: order.billingHoldOrderNumber ?? '' }) : reason}
                        </Badge>
                      ))}
                    </td>
                    <td className="issued-items-row-actions">
                      {canSnooze && (snoozeTargetId === order.transportOrderId ? (
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
                      ))}
                      <Link to={`/transport-orders/${order.transportOrderId}`}>{t('invoices.control.toOrder')}</Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      )}

      {control && (
        <section className="ui-form-section">
          <h3>{t('invoices.control.activitiesReviewTitle', { total: control.activitiesNeedingReview?.length ?? 0 })}</h3>
          {(control.activitiesNeedingReview ?? []).length === 0 ? (
            <p className="placeholder-text">{t('invoices.control.activitiesReviewEmpty')}</p>
          ) : (
            <table className="issued-items-table">
              <thead>
                <tr><th>{t('invoices.control.columns.activity')}</th><th>{t('invoices.control.columns.date')}</th><th>{t('invoices.control.columns.dossier')}</th><th>{t('invoices.control.columns.reasons')}</th><th /></tr>
              </thead>
              <tbody>
                {(control.activitiesNeedingReview ?? []).map((activity) => (
                  <tr key={activity.dossierActivityId}>
                    <td>{activityName(activity)}</td>
                    <td className="nowrap">{activity.date ? formatDate(activity.date) : '—'}</td>
                    <td>{activity.dossierNumber} · {activity.customerName}</td>
                    <td>
                      {activity.reasons.map((reason) => (
                        <Badge key={reason} tone="warning">
                          {ACTIVITY_REASON_KEYS[reason] ? t(ACTIVITY_REASON_KEYS[reason]) : reason}
                        </Badge>
                      ))}
                    </td>
                    <td className="issued-items-row-actions">
                      <Link to={`/dossiers/${activity.dossierId}`}>{t('invoices.control.toDossier')}</Link>
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
                  <td className="nowrap">{formatDate(order.orderDate)}</td>
                  <td>{order.dossierNumber ?? '—'}</td>
                  <td>{order.agreedPrice !== null ? euro(order.agreedPrice) : '—'}</td>
                  <td className="nowrap">{order.snoozedUntil ? formatDate(order.snoozedUntil) : '—'}</td>
                  <td>{order.snoozeReason ?? '—'}</td>
                  <td className="issued-items-row-actions">
                    {canSnooze && (
                      <button
                        type="button"
                        className="issued-items-link"
                        disabled={busy}
                        onClick={() => void clearSnooze(order.transportOrderId)}
                      >
                        {t('invoices.control.clearSnooze')}
                      </button>
                    )}
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
