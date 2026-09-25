import type { Ref } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '../../../../components/ui/Button'
import { useLocale } from '../../../../i18n/localeContext'
import { euro } from '../../../invoices/types'
import { dossierTabPath } from '../../dossierSections'
import { useDossierWorkspace } from '../../dossierWorkspace'
import { useRegisterDossierSection } from '../../sectionRegistry'
import { ActivityList } from '../ActivityList'

/** Activiteiten workspace: the ordered activity cards, legacy linked orders and the add action. */
export function DossierActivitiesSection({ addButtonRef }: { addButtonRef: Ref<HTMLButtonElement> }) {
  const { t } = useLocale()
  const navigate = useNavigate()
  const ws = useDossierWorkspace()
  const register = useRegisterDossierSection('activiteiten', { focusField: () => ws.focusAddActivity() })

  return (
    <section id="sectie-activiteiten" className="dossier-section" aria-label={t('dossiers.detail.activitiesTitle')} ref={register}>
      <h2 tabIndex={-1}>{t('dossiers.detail.activitiesTitle')}</h2>
      <ActivityList
        activities={ws.activities}
        dossier={ws.dossier}
        canManage={ws.canManage && ws.isOpen}
        onOpen={ws.openActivity}
        onOpenDetail={ws.openActivityDetail}
        // Several documents → the Documenten tab, opened on this activity's order.
        onOpenDocuments={(activity) =>
          navigate(`${dossierTabPath(ws.dossier.id, 'documenten')}?opdracht=${activity.linkedTransportOrderId}`)
        }
        highlightedActivityId={ws.highlightedActivityId}
        onAdd={ws.openAddActivity}
        addButtonRef={addButtonRef}
      />
      {ws.legacyOrders.length > 0 && (
        <div className="dossier-legacy-orders">
          <h3>{t('dossiers.detail.linkedOrders')}</h3>
          <ul className="db-list">
            {ws.legacyOrders.map((order) => (
              <li key={order.linkId}>
                <span className="db-row">
                  <button type="button" className="link-button" onClick={() => navigate(`/transport-orders/${order.orderId}`)}>
                    <code>{order.orderNumber}</code>
                  </button>
                  <span className="db-row-main" title={order.goodsDescription ?? undefined}>
                    {order.orderDate} · {order.status}
                    {order.agreedPrice !== null && <> · {euro(order.agreedPrice)}</>}
                  </span>
                  {ws.canManage && ws.isOpen && (
                    <Button variant="secondary" onClick={() => ws.unlinkOrder(order.orderId)} disabled={ws.busy}>
                      {t('dossiers.detail.unlink')}
                    </Button>
                  )}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  )
}
