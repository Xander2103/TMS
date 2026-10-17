import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useEffect, useState } from 'react'
import { formatDate } from '../../../utils/dates'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import type { TranslateFn } from '../../../i18n/localeContext'
import { getCustomer, searchCustomers } from '../../customers/api/customersApi'
import type { CustomerListItem } from '../../customers/types'
import { getPoPolicy } from '../../customers/api/customerBillingConfigApi'
import { getActiveLegalEntity, getLegalEntityOptions } from '../../legal-entities/api/legalEntitiesApi'
import type { LegalEntityOption } from '../../legal-entities/types'
import { localizeApiError } from '../../../api/problemDetails'
import { createInvoice, getDefaultVatRate, getNextInvoiceNumber, listUninvoicedActivities, listUninvoicedOrders } from '../api/invoicesApi'
import { euro, type ManualLineInput, type UninvoicedActivity, type UninvoicedOrder } from '../types'
import { comparePeriods, dateToPeriod, formatPeriod, monthInputToPeriod, periodToMonthInput } from '../utils/invoicePeriod'
import { READINESS_REASON_KEYS } from '../utils/readiness'
import './invoices.css'

interface ManualRow extends ManualLineInput {
  key: string
}

let manualKey = 0

/** Wave 2 §6: readable tooltip for the semicolon-separated readiness reason codes. */
function readinessTooltip(t: TranslateFn, reasons: string | null | undefined): string {
  if (!reasons) return t('invoices.readiness.tooltipFallback')
  return reasons
    .split(';')
    .map((code) => {
      const key = READINESS_REASON_KEYS[code]
      return key ? t(key) : code
    })
    .join(', ')
}

/** Invoice builder: pick a customer, tick completed orders and priced dossier activities, add manual lines. */
export function NewInvoicePage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()

  const [customers, setCustomers] = useState<CustomerListItem[]>([])
  const [customerId, setCustomerId] = useState('')
  const [orders, setOrders] = useState<UninvoicedOrder[] | null>(null)
  const [selectedOrderIds, setSelectedOrderIds] = useState<string[]>([])
  const [activities, setActivities] = useState<UninvoicedActivity[] | null>(null)
  const [selectedActivityIds, setSelectedActivityIds] = useState<string[]>([])
  const [manualLines, setManualLines] = useState<ManualRow[]>([])
  const [notes, setNotes] = useState('')
  const [poNumber, setPoNumber] = useState('')
  const [poPolicyRequired, setPoPolicyRequired] = useState(false)
  const [invoiceGrouping, setInvoiceGrouping] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // ACC-H1: the VAT % manual lines default to for this customer; null = unknown → the server
  // derives it from the customer's VAT treatment (never a silent 0).
  const [defaultVat, setDefaultVat] = useState<number | null>(null)

  const [entities, setEntities] = useState<LegalEntityOption[]>([])
  const [legalEntityId, setLegalEntityId] = useState('')
  // The invoice date defaults to today on the backend, so the period starts on today's month.
  const [periodInput, setPeriodInput] = useState(() => {
    const today = dateToPeriod(null)
    return periodToMonthInput(today.year, today.month)
  })
  const [nextNumber, setNextNumber] = useState<string | null>(null)
  // B05: anything the user filled in is lost on navigation; ask first.
  const dirty =
    customerId !== '' ||
    selectedOrderIds.length > 0 ||
    selectedActivityIds.length > 0 ||
    manualLines.length > 0 ||
    notes.trim() !== '' ||
    poNumber.trim() !== ''

  useEffect(() => {
    let mounted = true
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((data) => {
        if (mounted) setCustomers(data.items)
      })
      .catch(() => {})
    // Entity picker: preselect the user's own active entity, else the configured default.
    Promise.all([
      getLegalEntityOptions().catch(() => [] as LegalEntityOption[]),
      getActiveLegalEntity().catch(() => ({ legalEntityId: null })),
    ])
      .then(([options, active]) => {
        if (!mounted) return
        const usable = options.filter((option) => option.isActive)
        setEntities(usable)
        const preferred =
          usable.find((option) => option.id === active.legalEntityId) ??
          usable.find((option) => option.isDefault) ??
          usable[0]
        if (preferred) setLegalEntityId(preferred.id)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  // Live "next number" preview; refetches (debounced) whenever entity or period changes.
  useEffect(() => {
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      const period = monthInputToPeriod(periodInput)
      if (!period) {
        setNextNumber(null)
        return
      }
      getNextInvoiceNumber(
        { legalEntityId: legalEntityId || undefined, year: period.year, month: period.month },
        { signal: controller.signal },
      )
        .then((preview) => setNextNumber(preview.invoiceNumber))
        .catch(() => {
          // Hide the preview on 404 (no active entity) or any other failure.
          if (!controller.signal.aborted) setNextNumber(null)
        })
    }, 250)
    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [legalEntityId, periodInput])

  useEffect(() => {
    if (!customerId) {
      return
    }
    let mounted = true
    listUninvoicedOrders(customerId)
      .then((data) => {
        if (!mounted) return
        setOrders(data)
        // default: everything BILLABLE selected — a "Nakijken" order is never pre-selected (ACC-B1)
        setSelectedOrderIds(data.filter((o) => o.isInvoiceable !== false).map((o) => o.id))
      })
      .catch(() => {
        if (mounted) setOrders([])
      })
    // Dossier activities (Opslag/Kraan) load next to the orders, independently: a failure on
    // one side never hides the other.
    listUninvoicedActivities(customerId)
      .then((data) => {
        if (!mounted) return
        setActivities(data)
        setSelectedActivityIds(data.map((a) => a.id)) // default: everything selected
      })
      .catch(() => {
        if (mounted) setActivities([])
      })
    return () => {
      mounted = false
    }
  }, [customerId])

  // PO-beleid van de gekozen klant: waarschuw bij 'Verplicht' en vul een actief PO-nummer voor.
  // De reset bij het wisselen van klant gebeurt in handleCustomerChange (niet in dit effect).
  useEffect(() => {
    if (!customerId) return
    let mounted = true
    getPoPolicy(customerId)
      .then((policy) => {
        if (!mounted) return
        setPoPolicyRequired(policy.policy === 'Required')
        if (policy.effectivePoNumber) {
          setPoNumber((current) => (current.trim() === '' ? policy.effectivePoNumber ?? '' : current))
        }
      })
      .catch(() => {
        if (mounted) setPoPolicyRequired(false)
      })
    return () => {
      mounted = false
    }
  }, [customerId])

  function handleCustomerChange(value: string) {
    setCustomerId(value)
    // Reset synchronously with the user event, not inside the effect.
    setOrders(null)
    setSelectedOrderIds([])
    setActivities(null)
    setSelectedActivityIds([])
    setPoNumber('')
    setPoPolicyRequired(false)
    // Wave 2 §4: hint only — the proposal engine that acts on the preference is Wave 10.
    setInvoiceGrouping(null)
    setDefaultVat(null)
    if (value) {
      getCustomer(value)
        .then((detail) => setInvoiceGrouping(detail.invoiceGrouping ?? null))
        .catch(() => {})
      getDefaultVatRate(value)
        .then((result) => {
          setDefaultVat(result.vatRatePercent)
          // Lines added before the answer arrived follow the customer too.
          setManualLines((rows) => rows.map((row) => (row.vatRatePercent === null ? { ...row, vatRatePercent: result.vatRatePercent } : row)))
        })
        .catch(() => setDefaultVat(null))
    }
  }

  function toggleOrder(id: string) {
    if (orders?.some((o) => o.id === id && o.isInvoiceable === false)) return // ACC-B1: not billable from here
    setSelectedOrderIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]))
  }

  function toggleActivity(id: string) {
    setSelectedActivityIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]))
  }

  function setManual(key: string, patch: Partial<ManualRow>) {
    setManualLines((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  const period = monthInputToPeriod(periodInput)
  // Warn (without blocking) when invoicing in a month before the invoice date's month (= today).
  const isPastPeriod = period !== null && comparePeriods(period, dateToPeriod(null)) < 0

  const estimatedSubtotal =
    (orders ?? [])
      .filter((o) => selectedOrderIds.includes(o.id))
      .reduce((sum, o) => sum + (o.agreedPrice ?? 0), 0) +
    (activities ?? [])
      .filter((a) => selectedActivityIds.includes(a.id))
      .reduce((sum, a) => sum + a.agreedPrice, 0) +
    // ACC-M2: each line rounds to currency precision exactly like the server (half away from zero).
    manualLines.reduce((sum, l) => sum + Math.round(Math.abs(l.quantity * l.unitPrice) * 100 + 1e-9) / 100 * Math.sign(l.quantity * l.unitPrice || 1), 0)

  async function handleCreate() {
    if (!customerId) {
      showError(t('invoices.new.selectCustomer'))
      return
    }
    if (selectedOrderIds.length === 0 && selectedActivityIds.length === 0 && manualLines.length === 0) {
      showError(t('invoices.new.needsSelection'))
      return
    }
    for (const line of manualLines) {
      if (!line.description.trim() || line.quantity <= 0) {
        showError(t('invoices.new.invalidManualLine'))
        return
      }
    }
    setBusy(true)
    try {
      const invoice = await createInvoice({
        customerId,
        invoiceDate: null,
        orderIds: selectedOrderIds,
        dossierActivityIds: selectedActivityIds,
        manualLines: manualLines.map((line) => ({
          description: line.description.trim(),
          quantity: line.quantity,
          unitPrice: line.unitPrice,
          vatRatePercent: line.vatRatePercent,
        })),
        notes: notes.trim() || null,
        purchaseOrderNumber: poNumber.trim() || null,
        legalEntityId: legalEntityId || null,
        invoicePeriodYear: period?.year ?? null,
        invoicePeriodMonth: period?.month ?? null,
      })
      // ACC-H3: a draft has no legal number yet; it gets one when it is sent.
      showSuccess(invoice.invoiceNumber ? t('invoices.new.created', { number: invoice.invoiceNumber }) : t('invoices.new.createdDraft'))
      navigate(`/invoices/${invoice.id}`)
    } catch (err) {
      // ACC-M4: the server names the problem (unpriced source, PO, …); never a generic sentence.
      showError(localizeApiError(t, err, t('invoices.new.createError')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('invoices.list.title'), to: '/invoices' }, { label: t('invoices.internalList.newInvoice') }]} />
      <UnsavedChangesGuard when={dirty && !busy} />
      <PageHeader title={t('invoices.internalList.newInvoice')} subtitle={t('invoices.new.subtitle')} />

      <div className="inv-builder">
        <FormField label={t('invoices.fields.customer')} htmlFor="inv-customer" required>
          {/* B19: searchable — the customer list of a real tenant runs into the thousands. */}
          <SearchableSelect
            id="inv-customer"
            value={customerId || null}
            onChange={(next) => handleCustomerChange(next ?? '')}
            options={customers.map((customer) => ({
              value: customer.id,
              label: `${customer.name} (${customer.customerNumber})`,
              keywords: customer.customerNumber,
            }))}
            placeholder={t('invoices.new.customerPlaceholder')}
            disabled={busy}
          />
        </FormField>
        {invoiceGrouping && invoiceGrouping !== 'Manual' && (
          <p className="inv-grouping-hint" role="note">
            {invoiceGrouping === 'PerDossier' && t('invoices.new.grouping.PerDossier')}
            {invoiceGrouping === 'Weekly' && t('invoices.new.grouping.Weekly')}
            {invoiceGrouping === 'Monthly' && t('invoices.new.grouping.Monthly')}
            {invoiceGrouping === 'ByReference' && t('invoices.new.grouping.ByReference')}
          </p>
        )}

        <div className="inv-entity-period">
          <FormField label={t('invoices.fields.billingEntity')} htmlFor="inv-legal-entity">
            <select
              id="inv-legal-entity"
              value={legalEntityId}
              onChange={(e) => setLegalEntityId(e.target.value)}
              disabled={busy}
            >
              {entities.length === 0 && <option value="">{t('invoices.new.noEntity')}</option>}
              {entities.map((entity) => (
                <option key={entity.id} value={entity.id}>
                  {entity.displayName}
                  {entity.isDefault ? ` ${t('invoices.new.defaultSuffix')}` : ''}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t('invoices.fields.period')} htmlFor="inv-period">
            <input
              id="inv-period"
              type="month"
              value={periodInput}
              onChange={(e) => setPeriodInput(e.target.value)}
              disabled={busy}
            />
          </FormField>
        </div>
        {nextNumber && (
          <p className="inv-next-number">
            {t('invoices.new.numberAtIssue', { number: nextNumber })}
          </p>
        )}
        {isPastPeriod && period && (
          <p className="inv-period-warning">{t('invoices.new.pastPeriod', { period: formatPeriod(period.year, period.month) })}</p>
        )}

        <FormField label={t('invoices.fields.poNumber')} htmlFor="inv-po" hint={t('invoices.new.poHint')}>
          <input id="inv-po" value={poNumber} onChange={(e) => setPoNumber(e.target.value)} disabled={busy} maxLength={100} />
        </FormField>
        {poPolicyRequired && poNumber.trim() === '' && (
          <p className="inv-period-warning">{t('invoices.new.poRequired')}</p>
        )}

        {customerId && orders !== null && (
          <section className="inv-section">
            <h3>{t('invoices.new.billableOrders', { total: orders.length })}</h3>
            {orders.length === 0 && (
              <p className="placeholder-text">{t('invoices.new.noOrders')}</p>
            )}
            {orders.length > 0 && (
              <table className="inv-orders-table">
                <thead>
                  <tr>
                    <th aria-label={t('invoices.new.orderColumns.selection')} />
                    <th>{t('invoices.new.orderColumns.number')}</th>
                    <th>{t('invoices.new.orderColumns.date')}</th>
                    <th>{t('invoices.new.orderColumns.goods')}</th>
                    <th>{t('invoices.new.orderColumns.route')}</th>
                    <th>{t('invoices.new.orderColumns.price')}</th>
                    <th>{t('invoices.new.orderColumns.invoicing')}</th>
                  </tr>
                </thead>
                <tbody>
                  {orders.map((order) => (
                    <tr key={order.id} className="inv-order-row" onClick={() => toggleOrder(order.id)}>
                      <td>
                        <input
                          type="checkbox"
                          checked={selectedOrderIds.includes(order.id)}
                          onChange={() => toggleOrder(order.id)}
                          onClick={(e) => e.stopPropagation()}
                          disabled={order.isInvoiceable === false}
                          title={order.isInvoiceable === false ? readinessTooltip(t, order.invoiceReadinessReasons) : undefined}
                          aria-label={t('invoices.new.selectOrder', { number: order.orderNumber })}
                        />
                      </td>
                      <td>
                        <code>{order.orderNumber}</code>
                      </td>
                      <td>{formatDate(order.orderDate)}</td>
                      <td className="inv-goods">{order.goodsDescription}</td>
                      <td>
                        {order.firstLoadingCity ?? '?'} → {order.lastUnloadingCity ?? '?'}
                      </td>
                      <td>{order.agreedPrice !== null ? euro(order.agreedPrice) : '—'}</td>
                      <td>
                        {order.invoiceReadiness === 'ReadyForInvoice' && (
                          <span className="inv-readiness inv-readiness-ready">{t('invoices.readiness.ready')}</span>
                        )}
                        {order.invoiceReadiness === 'ReviewRequired' && (
                          <span
                            className="inv-readiness inv-readiness-review"
                            title={readinessTooltip(t, order.invoiceReadinessReasons)}
                          >
                            {t('invoices.readiness.review')}
                          </span>
                        )}
                        {(!order.invoiceReadiness || order.invoiceReadiness === 'NotReady') && '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>
        )}

        {customerId && activities !== null && (
          <section className="inv-section">
            <h3>{t('invoices.new.billableActivities', { total: activities.length })}</h3>
            {activities.length === 0 && (
              <p className="placeholder-text">{t('invoices.new.noActivities')}</p>
            )}
            {activities.length > 0 && (
              <table className="inv-orders-table">
                <thead>
                  <tr>
                    <th aria-label={t('invoices.new.orderColumns.selection')} />
                    <th>{t('invoices.new.activityColumns.dossier')}</th>
                    <th>{t('invoices.new.activityColumns.activity')}</th>
                    <th>{t('invoices.new.activityColumns.plannedDate')}</th>
                    <th>{t('invoices.new.activityColumns.amount')}</th>
                  </tr>
                </thead>
                <tbody>
                  {activities.map((activity) => (
                    <tr key={activity.id} className="inv-order-row" onClick={() => toggleActivity(activity.id)}>
                      <td>
                        <input
                          type="checkbox"
                          checked={selectedActivityIds.includes(activity.id)}
                          onChange={() => toggleActivity(activity.id)}
                          onClick={(e) => e.stopPropagation()}
                          aria-label={t('invoices.new.selectActivity', {
                            activity: activity.activityTypeName,
                            dossier: activity.dossierNumber,
                          })}
                        />
                      </td>
                      <td>
                        <code>{activity.dossierNumber}</code>
                        {activity.dossierTitle && <div className="inv-goods">{activity.dossierTitle}</div>}
                      </td>
                      <td>
                        {activity.activityTypeName}
                        {activity.label && <div className="inv-goods">{activity.label}</div>}
                      </td>
                      <td>{activity.plannedDate ? formatDate(activity.plannedDate) : '—'}</td>
                      <td>
                        <span>{euro(activity.agreedPrice)}</span>
                        {activity.isFree && <span className="inv-free-badge">{t('invoices.new.freeActivity')}</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>
        )}

        <section className="inv-section">
          <div className="inv-manual-head">
            <h3>{t('invoices.new.manualTitle')}</h3>
            <Button
              variant="secondary"
              onClick={() =>
                setManualLines((rows) => [
                  ...rows,
                  // ACC-H1: the VAT follows the customer (server default when still unknown), never 0.
                  { key: `m-${++manualKey}`, description: '', quantity: 1, unitPrice: 0, vatRatePercent: defaultVat },
                ])
              }
              disabled={busy}
            >
              {t('invoices.internalLines.addLine')}
            </Button>
          </div>
          {manualLines.map((line) => (
            <div key={line.key} className="inv-manual-row">
              <input
                value={line.description}
                onChange={(e) => setManual(line.key, { description: e.target.value })}
                placeholder={t('invoices.new.manualDescriptionPlaceholder')}
                disabled={busy}
                maxLength={500}
              />
              {/* ACC-M2: three decimals for quantities, four for unit prices; nothing is rounded here. */}
              <input
                type="number"
                min={0.001}
                step="0.001"
                value={line.quantity}
                onChange={(e) => setManual(line.key, { quantity: Number(e.target.value) })}
                disabled={busy}
                aria-label={t('invoices.new.quantityLabel')}
              />
              <input
                type="number"
                min={0}
                step="0.0001"
                value={line.unitPrice}
                onChange={(e) => setManual(line.key, { unitPrice: Number(e.target.value) })}
                disabled={busy}
                aria-label={t('invoices.new.unitPriceLabel')}
              />
              <input
                type="number"
                min={0}
                max={100}
                step="0.5"
                value={line.vatRatePercent ?? ''}
                placeholder={t('invoices.new.vatFollowsCustomer')}
                onChange={(e) => setManual(line.key, { vatRatePercent: e.target.value === '' ? null : Number(e.target.value) })}
                disabled={busy}
                aria-label={t('invoices.new.vatLabel')}
                title={t('invoices.new.vatLabel')}
              />
              <button
                type="button"
                className="inv-link inv-link-danger"
                onClick={() => setManualLines((rows) => rows.filter((r) => r.key !== line.key))}
                disabled={busy}
              >
                {t('ui.actions.delete')}
              </button>
            </div>
          ))}
        </section>

        <FormField label={t('invoices.fields.notes')} htmlFor="inv-notes">
          <textarea id="inv-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={busy} maxLength={4000} />
        </FormField>

        <div className="inv-builder-footer">
          <span className="inv-estimate">{t('invoices.new.estimatedSubtotal')} <strong>{euro(estimatedSubtotal)}</strong></span>
          <span className="inv-builder-actions">
            <Button variant="secondary" onClick={() => navigate('/invoices')} disabled={busy}>
              {t('ui.actions.cancel')}
            </Button>
            <Button onClick={() => void handleCreate()} disabled={busy}>
              {busy ? t('invoices.common.busy') : t('invoices.new.create')}
            </Button>
          </span>
        </div>
      </div>
    </div>
  )
}
