import { useImperativeHandle, useRef, useState, type Ref } from 'react'
import { describeApiError } from '../../../api/problemDetails'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import type { TransportOrderDetail } from '../../transport-orders/types'
import { createOrderForActivity } from '../api/dossiersApi'
import { DossierOrderPricePanel, type DossierOrderPricePanelHandle } from '../pricing/DossierOrderPricePanel'
import type { DossierActivity, DossierDetail } from '../types'
import { DossierActivityPricePanel, type DossierActivityPricePanelHandle } from './DossierActivityPricePanel'

export interface DossierPricePanelHandle {
  /** Focuses the control that resolves the "price" readiness field. */
  focusField: (field: string | null) => boolean
}

interface DossierPricePanelProps {
  dossier: DossierDetail
  /**
   * The billable unit the editor works on (stap 13): a transport activity (its order is `order`)
   * or a standalone activity (own price record); null for a dossier without billable activities.
   */
  activity: DossierActivity | null
  /** The order of the target transport activity (the price carrier); null while loading / without order. */
  order: TransportOrderDetail | null
  loading: boolean
  /** The linked order exists but could not be loaded (e.g. 404): show that instead of "add an activity". */
  orderUnavailable?: boolean
  /** The target transport activity WITHOUT an order (offers "Transportopdracht aanmaken" — real execution, never a price trick). */
  activityWithoutOrder: DossierActivity | null
  /** dossiers.manage AND dossier open — governs create-order / add-activity actions. */
  canManage: boolean
  /** orders.edit|orders.manage AND dossier open — governs the agreed price of an order. */
  canEditPrice: boolean
  /** The dossier is open — a closed dossier makes the order's line editor read-only too. */
  isOpen: boolean
  /** dossiers.price AND dossier open — governs the price of a standalone activity. */
  canEditActivityPrice: boolean
  /** dossiers.price regardless of the dossier status (tells "no right" apart from "dossier closed"). */
  hasActivityPriceRight: boolean
  onOrderSaved: (order: TransportOrderDetail) => void
  onDossierUpdated: (dossier: DossierDetail) => void
  onConflict: (err: unknown) => boolean
  onAddActivity: () => void
  /** Unsaved sales lines of a standalone activity (the section locks the activity picker meanwhile). */
  onDirtyChange?: (dirty: boolean) => void
  /** Re-fetches the target order after a failed load. */
  onRetryLoad?: () => void
  ref?: Ref<DossierPricePanelHandle>
}

/**
 * UX-sprint 2026-09-09 — Verkoop & prijs as a work surface, exposing the EXISTING pricing
 * concepts instead of a new "price" field. Stap 13 (2026-09-11): the dossier reckons in BILLABLE
 * UNITS — one per activity of a billable type.
 *
 * Master sprint 2026-09-21 (D5): this panel is the body for the SELECTED unit (the amount block
 * and the activity picker live in the section above it):
 *  - an order-backed activity is priced through its order — agreed (one-off) price plus THE order
 *    price line editor (`DossierOrderPricePanel`);
 *  - a standalone activity through its own price record — flat price or sales lines
 *    (`DossierActivityPricePanel`);
 *  - a transport activity without order offers "Transportopdracht aanmaken".
 */
export function DossierPricePanel({
  dossier,
  activity,
  order,
  loading,
  orderUnavailable = false,
  activityWithoutOrder,
  canManage,
  canEditPrice,
  isOpen,
  canEditActivityPrice,
  hasActivityPriceRight,
  onOrderSaved,
  onDossierUpdated,
  onConflict,
  onAddActivity,
  onDirtyChange,
  onRetryLoad,
  ref,
}: DossierPricePanelProps) {
  const { t } = useLocale()
  const toast = useToast()
  const createOrderRef = useRef<HTMLButtonElement>(null)
  const addActivityRef = useRef<HTMLButtonElement>(null)
  const activityPanelRef = useRef<DossierActivityPricePanelHandle>(null)
  const orderPanelRef = useRef<DossierOrderPricePanelHandle>(null)
  const [busy, setBusy] = useState(false)
  // The body shows the standalone editor when the target is a standalone unit; the order UI otherwise.
  const standaloneTarget = activity && !activity.hasStops ? activity : null

  useImperativeHandle(ref, () => ({
    focusField: () => {
      if (standaloneTarget) return activityPanelRef.current?.focusField() ?? false
      if (orderPanelRef.current?.focusField()) return true
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

  return (
    <div className="dossier-price-panel">
      {standaloneTarget && (
        <DossierActivityPricePanel
          key={standaloneTarget.id}
          ref={activityPanelRef}
          dossier={dossier}
          activity={standaloneTarget}
          canEdit={canEditActivityPrice}
          hasPriceRight={hasActivityPriceRight}
          onDossierUpdated={onDossierUpdated}
          onConflict={onConflict}
          onDirtyChange={onDirtyChange}
        />
      )}

      {!standaloneTarget && loading && <p className="placeholder-text">{t('dossiers.route.loading')}</p>}

      {!standaloneTarget && !loading && !order && orderUnavailable && (
        <div className="dossier-price-noorder">
          <p className="placeholder-text">{t('dossiers.route.loadFailed')}</p>
          {onRetryLoad && (
            <Button variant="secondary" onClick={onRetryLoad}>
              {t('dossierSheet.route.retryLoad')}
            </Button>
          )}
        </div>
      )}

      {!standaloneTarget && !loading && !order && !orderUnavailable && (
        // A transport unit without order: creating the order IS the real execution step (never a
        // trick to hang a price on). Without any billable activity: offer to add one.
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
        </div>
      )}

      {!standaloneTarget && order && (
        <DossierOrderPricePanel
          ref={orderPanelRef}
          dossier={dossier}
          activity={activity}
          order={order}
          canEditPrice={canEditPrice}
          isOpen={isOpen}
          onOrderSaved={onOrderSaved}
        />
      )}
    </div>
  )
}
