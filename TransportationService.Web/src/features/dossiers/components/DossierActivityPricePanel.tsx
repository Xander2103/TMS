import { useImperativeHandle, useRef, useState, type FormEvent, type Ref } from 'react'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { parseDecimalInput } from '../../../utils/numbers'
import { euro } from '../../invoices/types'
import { ORDER_PRICING_STATUS_LABELS, type OrderPricingStatus } from '../../transport-orders/types'
import { getDossier, setActivityPrice } from '../api/dossiersApi'
import { ActivityPriceLinesEditor, type ActivityPriceLinesEditorHandle } from '../pricing/ActivityPriceLinesEditor'
import { saveActivityPriceLines } from '../pricing/activityPriceLinesApi'
import { activityUnitName } from '../pricing/activityPriceDisplay'
import { PriceStatusBadge } from '../pricing/PriceStatusBadge'
import type { DossierActivity, DossierDetail } from '../types'
import '../pricing/dossier-pricing.css'

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
  /** dossiers.price, regardless of the dossier status — tells "no right" apart from "dossier closed". */
  hasPriceRight?: boolean
  onDossierUpdated: (dossier: DossierDetail) => void
  onConflict: (err: unknown) => boolean
  /** Unsaved sales lines exist (the section locks the activity picker meanwhile). */
  onDirtyChange?: (dirty: boolean) => void
  ref?: Ref<DossierActivityPricePanelHandle>
}

const LOCKED_STATUSES = new Set(['Locked', 'Invoiced'])

/** '' → null (clear), a valid amount ≥ 0 → cents-rounded number, anything else → undefined (invalid). */
function parseAmount(raw: string): number | null | undefined {
  if (raw.trim() === '') return null
  const value = parseDecimalInput(raw)
  return value !== null && value >= 0 ? Math.round(value * 100) / 100 : undefined
}

/** Text shown in the input for the activity's current FLAT price agreement ('' = none / priced through lines). */
function currentAgreedAmount(activity: DossierActivity): string {
  if ((activity.priceLines?.length ?? 0) > 0 || activity.pricingSource === 'Lines') return ''
  return activity.isPriced && activity.agreedPrice != null ? String(activity.agreedPrice) : ''
}

/**
 * Stap 13 (2026-09-11): a standalone billable activity (Opslag, Kraan, …) carries its own price
 * record instead of borrowing a transport order: own concurrency token (`pricingVersion`),
 * Locked/Invoiced read-only, and every save returns the whole dossier so totals, readiness and
 * this card re-render from the authoritative state.
 *
 * Master sprint 2026-09-21 (D5): the record can be priced in two ways — the flat "vaste prijs"
 * (one agreed amount; empty = cleared, € 0 is a deliberate price) or sales LINES whose total the
 * server computes. Switching is allowed and explained in one line. "Gratis" is an explicit
 * confirmation (empty list + freeConfirmed), "Prijs verwijderen" an empty list without it; the
 * status shown is always the server's `priceStatus`, and a missing price never reads as € 0,00.
 */
export function DossierActivityPricePanel({
  dossier, activity, canEdit, hasPriceRight = canEdit, onDossierUpdated, onConflict, onDirtyChange, ref,
}: DossierActivityPricePanelProps) {
  const { t } = useLocale()
  const toast = useToast()
  const inputRef = useRef<HTMLInputElement>(null)
  const linesRef = useRef<ActivityPriceLinesEditorHandle>(null)
  const [input, setInput] = useState(() => currentAgreedAmount(activity))
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [linesDirty, setLinesDirty] = useState(false)
  const [flatForced, setFlatForced] = useState(false)
  const [confirming, setConfirming] = useState<'free' | 'remove' | null>(null)
  // Bumped when the price was replaced wholesale (flat price, free, removed): the line draft is void.
  const [linesReset, setLinesReset] = useState(0)

  // The input follows the activity it belongs to (switching units or a refetch after a save must
  // never leave a stale amount on screen) — adjusted during render, React's "previous render" pattern.
  const activityKey = `${activity.id}:${activity.pricingVersion ?? 'none'}:${activity.agreedPrice ?? ''}:${activity.isPriced}`
  const [syncedKey, setSyncedKey] = useState(activityKey)
  if (syncedKey !== activityKey) {
    setSyncedKey(activityKey)
    setInput(currentAgreedAmount(activity))
    setError(null)
    setFlatForced(false)
  }

  const locked = activity.pricingStatus != null && LOCKED_STATUSES.has(activity.pricingStatus)
  const editable = canEdit && !locked
  const statusLabel = activity.pricingStatus && activity.pricingStatus in ORDER_PRICING_STATUS_LABELS
    ? t(ORDER_PRICING_STATUS_LABELS[activity.pricingStatus as OrderPricingStatus])
    : (activity.pricingStatus ?? '')
  const isFree = activity.priceStatus === 'Free'
  // A confirmed-free activity is a settled € 0 — no "is this deliberate?" question any more.
  const isZero = activity.isPriced && activity.agreedPrice === 0 && !isFree
  const hasLines = (activity.priceLines?.length ?? 0) > 0
  const hasPrice = activity.isPriced || isFree || hasLines
  // The flat price is the quick option; with lines on the table it steps aside until asked for.
  const showFlat = editable && ((!hasLines && !linesDirty) || flatForced)

  useImperativeHandle(ref, () => ({
    focusField: () => {
      if (showFlat && inputRef.current) {
        inputRef.current.focus()
        return true
      }
      if (editable) {
        linesRef.current?.addLine()
        return true
      }
      return false
    },
  }))

  function dropLineDraft() {
    setLinesReset((token) => token + 1)
    setLinesDirty(false)
    onDirtyChange?.(false)
  }

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
      dropLineDraft()
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

  /** Empty list + freeConfirmed = explicitly free; empty list without = not priced (contract 4.4). */
  async function clearLines(freeConfirmed: boolean) {
    if (busy || !editable) return
    setBusy(true)
    setError(null)
    try {
      const updated = await saveActivityPriceLines(dossier.id, activity.id, { version: activity.pricingVersion, lines: [], freeConfirmed })
      toast.showSuccess(freeConfirmed ? t('dossierPricing.activity.freeSaved') : t('dossierPricing.activity.priceRemoved'))
      setConfirming(null)
      dropLineDraft()
      onDossierUpdated(updated)
    } catch (err) {
      setConfirming(null)
      if (err instanceof ApiError && err.status === 409) {
        onConflict(err)
        setError(t('dossiers.orderDrawer.conflict'))
      } else {
        setError(describeApiError(err, t('dossierPricing.activity.actionFailed')).message)
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="dossier-activity-price">
      <p className="dossier-price-order-label">
        {t('dossierSheet.price.forActivity', { name: activityUnitName(activity) })} <PriceStatusBadge status={activity.priceStatus} />
      </p>

      {locked && <p className="dossier-price-note">{t('dossierSheet.price.lockedActivity', { status: statusLabel })}</p>}
      {!locked && !canEdit && (
        <p className="dossier-price-note">
          {hasPriceRight ? t('dossierPricing.activity.readOnlyClosed') : t('dossierPricing.activity.readOnlyNoPermission')}
        </p>
      )}

      {isFree && <p className="dossier-price-note">{t('dossierPricing.activity.freeConfirmed')}</p>}
      {!isFree && activity.isPriced && activity.agreedPrice != null && (
        <p className="dossier-price-note">{t('dossierSheet.price.agreedCurrent', { amount: euro(activity.agreedPrice) })}</p>
      )}
      {!hasPrice && editable && <p className="dossier-price-none">{t('dossierPricing.noPriceSet')}</p>}
      {!hasPrice && !editable && <p className="placeholder-text">{t('dossierSheet.price.noActivityPrice')}</p>}

      {showFlat && (
        <form className="dossier-price-agreed" onSubmit={(event) => void save(event)}>
          <FormField
            label={t('dossierSheet.price.agreedLabel')}
            htmlFor="dossier-activity-agreed-price"
            error={error ?? undefined}
            hint={hasLines ? t('dossierPricing.activity.flatReplacesLines') : t('dossierSheet.price.agreedHintActivity')}
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
      {!showFlat && error && (
        <p className="dossier-activity-lines-error" role="alert">
          {error}
        </p>
      )}

      {isZero && (
        <p className="dossier-price-zero-warning" role="note">
          {t('dossierSheet.price.zeroWarningActivity')}
        </p>
      )}

      <ActivityPriceLinesEditor
        key={linesReset}
        ref={linesRef}
        dossier={dossier}
        activity={activity}
        editable={editable}
        replacesFlatPrice={activity.isPriced && !hasLines && !isFree}
        onDirtyChange={(dirty) => {
          setLinesDirty(dirty)
          onDirtyChange?.(dirty)
        }}
        onDossierUpdated={onDossierUpdated}
        reloadDossier={() => getDossier(dossier.id)}
      />

      {editable && (
        <p className="dossier-price-actions dossier-activity-price-actions">
          {hasLines && !flatForced && (
            <Button variant="ghost" onClick={() => setFlatForced(true)} disabled={busy}>
              {t('dossierPricing.activity.useFlat')}
            </Button>
          )}
          {!isFree && (
            <Button variant="ghost" onClick={() => (hasPrice || linesDirty ? setConfirming('free') : void clearLines(true))} disabled={busy}>
              {t('dossierPricing.activity.confirmFree')}
            </Button>
          )}
          {hasPrice && (
            <Button variant="ghost" onClick={() => setConfirming('remove')} disabled={busy}>
              {t('dossierPricing.activity.removePrice')}
            </Button>
          )}
        </p>
      )}

      {confirming === 'free' && (
        <ConfirmDialog
          title={t('dossierPricing.activity.confirmFreeTitle')}
          message={t('dossierPricing.activity.confirmFreeMessage')}
          confirmLabel={t('dossierPricing.activity.confirmFreeAction')}
          busy={busy}
          onConfirm={() => void clearLines(true)}
          onCancel={() => setConfirming(null)}
        />
      )}
      {confirming === 'remove' && (
        <ConfirmDialog
          title={t('dossierPricing.activity.removePrice')}
          message={t('dossierPricing.activity.removePriceMessage')}
          confirmLabel={t('dossierPricing.activity.removePrice')}
          destructive
          busy={busy}
          onConfirm={() => void clearLines(false)}
          onCancel={() => setConfirming(null)}
        />
      )}
    </div>
  )
}
