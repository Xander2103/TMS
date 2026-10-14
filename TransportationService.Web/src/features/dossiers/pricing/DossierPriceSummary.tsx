import { useId } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import { isDossierPriced } from '../dossierDisplay'
import type { DossierActivity, DossierDetail, DossierOrder } from '../types'
import { activityUnitName, needsAttention } from './activityPriceDisplay'
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
 * "x van y onderdelen afgehandeld". Settled = priced (or confirmed free) AND not waiting for a
 * decision: a € 0 that still needs its "free" confirmation (`ZeroUnconfirmed`) is NOT settled,
 * even though the server's `pricedActivityCount` counts it as priced. The total is the server's
 * billable-unit count; the settled count follows the unit statuses on screen.
 */
function settledCounts(dossier: DossierDetail, activities: DossierActivity[], legacyOrders: DossierOrder[]): { handled: number; total: number } | null {
  const f = dossier.financials
  const total = f.billableActivityCount ?? (activities.length > 0 ? activities.length + legacyOrders.length : dossier.orders.length)
  if (total === 0) return null
  // B37: the server's settled count (Priced or confirmed Free) is the shared truth with the
  // confirmation dialog; the client-side derivation below is the fallback for older payloads.
  if (typeof f.settledActivityCount === 'number') return { handled: Math.min(f.settledActivityCount, total), total }
  if (activities.length === 0) {
    return { handled: f.pricedActivityCount ?? f.pricedOrderCount ?? dossier.orders.filter(legacyOrderPriced).length, total }
  }
  const handled = activities.filter((a) => !needsAttention(dossier, a)).length + legacyOrders.filter(legacyOrderPriced).length
  return { handled: Math.min(handled, total), total }
}

/**
 * DOSSIER summary block of the price tab — labelled as such, so it is never mistaken for the
 * price of the selected unit. The TOTAL is the server's financial summary (one column per
 * billable unit on the backend — nothing is summed here), "x van y onderdelen afgehandeld"
 * excludes the € 0 units that still await a decision, and the units that still need a price or a
 * check are links that make them the selected unit. "Nog geen prijs" instead of € 0,00 while
 * nothing is priced.
 */
export function DossierPriceSummary({ dossier, billableActivities, legacyOrders, onSelect, locked }: DossierPriceSummaryProps) {
  const { t } = useLocale()
  const titleId = useId()
  const priced = isDossierPriced(dossier)
  const pricingIssues = dossier.readiness.filter((issue) => issue.code.startsWith('pricing.') && issue.severity !== 'Info')
  const counts = settledCounts(dossier, billableActivities, legacyOrders)
  const toHandle = billableActivities.filter((activity) => needsAttention(dossier, activity))

  return (
    <section className="dossier-price-summary" aria-labelledby={titleId}>
      <p id={titleId} className="dossier-price-summary-title">
        {t('dossierPricing.summary.title')}
      </p>
      <p className="dossier-price-total">
        {priced ? (
          <strong>{euro(dossier.financials.agreedOrderTotal)}</strong>
        ) : (
          <strong className="dossier-price-none">{t('dossierSheet.price.noPrice')}</strong>
        )}
        {counts && <span className="dossier-price-partial">{t('dossierPricing.summary.handled', counts)}</span>}
        {pricingIssues.length > 0 && (
          <span className="dossier-price-warning">⚠ {t('dossiers.price.attention', { count: pricingIssues.length })}</span>
        )}
      </p>

      {toHandle.length > 0 && (
        <p className="dossier-price-toprice">
          <span>{t('dossierPricing.summary.toHandle')}</span>
          {toHandle.map((activity) => {
            const unit = activityUnitName(activity)
            const name = activity.linkedOrderNumber ? t('dossierPricing.picker.itemWithOrder', { number: activity.linkedOrderNumber, name: unit }) : unit
            const label =
              activity.priceStatus === 'PartiallyPriced'
                ? t('dossierPricing.summary.partial', { name })
                : activity.priceStatus === 'ZeroUnconfirmed'
                  ? t('dossierPricing.summary.needsReview', { name })
                  : name
            return (
              <button key={activity.id} type="button" className="link-button" onClick={() => onSelect(activity.id)} disabled={locked}>
                {label}
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
    </section>
  )
}
