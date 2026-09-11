import { useEffect, useImperativeHandle, useRef, useState, type FormEvent, type Ref } from 'react'
import { useNavigate } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import { saveOrderPriceLines, setOrderOneOffPrice } from '../../transport-orders/api/transportOrdersApi'
import { ORDER_PRICING_STATUS_LABELS, type OrderPricingLine, type TransportOrderDetail } from '../../transport-orders/types'
import { createOrderForActivity } from '../api/dossiersApi'
import { isDossierPriced } from '../dossierDisplay'
import type { DossierActivity, DossierDetail } from '../types'

export interface DossierPricePanelHandle {
  /** Focuses the control that resolves the "price" readiness field. */
  focusField: (field: string | null) => boolean
}

interface DossierPricePanelProps {
  dossier: DossierDetail
  /** First linked transport order (the price carrier); null while loading / without order. */
  order: TransportOrderDetail | null
  loading: boolean
  /** The linked order exists but could not be loaded (e.g. 404): show that instead of "add an activity". */
  orderUnavailable?: boolean
  /** First transport activity WITHOUT an order (offers "Transportopdracht aanmaken"). */
  activityWithoutOrder: DossierActivity | null
  /** dossiers.manage AND dossier open — governs create-order / add-activity actions. */
  canManage: boolean
  /** orders.edit|orders.manage AND dossier open — governs the agreed price. */
  canEditPrice: boolean
  /** orders.override_price|orders.manage AND dossier open — governs free sales lines. */
  canEditLines: boolean
  onOrderSaved: (order: TransportOrderDetail) => void
  onDossierUpdated: (dossier: DossierDetail) => void
  onConflict: (err: unknown) => boolean
  onAddActivity: () => void
  /** Re-fetches the target order after a failed load. */
  onRetryLoad?: () => void
  ref?: Ref<DossierPricePanelHandle>
}

const LOCKED_STATUSES = new Set(['Locked', 'Invoiced'])

function parseAmount(raw: string): number | null | undefined {
  const text = raw.trim().replace(',', '.')
  if (text === '') return null
  const value = Number(text)
  return Number.isFinite(value) && value >= 0 ? Math.round(value * 100) / 100 : undefined
}

/**
 * UX-sprint 2026-09-09 — Verkoop & prijs as a work surface, exposing the EXISTING pricing
 * concepts of the order instead of a new "price" field:
 *  - the dossier total (SUM of AgreedPrice over priced orders; "Nog geen prijs" when none is priced),
 *  - the agreed price = the order's one-off price agreement (PricingSource OneOff + fixed amount),
 *    saved through the price-only command POST /pricing/one-off (version-gated; never the full
 *    order PUT, which would re-validate an unrelated, still incomplete route),
 *  - the sales lines (TransportOrderPricingLine) with an inline free line (Kind Manual).
 * A manual override (PriceIsManual) or a locked pricing status keeps the panel read-only and
 * points to Prijsdetails; the dossier never invents a second source of truth.
 */
export function DossierPricePanel({
  dossier,
  order,
  loading,
  orderUnavailable = false,
  activityWithoutOrder,
  canManage,
  canEditPrice,
  canEditLines,
  onOrderSaved,
  onDossierUpdated,
  onConflict,
  onAddActivity,
  onRetryLoad,
  ref,
}: DossierPricePanelProps) {
  const { t } = useLocale()
  const toast = useToast()
  const navigate = useNavigate()
  const agreedInputRef = useRef<HTMLInputElement>(null)
  const lineLabelRef = useRef<HTMLInputElement>(null)
  const createOrderRef = useRef<HTMLButtonElement>(null)
  const addActivityRef = useRef<HTMLButtonElement>(null)
  const addLineButtonRef = useRef<HTMLButtonElement>(null)

  const [agreedInput, setAgreedInput] = useState(() => currentAgreedAmount(order))
  const [agreedError, setAgreedError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [addLineOpen, setAddLineOpen] = useState(false)
  const [lineLabel, setLineLabel] = useState('')
  const [lineQty, setLineQty] = useState('1')
  const [lineUnitPrice, setLineUnitPrice] = useState('')
  const [lineError, setLineError] = useState<string | null>(null)
  const pendingLineFocus = useRef(false)

  // The agreed-price field follows the order it belongs to (adjusted during render, see React's
  // "storing information from previous renders" pattern).
  const orderKey = order ? `${order.id}:${order.version}` : 'none'
  const [syncedOrderKey, setSyncedOrderKey] = useState(orderKey)
  if (syncedOrderKey !== orderKey) {
    setSyncedOrderKey(orderKey)
    setAgreedInput(currentAgreedAmount(order))
    setAgreedError(null)
  }

  useEffect(() => {
    if (pendingLineFocus.current && addLineOpen) {
      pendingLineFocus.current = false
      lineLabelRef.current?.focus()
    }
  }, [addLineOpen])

  const lines = order?.pricingLines ?? []
  const pricingStatus = order?.pricingSnapshot?.status ?? 'Draft'
  const locked = LOCKED_STATUSES.has(pricingStatus)
  const overridden = Boolean(order?.priceIsManual)
  const agreedEditable = Boolean(order) && canEditPrice && !locked && !overridden
  const linesEditable = Boolean(order) && canEditLines && !locked
  const calculatedTotal = calculatedTariffTotal(order)

  useImperativeHandle(ref, () => ({
    focusField: () => {
      if (agreedEditable && agreedInputRef.current) {
        agreedInputRef.current.focus()
        return true
      }
      if (linesEditable) {
        pendingLineFocus.current = true
        setAddLineOpen(true)
        return true
      }
      if (activityWithoutOrder && canManage && createOrderRef.current) {
        createOrderRef.current.focus()
        return true
      }
      if (addActivityRef.current) {
        addActivityRef.current.focus()
        return true
      }
      return false
    },
  }))

  async function saveAgreed(event: FormEvent) {
    event.preventDefault()
    if (!order || busy) return
    const amount = parseAmount(agreedInput)
    if (amount === undefined) {
      setAgreedError(t('dossierSheet.price.agreedInvalid'))
      return
    }
    setBusy(true)
    setAgreedError(null)
    try {
      // Price-only command (hardening 2026-09-11). The previous full order PUT echoed the loaded
      // stops and so re-validated an incomplete route ("Elke stop heeft een locatie …") — a valid
      // price must never depend on unrelated route data. Route rules stay on the route save.
      const updated = await setOrderOneOffPrice(order.id, { fixedAmount: amount, version: order.version })
      toast.showSuccess(amount === null ? t('dossierSheet.price.agreedCleared') : t('dossierSheet.price.agreedSaved'))
      onOrderSaved(updated)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setAgreedError(t('dossiers.orderDrawer.conflict'))
      } else {
        setAgreedError(describeApiError(err, t('dossierSheet.price.agreedSaveFailed')).message)
      }
    } finally {
      setBusy(false)
    }
  }

  async function submitLine(event: FormEvent) {
    event.preventDefault()
    if (!order || busy) return
    const label = lineLabel.trim()
    const quantity = parseAmount(lineQty)
    const unitPrice = parseAmount(lineUnitPrice)
    if (!label) {
      setLineError(t('dossierSheet.price.lineLabelRequired'))
      return
    }
    if (!quantity || unitPrice == null) {
      setLineError(t('dossierSheet.price.lineQtyRequired'))
      return
    }
    setBusy(true)
    setLineError(null)
    try {
      const updated = await saveOrderPriceLines(order.id, [
        { lineKey: null, label, quantity, unitPrice, amount: null, adjustReason: null, unit: null },
      ])
      toast.showSuccess(t('dossierSheet.price.lineSaved'))
      setAddLineOpen(false)
      setLineLabel('')
      setLineQty('1')
      setLineUnitPrice('')
      onOrderSaved(updated)
      addLineButtonRef.current?.focus()
    } catch (err) {
      setLineError(describeApiError(err, t('dossierSheet.price.lineSaveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function removeLine(line: OrderPricingLine) {
    if (!order || busy || !line.lineKey) return
    setBusy(true)
    try {
      const updated = await saveOrderPriceLines(order.id, [
        { lineKey: line.lineKey, label: line.label, quantity: null, unitPrice: null, amount: null, adjustReason: null, remove: true },
      ])
      toast.showSuccess(t('dossierSheet.price.lineRemoved'))
      onOrderSaved(updated)
    } catch (err) {
      toast.showError(describeApiError(err, t('dossierSheet.price.lineSaveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function createOrder() {
    if (!activityWithoutOrder || busy) return
    setBusy(true)
    try {
      const updated = await createOrderForActivity(dossier.id, activityWithoutOrder.id, dossier.version)
      toast.showSuccess(t('dossierSheet.price.orderCreated'))
      onDossierUpdated(updated)
    } catch (err) {
      if (!onConflict(err)) toast.showError(describeApiError(err, t('dossierSheet.route.createOrderFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  const priced = isDossierPriced(dossier)
  const pricingIssues = dossier.readiness.filter((issue) => issue.code.startsWith('pricing.') && issue.severity !== 'Info')
  const lineTotal = (line: OrderPricingLine) => (line.proposed ? null : line.amount)
  // Hardening 2026-09-10: the total is a SUM over the PRICED orders only. With several orders
  // of which some are unpriced, the amount must never read as "the dossier is priced".
  const pricedCount = dossier.financials.pricedOrderCount ?? dossier.orders.filter((o) => o.isPriced ?? (o.agreedPrice != null && o.agreedPrice > 0)).length
  const partial = priced && dossier.orders.length > 1 && pricedCount < dossier.orders.length
  const hasTransportActivity = dossier.activities.some((a) => a.hasStops)
  const standaloneOnly = dossier.activities.length > 0 && !hasTransportActivity

  return (
    <div className="dossier-price-panel">
      <p className="dossier-price-total">
        {priced ? (
          <strong>{euro(dossier.financials.agreedOrderTotal)}</strong>
        ) : (
          <strong className="dossier-price-none">{t('dossierSheet.price.noPrice')}</strong>
        )}
        {partial && (
          <span className="dossier-price-partial">{t('dossierSheet.price.partialTotal', { priced: pricedCount, total: dossier.orders.length })}</span>
        )}
        {pricingIssues.length > 0 && (
          <span className="dossier-price-warning">⚠ {t('dossiers.price.attention', { count: pricingIssues.length })}</span>
        )}
      </p>

      {dossier.orders.length > 1 && (
        <ul className="dossier-price-lines">
          {dossier.orders.map((o) => (
            <li key={o.linkId}>
              <span>
                <code>{o.orderNumber}</code>
                {o.goodsDescription && ` ${o.goodsDescription}`}
              </span>
              <span>{(o.isPriced ?? (o.agreedPrice != null && o.agreedPrice > 0)) ? euro(o.agreedPrice ?? 0) : '—'}</span>
            </li>
          ))}
        </ul>
      )}

      {loading && <p className="placeholder-text">{t('dossiers.route.loading')}</p>}

      {!loading && !order && orderUnavailable && (
        <div className="dossier-price-noorder">
          <p className="placeholder-text">{t('dossiers.route.loadFailed')}</p>
          {onRetryLoad && (
            <Button variant="secondary" onClick={onRetryLoad}>
              {t('dossierSheet.route.retryLoad')}
            </Button>
          )}
        </div>
      )}

      {!loading && !order && !orderUnavailable && standaloneOnly && (
        // Standalone activities (opslag, kraanwerk) have no price carrier in this release
        // (docs/dossiers.md). Say so plainly — never suggest a transport order to attach a price.
        <div className="dossier-price-noorder">
          <p className="placeholder-text">{t('dossierSheet.price.standaloneOnly')}</p>
        </div>
      )}

      {!loading && !order && !orderUnavailable && !standaloneOnly && (
        <div className="dossier-price-noorder">
          <p>{t('dossierSheet.price.noOrderTitle')}</p>
          {activityWithoutOrder && canManage ? (
            <Button ref={createOrderRef} variant="secondary" onClick={() => void createOrder()} disabled={busy}>
              {t('dossierSheet.price.noOrderCreate')}
            </Button>
          ) : (
            <>
              <p className="placeholder-text">{t('dossierSheet.price.noOrderNoActivity')}</p>
              {canManage && (
                <Button ref={addActivityRef} variant="secondary" onClick={onAddActivity} disabled={busy}>
                  {t('dossiers.activities.add')}
                </Button>
              )}
            </>
          )}
          {dossier.activities.some((a) => !a.hasStops) && (
            <p className="placeholder-text">{t('dossierSheet.price.standaloneNote')}</p>
          )}
        </div>
      )}

      {order && (
        <>
          {dossier.orders.length > 1 && (
            <p className="dossier-price-order-label">{t('dossierSheet.price.forOrder', { number: order.orderNumber })}</p>
          )}

          {overridden && (
            <p className="dossier-price-note">
              {t('dossierSheet.price.manualOverride', {
                amount: euro(order.agreedPrice ?? 0),
                reason: order.priceOverrideReason ?? '—',
              })}
            </p>
          )}
          {locked && (
            <p className="dossier-price-note">
              {t('dossierSheet.price.locked', { status: t(ORDER_PRICING_STATUS_LABELS[pricingStatus]) })}
            </p>
          )}

          {agreedEditable && (
            <form className="dossier-price-agreed" onSubmit={(event) => void saveAgreed(event)}>
              <FormField
                label={t('dossierSheet.price.agreedLabel')}
                htmlFor="dossier-agreed-price"
                error={agreedError ?? undefined}
                hint={
                  calculatedTotal != null
                    ? t('dossierSheet.price.agreedHintCalculated', { amount: euro(calculatedTotal) })
                    : t('dossierSheet.price.agreedHintFree')
                }
              >
                <div className="dossier-price-agreed-row">
                  <input
                    ref={agreedInputRef}
                    id="dossier-agreed-price"
                    type="text"
                    inputMode="decimal"
                    value={agreedInput}
                    onChange={(event) => setAgreedInput(event.target.value)}
                    disabled={busy}
                    aria-invalid={agreedError ? true : undefined}
                    placeholder="0,00"
                  />
                  <Button type="submit" disabled={busy || agreedInput.trim() === currentAgreedAmount(order)}>
                    {t('dossierSheet.price.agreedSave')}
                  </Button>
                </div>
              </FormField>
            </form>
          )}

          <div className="dossier-price-lines-block">
            <h3>{t('dossierSheet.price.linesTitle')}</h3>
            {lines.length === 0 && <p className="placeholder-text">{t('dossierSheet.price.noLinesYet')}</p>}
            {lines.length > 0 && (
              <table className="dossier-price-table">
                <thead>
                  <tr>
                    <th scope="col">{t('dossierSheet.price.colLabel')}</th>
                    <th scope="col" className="num">{t('dossierSheet.price.colQty')}</th>
                    <th scope="col" className="num">{t('dossierSheet.price.colUnitPrice')}</th>
                    <th scope="col" className="num">{t('dossierSheet.price.colAmount')}</th>
                    <th scope="col">{t('dossierSheet.price.colSource')}</th>
                    {linesEditable && <th scope="col" />}
                  </tr>
                </thead>
                <tbody>
                  {lines.map((line, index) => (
                    <tr key={line.lineKey ?? line.id ?? index} className={line.proposed || line.informational ? 'is-muted' : undefined}>
                      <td>{line.label}</td>
                      <td className="num">{line.quantity ?? '—'}</td>
                      <td className="num">{line.unitPrice != null ? euro(line.unitPrice) : '—'}</td>
                      <td className="num">{lineTotal(line) != null ? euro(lineTotal(line)!) : '—'}</td>
                      <td>
                        {line.source}
                        {line.proposed && ` · ${t('dossierSheet.price.proposed')}`}
                        {line.informational && ` · ${t('dossierSheet.price.informational')}`}
                      </td>
                      {linesEditable && (
                        <td className="actions">
                          {line.kind === 'Manual' && line.lineKey && (
                            <button type="button" className="link-button" onClick={() => void removeLine(line)} disabled={busy}>
                              {t('dossierSheet.price.removeLine')}
                            </button>
                          )}
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            )}

            {linesEditable && !addLineOpen && (
              <p className="dossier-price-actions">
                <Button ref={addLineButtonRef} variant="secondary" onClick={() => setAddLineOpen(true)} disabled={busy}>
                  {t('dossierSheet.price.addLine')}
                </Button>
                <Button variant="ghost" onClick={() => navigate(`/transport-orders/${order.id}`)}>
                  {t('dossiers.price.details')}
                </Button>
              </p>
            )}
            {linesEditable && addLineOpen && (
              <form className="dossier-price-addline" onSubmit={(event) => void submitLine(event)} aria-label={t('dossierSheet.price.addLine')}>
                <FormField label={t('dossierSheet.price.lineLabel')} htmlFor="dossier-line-label" required error={lineError ?? undefined}>
                  <input
                    ref={lineLabelRef}
                    id="dossier-line-label"
                    value={lineLabel}
                    onChange={(event) => setLineLabel(event.target.value)}
                    disabled={busy}
                    maxLength={300}
                  />
                </FormField>
                <FormField label={t('dossierSheet.price.lineQty')} htmlFor="dossier-line-qty">
                  <input id="dossier-line-qty" inputMode="decimal" value={lineQty} onChange={(event) => setLineQty(event.target.value)} disabled={busy} />
                </FormField>
                <FormField label={t('dossierSheet.price.lineUnitPrice')} htmlFor="dossier-line-unitprice">
                  <input
                    id="dossier-line-unitprice"
                    inputMode="decimal"
                    value={lineUnitPrice}
                    onChange={(event) => setLineUnitPrice(event.target.value)}
                    disabled={busy}
                  />
                </FormField>
                <div className="dossier-price-addline-actions">
                  <Button
                    variant="ghost"
                    onClick={() => {
                      setAddLineOpen(false)
                      setLineError(null)
                    }}
                    disabled={busy}
                  >
                    {t('dossierSheet.price.lineCancel')}
                  </Button>
                  <Button type="submit" disabled={busy}>
                    {t('dossierSheet.price.lineAdd')}
                  </Button>
                </div>
              </form>
            )}
            {!linesEditable && (
              <p className="dossier-price-actions">
                <Button variant="ghost" onClick={() => navigate(`/transport-orders/${order.id}`)}>
                  {t('dossiers.price.details')}
                </Button>
              </p>
            )}
          </div>
        </>
      )}
    </div>
  )
}

/** Text shown in the agreed-price input for the order's current price agreement ('' = none). */
function currentAgreedAmount(order: TransportOrderDetail | null): string {
  if (!order) return ''
  if (order.pricingSource === 'OneOff' && order.oneOffFixedAmount != null) return String(order.oneOffFixedAmount)
  // Legacy agreed price (no pricing configuration, no lines): show it so the planner sees what is stored.
  const hasLines = (order.pricingLines ?? []).some((line) => !line.informational && !line.proposed)
  if (!hasLines && order.agreedPrice != null && order.agreedPrice > 0 && !order.priceIsManual) return String(order.agreedPrice)
  return ''
}

/** The tariff-calculated total when the order is priced from the contract (null otherwise). */
function calculatedTariffTotal(order: TransportOrderDetail | null): number | null {
  if (!order || order.pricingSource === 'OneOff') return null
  const total = order.pricingSnapshot?.calculatedTotal
  if (total == null || total <= 0) return null
  const hasEngineLines = (order.pricingLines ?? []).some((line) => (line.kind === 'Auto' || line.kind === 'AutoAdjusted') && line.amount > 0)
  return hasEngineLines ? total : null
}
