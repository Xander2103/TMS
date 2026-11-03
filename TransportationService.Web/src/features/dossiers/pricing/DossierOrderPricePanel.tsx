import { useImperativeHandle, useRef, useState, type FormEvent, type Ref } from 'react'
import { useNavigate } from 'react-router-dom'
import { ArrowUpRight, Boxes, Calculator, Pencil, Trash2 } from 'lucide-react'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { SaveStatus } from '../../../components/ui/SaveStatus'
import { isRetryableSaveError, useSaveStatus } from '../../../components/ui/useSaveStatus'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate, formatTime } from '../../../utils/dates'
import { lineTotal } from '../../../utils/money'
import { formatCurrency, formatQuantity, parseDecimalInput } from '../../../utils/numbers'
import { euro } from '../../invoices/types'
import { useUnitTypes } from '../../master-data/hooks/useUnitTypes'
import { setOrderOneOffPrice } from '../../transport-orders/api/transportOrdersApi'
import { CargoCoverageBadge } from '../../transport-orders/components/CargoCoverageBadge'
import { describeCargoItem, unitLabelFrom, type UnitLabelResolver } from '../../transport-orders/pricing/cargoLabels'
import { OrderPricingDialogs } from '../../transport-orders/pricing/OrderPricingDialogs'
import { RedeliveryBillingDecision } from '../../transport-orders/pricing/RedeliveryBillingDecision'
import { useOrderPricingEditor } from '../../transport-orders/pricing/useOrderPricingEditor'
import {
  ORDER_PRICE_LINE_KIND_TONE,
  ORDER_PRICING_STATUS_LABELS,
  ORDER_STATUS_LABELS,
  PRICING_COVERAGE_LABELS,
  PRICING_COVERAGE_TONE,
  lineBadge,
  type OrderPricingLine,
  type TransportOrderDetail,
} from '../../transport-orders/types'
import type { DossierActivity, DossierDetail } from '../types'
import { PriceMethodChoice, type PriceMethod } from './PriceMethodChoice'
import { FixedPriceSection, PriceFold } from './PriceSections'
import { PriceUnitSummary } from './PriceUnitSummary'
import '../../transport-orders/pages/transport-orders.css'
import '../../transport-orders/detail/order-detail.css'
import '../../transport-orders/pricing/order-pricing.css'
import './dossier-pricing.css'

export interface DossierOrderPricePanelHandle {
  /** Focuses the agreed-price input, else opens the add-line dialog; false when nothing is editable. */
  focusField: () => boolean
}

interface DossierOrderPricePanelProps {
  dossier: DossierDetail
  /** The transport activity whose order carries the price (null for a legacy target). */
  activity: DossierActivity | null
  order: TransportOrderDetail
  /** orders.edit|orders.manage AND dossier open — governs the agreed (one-off) price. */
  canEditPrice: boolean
  /** The dossier is open; a closed dossier makes every order pricing action read-only as well. */
  isOpen: boolean
  onOrderSaved: (order: TransportOrderDetail) => void
  onSelectUnit?: (activityId: string) => void
  selectLocked?: boolean
  ref?: Ref<DossierOrderPricePanelHandle>
}

const LOCKED_STATUSES = new Set(['Locked', 'Invoiced'])
// R-N1: the statuses in which the server accepts the one-off price command (SetOneOffPriceAsync).
// From planning on, the price only moves through the sales lines.
const PRICE_AGREEMENT_STATUSES = new Set(['Draft', 'Submitted', 'Confirmed'])

/** '' → null (clear), a valid amount ≥ 0 → cents-rounded number, anything else → undefined (invalid). */
function parseAmount(raw: string): number | null | undefined {
  if (raw.trim() === '') return null
  const value = parseDecimalInput(raw)
  return value !== null && value >= 0 ? Math.round(value * 100) / 100 : undefined
}

/** Provenance of a linked order from the dossier payload (activity entry first, then the order link, then the loaded order itself). */
function isOrderPriced(dossier: DossierDetail, order: TransportOrderDetail): boolean {
  const entry = dossier.activities.find((a) => a.linkedTransportOrderId === order.id)
  if (entry && typeof entry.isPriced === 'boolean') return entry.isPriced
  const link = dossier.orders.find((o) => o.orderId === order.id)
  if (link?.isPriced !== undefined) return link.isPriced
  return Boolean(order.priceIsManual) || (order.pricingSource === 'OneOff' && order.oneOffFixedAmount != null) || (order.agreedPrice != null && order.agreedPrice > 0)
}

/** Text shown in the agreed-price input for the order's current price agreement ('' = none). */
function currentAgreedAmount(order: TransportOrderDetail): string {
  if (order.pricingSource === 'OneOff' && order.oneOffFixedAmount != null) return String(order.oneOffFixedAmount)
  // Legacy agreed price (no pricing configuration, no lines): show it so the planner sees what is stored.
  const hasLines = (order.pricingLines ?? []).some((line) => !line.informational && !line.proposed)
  if (!hasLines && order.agreedPrice != null && order.agreedPrice > 0 && !order.priceIsManual) return String(order.agreedPrice)
  return ''
}

/** The tariff-calculated total when the order is priced from the contract (null otherwise). */
function calculatedTariffTotal(order: TransportOrderDetail): number | null {
  if (order.pricingSource === 'OneOff') return null
  const total = order.pricingSnapshot?.calculatedTotal
  if (total == null || total <= 0) return null
  const hasEngineLines = (order.pricingLines ?? []).some((line) => (line.kind === 'Auto' || line.kind === 'AutoAdjusted') && line.amount > 0)
  return hasEngineLines ? total : null
}

/**
 * The Berekening column: quantity × unit × unit price when known AND consistent with the shown
 * amount (an amount-only adjustment leaves a stale quantity on the stored line), else a flat
 * amount or "—". The same rule as on the order page.
 */
function calculationLabel(line: OrderPricingLine, unitLabel: UnitLabelResolver, flatAmount: string): string {
  if (line.quantity != null && line.unitPrice != null && lineTotal(line.quantity, line.unitPrice) === line.amount) {
    const unit = line.unit ? ` ${unitLabel(line.unit, line.unit)}` : ''
    return `${formatQuantity(line.quantity)}${unit} × ${formatCurrency(line.unitPrice)}`
  }
  return line.kind === 'Manual' ? flatAmount : '—'
}

/**
 * The price of an ORDER-BACKED activity (transport) on the dossier price tab. The dossier never
 * invents a second source of truth: the agreed price is the order's one-off price agreement
 * (price-only command, version-gated — never the full order PUT, which would re-validate an
 * unrelated, still incomplete route), and the sales lines are edited through THE order price line
 * editor (`useOrderPricingEditor` + `OrderPricingDialogs`), with the same rules as on the order
 * page; only the presentation is the dossier's own (redesign 2026-11-03): free lines, line adjust/remove with reason, proposals, recalculation, coverage
 * and stale warnings, confirm/reopen, and the goods ↔ sales-line link (D4).
 *
 * Pricing UX sprint 2026-10-08: an order has no separate "free" confirmation — the one-off price
 * command at € 0 IS the planner's explicit decision. So entering 0 asks for that confirmation
 * BEFORE it is sent (cancelling keeps the previous value), and a deliberate € 0 reads as "Gratis"
 * with a quiet note instead of a warning that would contradict the status.
 */
export function DossierOrderPricePanel({ dossier, activity, order, canEditPrice, isOpen, onOrderSaved, onSelectUnit, selectLocked, ref }: DossierOrderPricePanelProps) {
  const { t } = useLocale()
  // B31: the agreed price is edited in place — its save state stays beside the field.
  const agreedStatus = useSaveStatus()
  // The amount of the last attempt, re-sent by "Opnieuw proberen".
  const [lastAgreedAmount, setLastAgreedAmount] = useState<number | null>(null)
  const navigate = useNavigate()
  const agreedInputRef = useRef<HTMLInputElement>(null)
  const units = useUnitTypes()
  const editor = useOrderPricingEditor(order, { onOrderChanged: onOrderSaved, readOnly: !isOpen })

  const [agreedInput, setAgreedInput] = useState(() => currentAgreedAmount(order))
  const [agreedError, setAgreedError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  // A typed € 0 waits here for the planner's explicit "yes, free" before it is sent.
  const [confirmZero, setConfirmZero] = useState(false)
  // B13: how an UNPRICED order gets its price; only that answer's controls are shown.
  const [method, setMethod] = useState<PriceMethod>('agreed')
  // Priced through sales lines: the fixed price only comes forward when asked for.
  const [flatForced, setFlatForced] = useState(false)

  // The agreed-price field follows the order it belongs to (adjusted during render, see React's
  // "storing information from previous renders" pattern).
  const orderKey = `${order.id}:${order.version}`
  const [syncedOrderKey, setSyncedOrderKey] = useState(orderKey)
  if (syncedOrderKey !== orderKey) {
    setSyncedOrderKey(orderKey)
    setAgreedInput(currentAgreedAmount(order))
    setAgreedError(null)
    setMethod('agreed')
    setFlatForced(false)
  }

  const pricingStatus = order.pricingSnapshot?.status ?? 'Draft'
  const locked = LOCKED_STATUSES.has(pricingStatus)
  const overridden = Boolean(order.priceIsManual)
  const agreementOpen = PRICE_AGREEMENT_STATUSES.has(order.status)
  const agreedEditable = canEditPrice && !locked && !overridden && agreementOpen
  const linesEditable = editor.canEditPricingLines && !locked
  const calculatedTotal = calculatedTariffTotal(order)
  // Intentional € 0 on the target order: priced (provenance from the dossier payload) AND amount 0
  // — a deliberate free delivery, settled by the planner's own one-off decision ("Gratis").
  const orderIsPriced = isOrderPriced(dossier, order)
  const orderIsFree = activity?.priceStatus === 'Free' || (orderIsPriced && order.agreedPrice === 0)
  const hasLines = (order.pricingLines ?? []).length > 0
  // A real sales line (not an engine notice) means the planner already chose lines.
  const hasSalesLines = (order.pricingLines ?? []).some((line) => !line.informational && !line.proposed)
  const undecided = agreedEditable && !orderIsPriced && !hasSalesLines
  // Priced through sales lines (no one-off agreement): the agreed price steps aside until asked for.
  const pricedByLines = hasSalesLines && order.pricingSource !== 'OneOff'
  const showAgreed = agreedEditable && (undecided ? method === 'agreed' : !pricedByLines || flatForced)
  const showLines = !undecided || method === 'lines'

  function chooseMethod(next: PriceMethod) {
    setAgreedError(null)
    setMethod(next)
    // Choosing sales lines opens the add-line dialog right away: one click less.
    if (next === 'lines' && linesEditable) editor.openAddLine()
  }

  useImperativeHandle(ref, () => ({
    focusField: () => {
      if (agreedEditable && agreedInputRef.current) {
        agreedInputRef.current.focus()
        return true
      }
      if (linesEditable) {
        editor.openAddLine()
        return true
      }
      return false
    },
  }))

  async function saveAgreed(event: FormEvent) {
    event.preventDefault()
    if (busy) return
    const amount = parseAmount(agreedInput)
    if (amount === undefined) {
      setAgreedError(t('dossierSheet.price.agreedInvalid'))
      return
    }
    // € 0 is never sent silently: the planner confirms that the order is deliberately free first.
    if (amount === 0 && currentAgreedAmount(order) !== '0') {
      setConfirmZero(true)
      return
    }
    await persistAgreed(amount)
  }

  async function persistAgreed(amount: number | null) {
    setBusy(true)
    setAgreedError(null)
    setLastAgreedAmount(amount)
    agreedStatus.setState({ kind: 'saving' })
    try {
      // Price-only command (hardening 2026-09-11). The previous full order PUT echoed the loaded
      // stops and so re-validated an incomplete route ("Elke stop heeft een locatie …") — a valid
      // price must never depend on unrelated route data. Route rules stay on the route save.
      // ACC-M1: € 0 only reaches here after the explicit "Bevestigen als gratis" step, so it is
      // sent with the flag the server requires to distinguish "gratis" from "niet geprijsd".
      const updated = await setOrderOneOffPrice(order.id, amount === 0
        ? { fixedAmount: 0, version: order.version, freeConfirmed: true }
        : { fixedAmount: amount, version: order.version })
      agreedStatus.setState({ kind: 'saved', at: new Date().toISOString() })
      setConfirmZero(false)
      onOrderSaved(updated)
    } catch (err) {
      setConfirmZero(false)
      agreedStatus.setState(isRetryableSaveError(err) ? { kind: 'error' } : { kind: 'idle' })
      if (err instanceof ApiError && err.status === 409) {
        setAgreedError(t('dossiers.orderDrawer.conflict'))
      } else {
        setAgreedError(describeApiError(err, t('dossierSheet.price.agreedSaveFailed')).message)
      }
    } finally {
      setBusy(false)
    }
  }

  const unitLabel = unitLabelFrom(units.displayUnits)
  const {
    priceDisplay, totalPrice, pricingBusy, pricingLocked, canLockPrice, canEditPricingStatus, canEditPricingLines,
    invoiceLines, notAppliedLines, coverage, unpricedCoverage,
  } = editor
  const canEditLineRows = canEditPricingLines && !pricingLocked
  // ONE primary per state: confirm while there is a price to confirm; once confirmed only "Prijs aanpassen".
  const canConfirm = canLockPrice && (editor.pricingStatus === 'Draft' || editor.pricingStatus === 'Reviewed') && orderIsPriced
  const stale = Boolean(order.pricingSnapshot?.isStale)
  const coveredByAgreedPrice = order.pricingSource === 'OneOff' && order.oneOffFixedAmount != null
  // Goods without a tariff under an AGREED trip price are information; before confirmation they need attention.
  const coverageTone: 'info' | 'attention' = coveredByAgreedPrice || pricingLocked ? 'info' : 'attention'
  const hasCalcDetails = Boolean(order.pricingSnapshot) || notAppliedLines.length > 0
  const hasCommercialCoverage = order.cargoItems.some((item) => item.commercialCoverage)
  const confirmedAt = order.pricingSnapshot?.confirmedAtUtc
  const confirmedTime = confirmedAt ? formatTime(confirmedAt) : ''
  const linkedGoods = (line: OrderPricingLine): string =>
    (line.cargoItemIds ?? [])
      .map((id) => order.cargoItems.find((item) => item.id === id))
      .filter((item) => item !== undefined)
      .map((item) => describeCargoItem(item, unitLabel))
      .join('; ')

  const agreedForm = (
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
          <SaveStatus state={agreedStatus.state} onRetry={() => void persistAgreed(lastAgreedAmount)} />
        </div>
      </FormField>
    </form>
  )

  return (
    <div className="dossier-order-price">
      <PriceUnitSummary
        dossier={dossier}
        activity={activity}
        fallbackTitle={order.orderNumber}
        total={orderIsPriced ? formatCurrency(totalPrice ?? 0) : t('dossierSheet.price.noPrice')}
        unpriced={!orderIsPriced}
        statusFallback={t(priceDisplay.labelKey)}
        statusDetail={t('dossierPricing.panel.orderStatus', { status: t(ORDER_STATUS_LABELS[order.status]) })}
        onSelectUnit={onSelectUnit}
        selectLocked={selectLocked}
        actions={
          showLines && (canEditLineRows || (canEditPricingStatus && !pricingLocked && stale) || (canLockPrice && editor.pricingStatus === 'Locked') || canConfirm) ? (
            <>
              {canEditLineRows && (
                <Button variant="secondary" onClick={editor.openAddLine} disabled={pricingBusy}>
                  {t('dossierPricing.addLine')}
                </Button>
              )}
              {canEditPricingStatus && !pricingLocked && stale && (
                <Button variant="secondary" onClick={editor.handleRecalculateClick} disabled={pricingBusy}>
                  {t('dossierPricing.panel.recalculate')}
                </Button>
              )}
              {canLockPrice && editor.pricingStatus === 'Locked' && (
                <Button variant="secondary" onClick={editor.openReopenPrice} disabled={pricingBusy}>
                  <Pencil size={15} aria-hidden="true" /> {t('dossierPricing.panel.adjustPrice')}
                </Button>
              )}
              {canConfirm && (
                <Button onClick={editor.handleConfirmPriceClick} disabled={pricingBusy}>
                  <Pencil size={15} aria-hidden="true" /> {t('dossierPricing.panel.confirmPrice')}
                </Button>
              )}
            </>
          ) : undefined
        }
      />

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
      {canEditPrice && !locked && !agreementOpen && !orderIsFree && (
        <p className="dossier-price-note">
          {t('dossierPricing.postExecution.agreementClosed')}
          {!order.postExecutionFreeAllowed && ` ${t('dossierPricing.postExecution.freeClosed')}`}
        </p>
      )}
      {orderIsFree && <p className="dossier-price-note dossier-price-free-note">{t('dossierPricing.order.freeNote')}</p>}
      {confirmedAt && (
        <p className="dossier-price-note">
          {t('dossierPricing.panel.confirmedAt', { date: formatDate(confirmedAt), time: confirmedTime })}
          {order.pricingSnapshot?.confirmedByName ? ` ${t('dossierPricing.panel.confirmedBy', { name: order.pricingSnapshot.confirmedByName })}` : ''}
        </p>
      )}
      {order.pricingSnapshot?.confirmedWithUnpricedGoodsReason && (
        <p className="order-price-note" role="note">
          {t('dossierPricing.panel.confirmedUnpriced', { reason: order.pricingSnapshot.confirmedWithUnpricedGoodsReason })}
        </p>
      )}
      {stale && (
        <p className="to-price-summary-warning" role="alert">
          {t('dossierPricing.panel.staleWarning')}
        </p>
      )}

      {!orderIsPriced && agreedEditable && <p className="dossier-price-none">{t('dossierPricing.noPriceSet')}</p>}
      {undecided && <PriceMethodChoice value={method} onChange={chooseMethod} disabled={busy} />}
      {undecided && method === 'free' && (
        <div className="dossier-price-free-choice">
          <p>{t('dossierPricing.method.freeExplainOrder', { number: order.orderNumber })}</p>
          <Button onClick={() => void persistAgreed(0)} disabled={busy}>
            {t('dossierPricing.method.freeConfirm')}
          </Button>
          {agreedError && <p className="dossier-activity-lines-error" role="alert">{agreedError}</p>}
        </div>
      )}
      {undecided && showAgreed && agreedForm}

      {showLines && (
        <RedeliveryBillingDecision
          order={order}
          canDecide={canEditPricingLines}
          busy={pricingBusy}
          onConfirm={editor.confirmFreeAfterExecution}
        />
      )}

      {showLines && hasLines && (
        <section className="dossier-price-card">
          <h3 className="dossier-price-card-title">
            {t('dossierPricing.activity.linesTitle')} <Badge tone={priceDisplay.tone}>{t(priceDisplay.labelKey)}</Badge>
          </h3>
          {unpricedCoverage.length > 0 && (
            <div className={coverageTone === 'info' ? 'order-coverage-note' : 'order-coverage-attention'} role="note">
              <strong>
                {t(coveredByAgreedPrice
                  ? 'dossierPricing.panel.coverageAgreed'
                  : pricingLocked
                    ? 'dossierPricing.panel.coverageLocked'
                    : 'dossierPricing.panel.coverageOpen')}
              </strong>
              {!coveredByAgreedPrice && (
                <ul>
                  {unpricedCoverage.map((c, index) => (
                    <li key={c.unitTypeId ?? `${c.unitLabel}-${index}`}>
                      {formatQuantity(c.quantity)} {c.unitLabel}:{' '}
                      {(c.reason ?? t('dossierPricing.panel.noBaseTariff')).toLowerCase()}
                      {c.servicesAmount > 0 && ` — ${t('dossierPricing.panel.servicesOnly', { amount: formatCurrency(c.servicesAmount) })}`}
                    </li>
                  ))}
                </ul>
              )}
              {coverageTone === 'attention' && <p className="order-coverage-hint">{t('dossierPricing.panel.coverageHint')}</p>}
            </div>
          )}
          <div className="dossier-price-table-wrap">
            <table className="dossier-price-lines-table">
              <thead>
                <tr>
                  <th scope="col">{t('dossierPricing.activity.colLabel')}</th>
                  <th scope="col">{t('dossierPricing.panel.colType')}</th>
                  <th scope="col">{t('dossierPricing.panel.colCalculation')}</th>
                  <th scope="col" className="num">{t('dossierPricing.activity.colAmount')}</th>
                  {canEditLineRows && <th scope="col" className="actions">{t('dossierPricing.activity.colActions')}</th>}
                </tr>
              </thead>
              <tbody>
                {invoiceLines.map((line, index) => (
                  <tr key={line.id ?? line.lineKey ?? index} className={line.informational ? 'is-informational' : undefined}>
                    <td>
                      {line.label}
                      {line.kind === 'AutoAdjusted' && (
                        <div className="to-price-original">
                          <s>
                            {line.originalQuantity != null && line.originalUnitPrice != null
                              ? `${formatQuantity(line.originalQuantity)} × ${formatCurrency(line.originalUnitPrice)}`
                              : formatCurrency(line.originalAmount ?? 0)}
                          </s>
                          {' → '}
                          {line.quantity != null && line.unitPrice != null
                            ? `${formatQuantity(line.quantity)} × ${formatCurrency(line.unitPrice)}`
                            : formatCurrency(line.amount)}
                        </div>
                      )}
                      {linkedGoods(line) && (
                        <div className="order-price-line-goods">{t('dossierPricing.goodsLink.linked', { goods: linkedGoods(line) })}</div>
                      )}
                    </td>
                    <td>
                      <Badge tone={ORDER_PRICE_LINE_KIND_TONE[line.kind]}>{t(lineBadge(line))}</Badge>
                    </td>
                    <td>{calculationLabel(line, unitLabel, t('dossierPricing.panel.flatAmount'))}</td>
                    <td className="num">{formatCurrency(line.amount)}</td>
                    {canEditLineRows && (
                      <td className="actions">
                        {line.kind === 'Proposed' ? (
                          <Button variant="ghost" onClick={() => editor.handleConfirmLine(line)} disabled={pricingBusy}>
                            {t('dossierPricing.panel.confirmLine')}
                          </Button>
                        ) : !line.lineKey ? null : (
                          // A line without a LineKey (the engine's "geen tarief" diagnostics) cannot be
                          // addressed by the API: an edit would be sent as a NEW free line and refused.
                          <>
                            <Button variant="ghost" onClick={() => editor.openEditLine(line)} disabled={pricingBusy}>
                              <Pencil size={15} aria-hidden="true" /> {t('dossierPricing.panel.editLine')}
                            </Button>
                            <Button variant="ghost" onClick={() => editor.openRemoveLine(line)} disabled={pricingBusy}>
                              <Trash2 size={15} aria-hidden="true" /> {t('dossierPricing.panel.removeLine')}
                            </Button>
                          </>
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <th scope="row" colSpan={3}>{t('dossierPricing.panel.linesTotal')}</th>
                  <td className="num">{formatCurrency(order.pricingSnapshot?.linesTotal ?? order.agreedPrice ?? 0)}</td>
                  {canEditLineRows && <td />}
                </tr>
              </tfoot>
            </table>
          </div>
        </section>
      )}
      {showLines && !hasLines && <p className="placeholder-text">{t('dossierSheet.price.noLinesYet')}</p>}
      {!linesEditable && !locked && isOpen && <p className="dossier-price-note">{t('dossierSheet.price.linesPermission')}</p>}

      {showLines && hasCalcDetails && (
        <PriceFold title={t('dossierPricing.panel.calcDetails')} icon={Calculator}>
          {notAppliedLines.length > 0 && (
            <div className="to-price-not-applied">
              <h4>{t('dossierPricing.panel.notApplied')}</h4>
              <ul>
                {notAppliedLines.map((line, index) => (
                  <li key={line.id ?? line.lineKey ?? index}>{line.label}</li>
                ))}
              </ul>
            </div>
          )}
          {!order.pricingSnapshot && notAppliedLines.length === 0 ? null : (
            <p className="dossier-price-actions">
              {order.pricingSnapshot && (
                <Button variant="ghost" onClick={editor.openCalcDetails}>
                  {t('dossierPricing.panel.fullCalculation')}
                </Button>
              )}
              {canEditPricingStatus && !pricingLocked && !stale && (
                <Button variant="ghost" onClick={editor.handleRecalculateClick} disabled={pricingBusy}>
                  {t('dossierPricing.panel.recalculate')}
                </Button>
              )}
            </p>
          )}
        </PriceFold>
      )}

      {showLines && (coverage.length > 0 || hasCommercialCoverage) && (
        <PriceFold title={t('dossierPricing.goodsCoverage.title')} icon={Boxes} defaultOpen>
          {coverage.length > 0 && (
            <ul className="dossier-price-coverage">
              {coverage.map((c, index) => (
                <li key={c.unitTypeId ?? `${c.unitLabel}-${index}`}>
                  <Badge tone={PRICING_COVERAGE_TONE[c.status]}>{t(PRICING_COVERAGE_LABELS[c.status])}</Badge>{' '}
                  {formatQuantity(c.quantity)} {c.unitLabel}
                  {c.status === 'Full' && c.coveredBy === 'FixedPrice' && ` — ${t('dossierPricing.panel.coveredByAgreed')}`}
                  {c.status === 'Full' && c.coveredBy !== 'FixedPrice' && ` — ${c.baseRuleName ?? t('dossierPricing.panel.baseTariff')}: ${formatCurrency(c.baseAmount)}`}
                  {c.status !== 'Full' && c.reason ? ` — ${c.reason}` : ''}
                  {c.servicesAmount > 0 && ` · ${t('dossierPricing.panel.services', { amount: formatCurrency(c.servicesAmount) })}`}
                </li>
              ))}
            </ul>
          )}
          {hasCommercialCoverage && (
            <div className="order-cargo-coverage">
              <div className="order-cargo-coverage-wrap">
                <table className="order-cargo-coverage-table">
                  <thead>
                    <tr>
                      <th scope="col">{t('dossierPricing.goodsCoverage.colGoods')}</th>
                      <th scope="col">{t('dossierPricing.goodsCoverage.colCoverage')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {[...order.cargoItems].sort((a, b) => a.sequence - b.sequence).map((item) => (
                      <tr key={item.id}>
                        <td>{describeCargoItem(item, unitLabel)}</td>
                        <td>
                          <CargoCoverageBadge coverage={item.commercialCoverage} />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <p className="order-cargo-coverage-hint">{t('dossierPricing.goodsCoverage.includedHint')}</p>
            </div>
          )}
        </PriceFold>
      )}

      {agreedEditable && !undecided && (
        <FixedPriceSection
          title={t(pricedByLines ? 'dossierPricing.activity.useFlat' : 'dossierPricing.panel.fixedTitle')}
          description={pricedByLines ? t('dossierPricing.panel.fixedDescription') : undefined}
          toggle={pricedByLines ? { checked: flatForced, onChange: setFlatForced, disabled: busy } : undefined}
        >
          {showAgreed ? agreedForm : null}
        </FixedPriceSection>
      )}

      <button type="button" className="dossier-price-open-order" onClick={() => navigate(`/transport-orders/${order.id}`)}>
        <span>{t('dossierPricing.detail.openOrder')}</span>
        <ArrowUpRight size={18} aria-hidden="true" />
      </button>

      {confirmZero && (
        <ConfirmDialog
          title={t('dossierPricing.order.zeroConfirmTitle')}
          message={t('dossierPricing.order.zeroConfirmMessage', { number: order.orderNumber })}
          confirmLabel={t('dossierPricing.order.zeroConfirmAction')}
          busy={busy}
          onConfirm={() => void persistAgreed(0)}
          onCancel={() => {
            // Cancelling keeps the previous agreement on screen, never a half-typed € 0.
            setConfirmZero(false)
            setAgreedInput(currentAgreedAmount(order))
          }}
        />
      )}

      {/* Price-line picker: active pricing units; the goods units stay resolvable for the cargo links. */}
      <OrderPricingDialogs
        editor={editor}
        unitTypes={units.pricingPickerOptions([editor.addUnit, ...order.cargoItems.map((c) => c.quantityUnitCode)])}
      />
    </div>
  )
}
