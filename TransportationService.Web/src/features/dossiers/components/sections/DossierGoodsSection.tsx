import { RetainedHeight } from '../../../../components/ui/RetainedHeight'
import { useLocale } from '../../../../i18n/localeContext'
import { useDossierWorkspace } from '../../dossierWorkspace'
import { useRegisterDossierSection } from '../../sectionRegistry'
import { DossierGoodsSummary } from '../DossierGoodsSummary'
import { DossierOrderSwitcher } from '../DossierOrderSwitcher'

/** Goederen workspace: the target order's goods lines with the existing GoodsDrawer edit flow. */
export function DossierGoodsSection() {
  const { t } = useLocale()
  const ws = useDossierWorkspace()
  const register = useRegisterDossierSection('goederen')
  const hasGoods = ws.activities.some((a) => a.supportsGoods)

  return (
    <section id="sectie-goederen" className="dossier-section dossier-workspace" aria-label={t('dossiers.detail.goodsTitle')} ref={register}>
      <h2 tabIndex={-1}>{t('dossiers.detail.goodsTitle')}</h2>
      {!hasGoods && <p className="placeholder-text">{t('dossiers.overview.goodsNoTransport')}</p>}
      {hasGoods && (
        <>
          <DossierOrderSwitcher
            activities={ws.transportActivities}
            selectedActivityId={ws.routeActivity?.id ?? ''}
            onSelect={ws.selectActivity}
            locked={ws.routeDirty}
          />
          <RetainedHeight retain={ws.firstOrderLoading} className="dossier-section-body">
            {ws.firstLinkedOrderId ? (
              <DossierGoodsSummary
                order={ws.firstOrder}
                loading={ws.firstOrderLoading}
                canEdit={ws.canManage && ws.isOpen}
                onEdit={ws.openGoodsDrawer}
                vehiclePayloadKg={ws.routeVehicleCapacity.payloadKg}
                tailLiftCapacityKg={ws.routeVehicleCapacity.tailLiftCapacityKg}
              />
            ) : (
              <p className="placeholder-text">{t('dossiers.detail.goodsOnOrder')}</p>
            )}
          </RetainedHeight>
        </>
      )}
    </section>
  )
}
