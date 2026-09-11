import type { Ref } from 'react'
import { RetainedHeight } from '../../../../components/ui/RetainedHeight'
import { useLocale } from '../../../../i18n/localeContext'
import { useDossierWorkspace } from '../../dossierWorkspace'
import { useRegisterDossierSection } from '../../sectionRegistry'
import { DossierOrderSwitcher } from '../DossierOrderSwitcher'
import { DossierPricePanel, type DossierPricePanelHandle } from '../DossierPricePanel'

/**
 * Verkoop & prijs workspace: the existing pricing panel (dossier total, completeness, agreed
 * price, sales lines, intentional-zero warning, standalone activity pricing) — unchanged —
 * with the billable-unit switcher above it.
 */
export function DossierPriceSection({ panelRef }: { panelRef: Ref<DossierPricePanelHandle> }) {
  const { t } = useLocale()
  const ws = useDossierWorkspace()
  const register = useRegisterDossierSection('prijs', { focusField: (field) => ws.focusPriceField(field) })
  const loading = !ws.priceTargetIsStandalone && ws.firstOrderLoading

  return (
    <section id="sectie-prijs" className="dossier-section dossier-workspace" aria-label={t('dossiers.detail.priceAria')} ref={register}>
      <h2 tabIndex={-1}>{t('dossiers.detail.priceTitle')}</h2>
      <DossierOrderSwitcher
        activities={ws.billableActivities}
        selectedActivityId={ws.priceActivity?.id ?? ''}
        onSelect={ws.selectActivity}
        locked={ws.routeDirty}
        label={t('dossierSheet.orderSwitch.unitLabel')}
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
          canEditLines={ws.canEditPriceLines && ws.isOpen}
          canEditActivityPrice={ws.canEditActivityPrice && ws.isOpen}
          onOrderSaved={ws.handleOrderSaved}
          onDossierUpdated={ws.applyDossier}
          onConflict={ws.handleConflict}
          onAddActivity={ws.openAddActivity}
          onRetryLoad={ws.retryOrderLoad}
        />
      </RetainedHeight>
    </section>
  )
}
