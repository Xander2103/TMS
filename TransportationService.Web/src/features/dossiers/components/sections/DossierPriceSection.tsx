import { useEffect, useRef, useState, type Ref } from 'react'
import { useSearchParams } from 'react-router-dom'
import { RetainedHeight } from '../../../../components/ui/RetainedHeight'
import { useLocale } from '../../../../i18n/localeContext'
import { useDossierWorkspace } from '../../dossierWorkspace'
import { ActivityPricePicker } from '../../pricing/ActivityPricePicker'
import { DossierPriceSummary } from '../../pricing/DossierPriceSummary'
import { useRegisterDossierSection } from '../../sectionRegistry'
import { DossierPricePanel, type DossierPricePanelHandle } from '../DossierPricePanel'

/** Deep link: `/dossiers/:id/prijs?activiteit=<activityId>` lands on that activity's price. */
const ACTIVITY_PARAM = 'activiteit'

/**
 * Verkoop & prijs workspace (master sprint 2026-09-21, spec §15): the dossier amount block on
 * top, then the activity picker — ALWAYS shown, every commercial activity selectable — and the
 * price editor of the selected activity. The selection is the workspace's (shared with the
 * attention jumps and the route target); `?activiteit=` selects once on arrival and never writes
 * the URL back, so switching stays local state (no navigation, no scroll jump).
 */
export function DossierPriceSection({ panelRef }: { panelRef: Ref<DossierPricePanelHandle> }) {
  const { t } = useLocale()
  const ws = useDossierWorkspace()
  const register = useRegisterDossierSection('prijs', { focusField: (field) => ws.focusPriceField(field) })
  const loading = !ws.priceTargetIsStandalone && ws.firstOrderLoading
  const [searchParams] = useSearchParams()
  const requestedId = searchParams.get(ACTIVITY_PARAM)
  const consumedParam = useRef<string | null>(null)
  // The activity whose sales lines hold unsaved edits; a lock only while it is still the target.
  const [dirtyActivityId, setDirtyActivityId] = useState<string | null>(null)
  const priceActivityId = ws.priceActivity?.id ?? null
  const priceDirty = dirtyActivityId !== null && dirtyActivityId === priceActivityId

  const { billableActivities, selectActivity } = ws
  useEffect(() => {
    if (!requestedId || consumedParam.current === requestedId) return
    if (!billableActivities.some((activity) => activity.id === requestedId)) return
    consumedParam.current = requestedId
    selectActivity(requestedId)
    // The selection is applied once per param value; the planner's later picks are never overridden.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestedId, billableActivities.length])

  return (
    <section id="sectie-prijs" className="dossier-section dossier-workspace" aria-label={t('dossiers.detail.priceAria')} ref={register}>
      <h2 tabIndex={-1}>{t('dossiers.detail.priceTitle')}</h2>
      <DossierPriceSummary
        dossier={ws.dossier}
        billableActivities={ws.billableActivities}
        legacyOrders={ws.legacyOrders}
        onSelect={ws.selectActivity}
        locked={ws.routeDirty || priceDirty}
      />
      <ActivityPricePicker
        dossier={ws.dossier}
        activities={ws.billableActivities}
        selectedActivityId={ws.priceActivity?.id ?? ''}
        onSelect={ws.selectActivity}
        locked={ws.routeDirty || priceDirty}
        lockedHint={ws.routeDirty ? t('dossierSheet.orderSwitch.lockedWhileDirty') : t('dossierPricing.picker.lockedWhileDirty')}
      />
      <RetainedHeight retain={loading} className="dossier-section-body">
        <DossierPricePanel
          ref={panelRef}
          dossier={ws.dossier}
          activity={ws.priceActivity}
          order={ws.firstOrder}
          loading={loading}
          orderUnavailable={Boolean(ws.firstLinkedOrderId) && !ws.firstOrderLoading && !ws.firstOrder}
          activityWithoutOrder={ws.activityWithoutOrder}
          canManage={ws.canManage && ws.isOpen}
          canEditPrice={ws.canEditOrder && ws.isOpen}
          isOpen={ws.isOpen}
          canEditActivityPrice={ws.canEditActivityPrice && ws.isOpen}
          hasActivityPriceRight={ws.canEditActivityPrice}
          onOrderSaved={ws.handleOrderSaved}
          onDossierUpdated={ws.applyDossier}
          onConflict={ws.handleConflict}
          onAddActivity={ws.openAddActivity}
          onDirtyChange={(dirty) => setDirtyActivityId(dirty ? priceActivityId : null)}
          onRetryLoad={ws.retryOrderLoad}
        />
      </RetainedHeight>
    </section>
  )
}
