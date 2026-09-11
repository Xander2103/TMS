import { useImperativeHandle, useRef, useState, type FormEvent, type Ref } from 'react'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import { ORDER_PRICING_STATUS_LABELS, type OrderPricingStatus } from '../../transport-orders/types'
import { setActivityPrice } from '../api/dossiersApi'
import type { DossierActivity, DossierDetail } from '../types'

export interface DossierActivityPricePanelHandle {
  /** Focuses the agreed-price input (the control that resolves the "price" readiness field). */
  focusField: () => boolean
}

interface DossierActivityPricePanelProps {
  dossier: DossierDetail
  /** The standalone billable activity (hasStops = false) that is the price target. */
  activity: DossierActivity
  /** dossiers.price AND dossier open. */
  canEdit: boolean
  onDossierUpdated: (dossier: DossierDetail) => void
  onConflict: (err: unknown) => boolean
  ref?: Ref<DossierActivityPricePanelHandle>
}

const LOCKED_STATUSES = new Set(['Locked', 'Invoiced'])

function parseAmount(raw: string): number | null | undefined {
  const text = raw.trim().replace(',', '.')
  if (text === '') return null
  const value = Number(text)
  return Number.isFinite(value) && value >= 0 ? Math.round(value * 100) / 100 : undefined
}

/** Text shown in the input for the activity's current price agreement ('' = none). */
function currentAgreedAmount(activity: DossierActivity): string {
  return activity.isPriced && activity.agreedPrice != null ? String(activity.agreedPrice) : ''
}

function unitName(activity: DossierActivity): string {
  return activity.label ? `${activity.activityTypeName} · ${activity.label}` : activity.activityTypeName
}

/**
 * Stap 13 (2026-09-11): a standalone billable activity (Opslag, Kraan, …) carries its own price
 * record instead of borrowing a transport order. The panel deliberately mirrors the order's
 * "eenmalige prijsafspraak": one agreed amount, empty = cleared, € 0 is a deliberate price
 * (provenance, not magnitude), own concurrency token (`pricingVersion`) with the dossier 409
 * banner on a stale save, Locked/Invoiced read-only. Saving returns the whole dossier so totals,
 * readiness and this card re-render from the authoritative state.
 */
export function DossierActivityPricePanel({ dossier, activity, canEdit, onDossierUpdated, onConflict, ref }: DossierActivityPricePanelProps) {
  const { t } = useLocale()
  const toast = useToast()
  const inputRef = useRef<HTMLInputElement>(null)
  const [input, setInput] = useState(() => currentAgreedAmount(activity))
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // The input follows the activity it belongs to (switching units or a refetch after a save must
  // never leave a stale amount on screen) — adjusted during render, React's "previous render" pattern.
  const activityKey = `${activity.id}:${activity.pricingVersion ?? 'none'}:${activity.agreedPrice ?? ''}:${activity.isPriced}`
  const [syncedKey, setSyncedKey] = useState(activityKey)
  if (syncedKey !== activityKey) {
    setSyncedKey(activityKey)
    setInput(currentAgreedAmount(activity))
    setError(null)
  }

  const locked = activity.pricingStatus != null && LOCKED_STATUSES.has(activity.pricingStatus)
  const editable = canEdit && !locked
  const statusLabel = activity.pricingStatus && activity.pricingStatus in ORDER_PRICING_STATUS_LABELS
    ? t(ORDER_PRICING_STATUS_LABELS[activity.pricingStatus as OrderPricingStatus])
    : (activity.pricingStatus ?? '')
  const isZero = activity.isPriced && activity.agreedPrice === 0

  useImperativeHandle(ref, () => ({
    focusField: () => {
      if (editable && inputRef.current) {
        inputRef.current.focus()
        return true
      }
      return false
    },
  }))

  async function save(event: FormEvent) {
    event.preventDefault()
    if (busy || !editable) return
    const amount = parseAmount(input)
    if (amount === undefined) {
      setError(t('dossierSheet.price.agreedInvalid'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      const updated = await setActivityPrice(dossier.id, activity.id, { fixedAmount: amount, version: activity.pricingVersion })
      toast.showSuccess(amount === null ? t('dossierSheet.price.agreedClearedActivity') : t('dossierSheet.price.agreedSaved'))
      onDossierUpdated(updated)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        // The page banner offers "Herladen"; the inline message says why the save did not land.
        onConflict(err)
        setError(t('dossiers.orderDrawer.conflict'))
      } else {
        setError(describeApiError(err, t('dossierSheet.price.agreedSaveFailed')).message)
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="dossier-activity-price">
      <p className="dossier-price-order-label">{t('dossierSheet.price.forActivity', { name: unitName(activity) })}</p>

      {locked && <p className="dossier-price-note">{t('dossierSheet.price.lockedActivity', { status: statusLabel })}</p>}

      {activity.isPriced && activity.agreedPrice != null && (
        <p className="dossier-price-note">{t('dossierSheet.price.agreedCurrent', { amount: euro(activity.agreedPrice) })}</p>
      )}
      {!activity.isPriced && !editable && <p className="placeholder-text">{t('dossierSheet.price.noActivityPrice')}</p>}

      {editable && (
        <form className="dossier-price-agreed" onSubmit={(event) => void save(event)}>
          <FormField
            label={t('dossierSheet.price.agreedLabel')}
            htmlFor="dossier-activity-agreed-price"
            error={error ?? undefined}
            hint={t('dossierSheet.price.agreedHintActivity')}
          >
            <div className="dossier-price-agreed-row">
              <input
                ref={inputRef}
                id="dossier-activity-agreed-price"
                type="text"
                inputMode="decimal"
                value={input}
                onChange={(event) => setInput(event.target.value)}
                disabled={busy}
                aria-invalid={error ? true : undefined}
                placeholder="0,00"
              />
              <Button type="submit" disabled={busy || input.trim() === currentAgreedAmount(activity)}>
                {t('dossierSheet.price.agreedSave')}
              </Button>
            </div>
          </FormField>
        </form>
      )}

      {isZero && (
        <p className="dossier-price-zero-warning" role="note">
          {t('dossierSheet.price.zeroWarningActivity')}
        </p>
      )}
    </div>
  )
}
