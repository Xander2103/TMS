import { CheckCircle2 } from 'lucide-react'
import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import { issueActivity } from '../activityDisplay'
import { isDossierPriced } from '../dossierDisplay'
import type { DossierActivity, DossierDetail, DossierOrder } from '../types'
import { needsAttention } from './activityPriceDisplay'
import { priceAttention } from './priceUnitDisplay'
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

/** Above this many units the progress reads as one bar instead of a segment per unit. */
const MAX_SEGMENTS = 12

/**
 * DOSSIER summary of the price tab: a compact card under the list of parts, so the dossier's own
 * numbers never mix with those of the selected unit on the right.
 *  - TOTAAL: the server's financial summary (nothing is summed here); "Nog geen prijs" while
 *    nothing is priced, and the label says "Voorlopig totaal" as long as a unit is still open, so
 *    a partial aggregate is never read as the dossier price.
 *  - VOORTGANG: "x van y onderdelen afgehandeld" with a segment per unit — the € 0 units that
 *    still await a decision are not settled.
 *  - A pricing warning that no open unit represents (e.g. an outdated price) is named here; the
 *    open units themselves are visible in the list above.
 */
export function DossierPriceSummary({ dossier, billableActivities, legacyOrders, onSelect, locked }: DossierPriceSummaryProps) {
  const { t } = useLocale()
  const priced = isDossierPriced(dossier)
  const counts = settledCounts(dossier, billableActivities, legacyOrders)
  const { otherIssues, count: openCount } = priceAttention(dossier, billableActivities)
  const complete = counts !== null && counts.handled >= counts.total && openCount === 0

  return (
    <section className="dossier-price-summary" aria-label={t('dossierPricing.summary.title')}>
      <div className="dossier-price-total">
        <span className="dossier-price-summary-label">
          {t(counts && counts.handled < counts.total ? 'dossierPricing.summary.totalProvisional' : 'dossierPricing.summary.total')}
        </span>
        {priced ? (
          <strong className="dossier-price-total-amount">{euro(dossier.financials.agreedOrderTotal)}</strong>
        ) : (
          <strong className="dossier-price-total-amount dossier-price-none">{t('dossierSheet.price.noPrice')}</strong>
        )}
      </div>

      {counts && (
        <div className="dossier-price-progress">
          <span className="dossier-price-progress-text">{t('dossierPricing.summary.handled', counts)}</span>
          {counts.total <= MAX_SEGMENTS ? (
            <span className={`dossier-price-progress-bar${complete ? ' is-complete' : ''}`} aria-hidden="true">
              {Array.from({ length: counts.total }, (_, index) => (
                <span key={index} className={index < counts.handled ? 'is-done' : undefined} />
              ))}
            </span>
          ) : (
            <span className={`dossier-price-progress-bar is-continuous${complete ? ' is-complete' : ''}`} aria-hidden="true">
              <span className="is-done" style={{ width: `${Math.round((counts.handled / counts.total) * 100)}%` }} />
            </span>
          )}
        </div>
      )}

      {openCount === 0 && (
        <p className="dossier-price-settled">
          <CheckCircle2 size={16} aria-hidden />
          {t('dossierPricing.summary.allSettled')}
        </p>
      )}

      {otherIssues.length > 0 && (
        <ul className="dossier-price-issues">
          {otherIssues.map((issue) => {
            const target = issueActivity(issue, billableActivities)
            return (
              <li key={`${issue.code}-${issue.activityId ?? issue.transportOrderId ?? issue.message}`}>
                {target ? (
                  <button type="button" className="link-button" onClick={() => onSelect(target.id)} disabled={locked}>
                    {issue.message}
                  </button>
                ) : (
                  issue.message
                )}
              </li>
            )
          })}
        </ul>
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
