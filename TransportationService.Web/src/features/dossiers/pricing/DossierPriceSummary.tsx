import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import { isDossierPriced } from '../dossierDisplay'
import type { DossierActivity, DossierDetail, DossierOrder } from '../types'
import { activityUnitName, needsPricing } from './activityPriceDisplay'
import './dossier-pricing.css'

interface DossierPriceSummaryProps {
  dossier: DossierDetail
  billableActivities: DossierActivity[]
  /** Linked orders no activity represents (legacy dossiers) — listed, not selectable. */
  legacyOrders: DossierOrder[]
  onSelect: (activityId: string) => void
  /** Unsaved edits: the links are disabled like the picker. */
  locked: boolean
}

/** Legacy fallback for a payload without the per-order flag: a positive amount. */
function legacyOrderPriced(o: DossierOrder): boolean {
  return o.isPriced ?? (o.agreedPrice != null && o.agreedPrice > 0)
}

/**
 * Priced/total units. The server's counts when the payload carries them; an older payload only
 * gets the marker when it is PARTIAL (the amount must never read as "the dossier is priced").
 */
function unitCounts(dossier: DossierDetail, activities: DossierActivity[], legacyOrders: DossierOrder[]): { priced: number; total: number } | null {
  const f = dossier.financials
  if (f.pricedActivityCount !== undefined) {
    const total = f.billableActivityCount ?? activities.length + legacyOrders.length
    return total > 0 ? { priced: f.pricedActivityCount, total } : null
  }
  const total = activities.length > 0 ? activities.length + legacyOrders.length : dossier.orders.length
  const pricedCount =
    activities.length > 0
      ? activities.filter((a) => !needsPricing(dossier, a)).length + legacyOrders.filter(legacyOrderPriced).length
      : (f.pricedOrderCount ?? dossier.orders.filter(legacyOrderPriced).length)
  return pricedCount > 0 && total > 1 && pricedCount < total ? { priced: pricedCount, total } : null
}

/**
 * Dossier amount block of the price tab. The TOTAL is the server's financial summary (one column
 * per billable unit on the backend — nothing is summed here, so nothing can be counted twice),
 * "x van y activiteiten geprijsd" are the server's counts, and the activities that still need a
 * price are links that make them the price target. "Nog geen prijs" instead of € 0,00 while
 * nothing is priced.
 */
export function DossierPriceSummary({ dossier, billableActivities, legacyOrders, onSelect, locked }: DossierPriceSummaryProps) {
  const { t } = useLocale()
  const priced = isDossierPriced(dossier)
  const pricingIssues = dossier.readiness.filter((issue) => issue.code.startsWith('pricing.') && issue.severity !== 'Info')
  const counts = unitCounts(dossier, billableActivities, legacyOrders)
  const toPrice = billableActivities.filter((activity) => needsPricing(dossier, activity))

  return (
    <div className="dossier-price-summary">
      <p className="dossier-price-total">
        {priced ? (
          <strong>{euro(dossier.financials.agreedOrderTotal)}</strong>
        ) : (
          <strong className="dossier-price-none">{t('dossierSheet.price.noPrice')}</strong>
        )}
        {counts && <span className="dossier-price-partial">{t('dossierSheet.price.partialTotal', counts)}</span>}
        {pricingIssues.length > 0 && (
          <span className="dossier-price-warning">⚠ {t('dossiers.price.attention', { count: pricingIssues.length })}</span>
        )}
      </p>

      {toPrice.length > 0 && (
        <p className="dossier-price-toprice">
          <span>{t('dossierPricing.summary.toPrice')}</span>
          {toPrice.map((activity) => {
            const unit = activityUnitName(activity)
            const name = activity.linkedOrderNumber ? t('dossierPricing.picker.itemWithOrder', { number: activity.linkedOrderNumber, name: unit }) : unit
            return (
              <button key={activity.id} type="button" className="link-button" onClick={() => onSelect(activity.id)} disabled={locked}>
                {activity.priceStatus === 'PartiallyPriced' ? t('dossierPricing.summary.partial', { name }) : name}
              </button>
            )
          })}
        </p>
      )}

      {legacyOrders.length > 0 && (
        <div className="dossier-price-legacy">
          <p className="dossier-price-note">{t('dossierPricing.summary.legacyOrders')}</p>
          <ul className="dossier-price-lines">
            {legacyOrders.map((o) => (
              <li key={o.linkId}>
                <span>
                  <code>{o.orderNumber}</code>
                  {o.goodsDescription && ` · ${o.goodsDescription}`}
                </span>
                <span>{legacyOrderPriced(o) && o.agreedPrice != null ? euro(o.agreedPrice) : t('dossierPricing.notPricedYet')}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
