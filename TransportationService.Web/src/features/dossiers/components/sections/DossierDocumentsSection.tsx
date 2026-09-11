import { useLocale } from '../../../../i18n/localeContext'
import { OrderDocumentsPanel } from '../../../transport-orders/components/OrderDocumentsPanel'
import { useDossierWorkspace } from '../../dossierWorkspace'
import { useRegisterDossierSection } from '../../sectionRegistry'
import { DossierOrderSwitcher } from '../DossierOrderSwitcher'

/**
 * Documenten workspace: documents live on the transport order, so this hosts the existing
 * self-saving order documents panel for the selected transport unit (no second document model).
 */
export function DossierDocumentsSection() {
  const { t } = useLocale()
  const ws = useDossierWorkspace()
  const register = useRegisterDossierSection('documenten')

  return (
    <section id="sectie-documenten" className="dossier-section dossier-workspace" aria-label={t('dossiers.detail.documentsTitle')} ref={register}>
      <h2 tabIndex={-1}>{t('dossiers.detail.documentsTitle')}</h2>
      {ws.transportActivities.length > 1 && (
        <DossierOrderSwitcher
          activities={ws.transportActivities}
          selectedActivityId={ws.routeActivity?.id ?? ''}
          onSelect={ws.selectActivity}
          locked={ws.routeDirty}
        />
      )}
      {ws.firstLinkedOrderId && ws.routeActivity?.linkedOrderNumber ? (
        <>
          <p className="dossier-ov-muted">{t('dossiers.overview.documentsForOrder', { number: ws.routeActivity.linkedOrderNumber })}</p>
          <OrderDocumentsPanel key={ws.firstLinkedOrderId} orderId={ws.firstLinkedOrderId} />
        </>
      ) : (
        <p className="placeholder-text">{t('dossiers.overview.documentsNoOrder')}</p>
      )}
    </section>
  )
}
