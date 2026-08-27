import { useNavigate } from 'react-router-dom'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import type { DossierDetail } from '../types'

interface DossierPriceSummaryProps {
  dossier: DossierDetail
}

/**
 * §11 Verkoop & prijs: het afgesproken totaal + één regel per opdracht; prijsdetails en
 * verkooplijnen leven één klik dieper op de opdrachtpagina (Wave 1 houdt dit lean).
 */
export function DossierPriceSummary({ dossier }: DossierPriceSummaryProps) {
  const { t } = useLocale()
  const navigate = useNavigate()
  const pricingIssues = dossier.readiness.filter((issue) => issue.code.startsWith('pricing.') && issue.severity !== 'Info')
  const firstOrderId = dossier.orders[0]?.orderId

  return (
    <>
      <p className="dossier-price-total">
        <strong>{euro(dossier.financials.agreedOrderTotal)}</strong>
        {pricingIssues.length > 0 && (
          <span className="dossier-price-warning">
            ⚠ {t('dossiers.price.attention', { count: pricingIssues.length })}
          </span>
        )}
      </p>
      {dossier.orders.length === 0 && <p className="placeholder-text">{t('dossiers.price.noLines')}</p>}
      {dossier.orders.length > 0 && (
        <ul className="dossier-price-lines">
          {dossier.orders.map((order) => (
            <li key={order.linkId}>
              <span>
                <code>{order.orderNumber}</code>
                {order.goodsDescription && ` ${order.goodsDescription}`}
              </span>
              <span>{order.agreedPrice != null ? euro(order.agreedPrice) : '—'}</span>
            </li>
          ))}
        </ul>
      )}
      {firstOrderId && (
        <p className="dossier-price-actions">
          <Button variant="secondary" onClick={() => navigate(`/transport-orders/${firstOrderId}`)}>
            {t('dossiers.price.details')}
          </Button>
          <Button variant="secondary" onClick={() => navigate(`/transport-orders/${firstOrderId}`)}>
            {t('dossiers.price.addLine')}
          </Button>
        </p>
      )}
    </>
  )
}
