import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import { getOrderDeliveryContext, type OrderDeliveryContext } from '../api/incidentsApi'

/**
 * EX-08 + redelivery links on the order page: what actually reached the consignee when it was
 * not (fully) delivered, and the redelivery relation in both directions — forward to the new
 * order, back to the original order and the incident that caused it. Nothing when all is well.
 */
export function DeliveryContextNotice({ transportOrderId }: { transportOrderId: string }) {
  const { t } = useLocale()
  const [context, setContext] = useState<OrderDeliveryContext | null>(null)

  useEffect(() => {
    let active = true
    getOrderDeliveryContext(transportOrderId)
      .then((result) => { if (active) setContext(result) })
      .catch(() => undefined)
    return () => { active = false }
  }, [transportOrderId])

  if (!context) return null
  const shortfall = context.deliveryOutcome && context.deliveryOutcome !== 'Delivered'
  if (!shortfall && !context.redeliveryOfOrderId && !context.redeliveryOrderId) return null

  return (
    <div className="related-incidents-notice" role="status">
      {shortfall && <strong>{t(`incidents.delivery.outcome.${context.deliveryOutcome}`)}</strong>}
      {context.redeliveryOrderId && (
        <span>
          {shortfall ? ' · ' : ''}
          {t('incidents.delivery.redelivery')}{' '}
          <Link className="ui-id-link" to={`/transport-orders/${context.redeliveryOrderId}`}>{context.redeliveryOrderNumber}</Link>
        </span>
      )}
      {context.redeliveryOfOrderId && (
        <span>
          {t('incidents.delivery.redeliveryOf')}{' '}
          <Link className="ui-id-link" to={`/transport-orders/${context.redeliveryOfOrderId}`}>{context.redeliveryOfOrderNumber}</Link>
          {context.sourceIncidentId && (
            <>
              {' · '}
              <Link to={`/incidents/${context.sourceIncidentId}`}>{context.sourceIncidentTitle ?? t('incidents.delivery.incident')}</Link>
            </>
          )}
        </span>
      )}
    </div>
  )
}
