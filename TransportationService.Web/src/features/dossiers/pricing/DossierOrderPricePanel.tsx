import { useImperativeHandle, useRef, useState, type FormEvent, type Ref } from 'react'
import { useNavigate } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { parseDecimalInput } from '../../../utils/numbers'
import { euro } from '../../invoices/types'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { setOrderOneOffPrice } from '../../transport-orders/api/transportOrdersApi'
import { unitLabelFrom } from '../../transport-orders/pricing/cargoLabels'
import { OrderPricingDialogs } from '../../transport-orders/pricing/OrderPricingDialogs'
import { OrderPricingPanel } from '../../transport-orders/pricing/OrderPricingPanel'
import { useOrderPricingEditor } from '../../transport-orders/pricing/useOrderPricingEditor'
import { ORDER_PRICING_STATUS_LABELS, type TransportOrderDetail } from '../../transport-orders/types'
import type { DossierActivity, DossierDetail } from '../types'
import { PriceStatusBadge } from './PriceStatusBadge'
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
  ref?: Ref<DossierOrderPricePanelHandle>
}

const LOCKED_STATUSES = new Set(['Locked', 'Invoiced'])

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
 * The price of an ORDER-BACKED activity (transport) on the dossier price tab. The dossier never
 * invents a second source of truth: the agreed price is the order's one-off price agreement
 * (price-only command, version-gated — never the full order PUT, which would re-validate an
 * unrelated, still incomplete route), and the sales lines are edited through THE order price line
 * editor (`useOrderPricingEditor` + `OrderPricingPanel` + `OrderPricingDialogs`), exactly as on
 * the order page: free lines, line adjust/remove with reason, proposals, recalculation, coverage
 * and stale warnings, confirm/reopen, and the goods ↔ sales-line link (D4).
 */
export function DossierOrderPricePanel({ dossier, activity, order, canEditPrice, isOpen, onOrderSaved, ref }: DossierOrderPricePanelProps) {
  const { t } = useLocale()
  const toast = useToast()
  const navigate = useNavigate()
  const agreedInputRef = useRef<HTMLInputElement>(null)
  const { options: unitTypes } = useLookupOptions('/api/unit-types')
  const editor = useOrderPricingEditor(order, { onOrderChanged: onOrderSaved, readOnly: !isOpen })

  const [agreedInput, setAgreedInput] = useState(() => currentAgreedAmount(order))
  const [agreedError, setAgreedError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // The agreed-price field follows the order it belongs to (adjusted during render, see React's
  // "storing information from previous renders" pattern).
  const orderKey = `${order.id}:${order.version}`
  const [syncedOrderKey, setSyncedOrderKey] = useState(orderKey)
  if (syncedOrderKey !== orderKey) {
    setSyncedOrderKey(orderKey)
    setAgreedInput(currentAgreedAmount(order))
    setAgreedError(null)
  }

  const pricingStatus = order.pricingSnapshot?.status ?? 'Draft'
  const locked = LOCKED_STATUSES.has(pricingStatus)
  const overridden = Boolean(order.priceIsManual)
  const agreedEditable = canEditPrice && !locked && !overridden
  const linesEditable = editor.canEditPricingLines && !locked
  const calculatedTotal = calculatedTariffTotal(order)
  // Intentional € 0 on the target order: priced (provenance from the dossier payload) AND amount 0.
  // Not a blocker — a deliberate free delivery exists — but it must never pass unnoticed.
  const orderIsPriced = isOrderPriced(dossier, order)
  const orderIsZero = orderIsPriced && order.agreedPrice === 0
  const hasLines = (order.pricingLines ?? []).length > 0

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

  return (
    <div className="dossier-order-price">
      <p className="dossier-price-order-label">
        {t('dossierSheet.price.forOrder', { number: order.orderNumber })} <PriceStatusBadge status={activity?.priceStatus} />
      </p>

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
      {!agreedEditable && orderIsPriced && order.agreedPrice != null && !overridden && (
        <p className="dossier-price-note">{t('dossierSheet.price.agreedCurrent', { amount: euro(order.agreedPrice) })}</p>
      )}
      {orderIsZero && (
        <p className="dossier-price-zero-warning" role="note">
          {t('dossierSheet.price.zeroWarningOrder')}
        </p>
      )}

      <OrderPricingPanel
        ws={{ ...editor, order }}
        unitLabel={unitLabelFrom(unitTypes)}
        isPriced={orderIsPriced}
        addLineLabel={t('dossierPricing.addLine')}
      />
      {!hasLines && <p className="placeholder-text">{t('dossierSheet.price.noLinesYet')}</p>}
      {!linesEditable && !locked && isOpen && <p className="dossier-price-note">{t('dossierSheet.price.linesPermission')}</p>}

      <p className="dossier-price-actions">
        <Button variant="ghost" onClick={() => navigate(`/transport-orders/${order.id}`)}>
          {t('dossiers.price.details')}
        </Button>
      </p>

      <OrderPricingDialogs editor={editor} unitTypes={unitTypes} />
    </div>
  )
}
