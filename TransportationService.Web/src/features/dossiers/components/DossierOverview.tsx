import { createElement, type ComponentType, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ArrowRight, Calendar, ChevronRight, FileText, History, ListChecks, MapPin, Package, Tag } from 'lucide-react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import { formatDateTime } from '../../../utils/dates'
import { euro } from '../../invoices/types'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { ORDER_DOCUMENT_TYPE_LABELS, type OrderDocumentType } from '../../transport-orders/api/orderDocumentsApi'
import { isOnSiteLiftingOrder } from '../../transport-orders/components/onSiteLifting'
import { unitLabelFrom } from '../../transport-orders/pricing/cargoLabels'
import { ORDER_STATUS_LABELS, ORDER_STATUS_TONE, type TransportOrderStatus } from '../../transport-orders/types'
import { PRICE_STATUS_LABEL_KEYS, PRICE_STATUS_TONE, activityPriceStatus, activityPriceText, activityReference } from '../activityDisplay'
import { activityTypeIcon } from '../activityTypeIcons'
import { needsPricing } from '../pricing/activityPriceDisplay'
import { dossierTabPath, type DossierTab } from '../dossierSections'
import { isDossierPriced } from '../dossierDisplay'
import { useDossierWorkspace } from '../dossierWorkspace'
import type { DossierActivity } from '../types'
import './dossier-overview-blocks.css'
import { stopLine, stopTiming } from '../routeStopText'

interface OverviewCardProps {
  tab: DossierTab
  icon: ComponentType<{ size?: number; 'aria-hidden'?: boolean }>
  title: string
  /** Compact state chip in the header (count, "Niet ingevuld", …). */
  status?: { label: string; tone: BadgeTone }
  action: string
  children: ReactNode
}

/**
 * One conceptual group of the read-only Overzicht. The header chevron and the footer action are
 * real links to the subsection (keyboard + middle-click + screen readers); the body never
 * contains an input — editing lives in the subsection.
 */
function OverviewCard({ tab, icon, title, status, action, children }: OverviewCardProps) {
  const { dossier } = useDossierWorkspace()
  const path = dossierTabPath(dossier.id, tab)
  return (
    <article className="dossier-ov-card" aria-labelledby={`ov-${tab}-title`}>
      <header className="dossier-ov-head">
        {createElement(icon, { size: 20, 'aria-hidden': true })}
        <h2 id={`ov-${tab}-title`}>{title}</h2>
        {status && <Badge tone={status.tone}>{status.label}</Badge>}
        <Link to={path} className="dossier-ov-chevron" aria-label={action}>
          <ChevronRight size={18} aria-hidden />
        </Link>
      </header>
      <div className="dossier-ov-body">{children}</div>
      <footer className="dossier-ov-foot">
        <Link to={path} className="dossier-ov-action">
          {action}
          <ArrowRight size={16} aria-hidden />
        </Link>
      </footer>
    </article>
  )
}

const MAX_ACTIVITY_PREVIEW = 3

/**
 * Redesign 2026-09-11: "What is this dossier, what is in it, what is missing, where do I go
 * next?" — a two-column, read-only summary built from the dossier payload and the already
 * loaded target order. Every card ends in one navigation action; no editing control lives here.
 */
export function DossierOverview() {
  const { t } = useLocale()
  const ws = useDossierWorkspace()
  const { dossier, activities, transportActivities, routeActivity, firstOrder, firstOrderLoading, firstLinkedOrderId } = ws
  const { options: unitTypes } = useLookupOptions('/api/unit-types')
  const unitLabel = unitLabelFrom(unitTypes)
  const hasRoute = activities.some((a) => a.hasStops)
  const hasGoods = activities.some((a) => a.supportsGoods)

  // --- Route -------------------------------------------------------------------------------
  const stops = firstOrder?.stops ?? []
  const loadingStop = stops.find((s) => s.stopType === 'Loading') ?? null
  const unloadingStop = stops.find((s) => s.stopType === 'Unloading') ?? null
  const siteStop = stops.find((s) => s.stopType === 'Site') ?? null
  const plannedStop = stops.find((s) => s.plannedFrom || s.plannedTo) ?? null
  // D2: an on-site lifting job has ONE applicable stop — its work site. No fictitious Laden/Lossen rows.
  const onSite = isOnSiteLiftingOrder(firstOrder) || (siteStop !== null && !loadingStop && !unloadingStop)
  // The readiness projection is the authority: while the DTO still flags a route (an activity
  // without its order route, a missing stop or work site) the card never says "Ingevuld" — whatever
  // the one loaded order happens to contain. A missing planning DATE is the Planning line's job.
  const routeFlagged =
    dossier.readiness.some((issue) => issue.section === 'route' && issue.severity !== 'Info' && !issue.code.endsWith('date_missing')) ||
    // `route.order_missing` is emitted PER activity; an older payload without it still has the fact.
    transportActivities.some((activity) => !activity.linkedTransportOrderId)
  const routeComplete = (onSite ? Boolean(siteStop) : Boolean(loadingStop && unloadingStop)) && !routeFlagged
  const routeStatus = firstOrderLoading
    ? undefined
    : { label: routeComplete ? t('dossiers.overview.routeFilled') : t('dossiers.overview.routeIncomplete'), tone: (routeComplete ? 'success' : 'warning') as BadgeTone }

  // --- Verkoop & prijs ---------------------------------------------------------------------
  const fin = dossier.financials
  const billable = fin.billableActivityCount ?? dossier.orders.length
  const priced = fin.pricedActivityCount ?? fin.pricedOrderCount ?? 0
  const zero = fin.zeroPricedActivityCount ?? 0
  const isPriced = isDossierPriced(dossier)
  const priceStatus = !isPriced
    ? { label: t('dossiers.overview.priceNone'), tone: 'danger' as BadgeTone }
    : priced < billable
      ? { label: t('dossiers.overview.pricePartial'), tone: 'warning' as BadgeTone }
      : { label: t('dossiers.overview.priceComplete'), tone: 'success' as BadgeTone }

  // --- Goederen ----------------------------------------------------------------------------
  // The unit NAME from the catalogue (code while it loads, legacy free text without a code) —
  // the same resolver as the goods tab and the order page, never the raw code.
  const cargo = firstOrder?.cargoItems ?? []
  const cargoByUnit = new Map<string, number>()
  for (const line of cargo) {
    const unit = unitLabel(line.quantityUnitCode, line.quantityUnit) || t('dossiers.goods.fallbackUnit')
    cargoByUnit.set(unit, (cargoByUnit.get(unit) ?? 0) + line.expectedQuantity)
  }
  const totalWeight = cargo.reduce((sum, line) => sum + (line.totalWeightKg ?? 0), 0)
  // D2: an on-site lifting job lifts a load — no goods lines is its normal state, not an omission.
  const liftingWithoutGoods = onSite && cargo.length === 0

  // --- Documenten & historiek --------------------------------------------------------------
  const documentCount = dossier.documentCount ?? 0
  const documentTypes = (dossier.documentTypes ?? []).map((type) =>
    ORDER_DOCUMENT_TYPE_LABELS[type as OrderDocumentType] ? t(ORDER_DOCUMENT_TYPE_LABELS[type as OrderDocumentType]) : type,
  )
  // D7: dossier-level notes are counted by the API; the legacy free text is no longer shown here.
  const noteCount = dossier.noteCount ?? 0

  // --- Verkoop & prijs: units still without a (complete) price, straight from each unit's status.
  const missingPrices = ws.billableActivities.filter((activity) => needsPricing(dossier, activity))

  function activitySecondary(activity: DossierActivity): ReactNode {
    if (activity.linkedOrderNumber) {
      const status = activity.linkedOrderStatus as TransportOrderStatus | null
      return (
        <>
          {t('dossiers.overview.activityOrder', { number: activity.linkedOrderNumber })}
          {status && (
            <Badge tone={ORDER_STATUS_TONE[status] ?? 'neutral'}>
              {ORDER_STATUS_LABELS[status] ? t(ORDER_STATUS_LABELS[status]) : status}
            </Badge>
          )}
        </>
      )
    }
    return activity.hasStops ? t('dossiers.overview.activityNoOrder') : null
  }

  /** Driver · plate · price (or price status) — the same rules as the activity card. */
  function activityFacts(activity: DossierActivity): ReactNode {
    const assignment = activity.assignment ?? null
    const status = activityPriceStatus(dossier, activity)
    return (
      <span className="dossier-ov-facts">
        {activity.hasStops && (
          <>
            <span className={assignment?.driverName ? undefined : 'is-missing'}>
              {assignment?.driverName ?? t('dossierActivities.card.notAssigned')}
            </span>
            <span className={assignment?.vehiclePlate ? undefined : 'is-missing'}>
              {assignment?.vehiclePlate ?? '—'}
              {assignment?.vehicleId && assignment.vehicleSelectionSource === 'Suggested' && ` (${t('dossierActivities.card.suggested')})`}
            </span>
          </>
        )}
        {/* The price when there is one; else what is missing — never an amount for a missing price. */}
        {status === 'PartiallyPriced' ? (
          <Badge tone={PRICE_STATUS_TONE[status]}>{t(PRICE_STATUS_LABEL_KEYS[status])}</Badge>
        ) : (
          status !== null && <span className={status === 'NotPriced' ? 'is-missing' : undefined}>{activityPriceText(t, dossier, activity)}</span>
        )}
      </span>
    )
  }

  return (
    <div className="dossier-overview">
      {hasRoute && (
        <OverviewCard tab="route" icon={MapPin} title={t('dossiers.overview.routeTitle')} status={routeStatus} action={t('dossiers.overview.openRoute')}>
          {transportActivities.length > 1 && routeActivity?.linkedOrderNumber && (
            <p className="dossier-ov-context">{t('dossiers.overview.routeForOrder', { number: routeActivity.linkedOrderNumber })}</p>
          )}
          {onSite && (
            <ol className="dossier-ov-route">
              <li className={siteStop ? 'is-filled' : undefined}>
                <span className="dossier-ov-route-label">{t('stopEditor.stopType.Site')}</span>
                <span className="dossier-ov-route-value">
                  {siteStop ? stopLine(t, siteStop) : t('dossiers.overview.notFilled')}
                  {siteStop && stopTiming(t, siteStop) && <small>{stopTiming(t, siteStop)}</small>}
                </span>
              </li>
            </ol>
          )}
          {!onSite && (
          <ol className="dossier-ov-route">
            <li className={loadingStop ? 'is-filled' : undefined}>
              <span className="dossier-ov-route-label">{t('dossiers.overview.loading')}</span>
              <span className="dossier-ov-route-value">
                {loadingStop ? stopLine(t, loadingStop) : t('dossiers.overview.notFilled')}
                {loadingStop && stopTiming(t, loadingStop) && <small>{stopTiming(t, loadingStop)}</small>}
              </span>
            </li>
            <li className={unloadingStop ? 'is-filled' : undefined}>
              <span className="dossier-ov-route-label">{t('dossiers.overview.unloading')}</span>
              <span className="dossier-ov-route-value">
                {unloadingStop ? stopLine(t, unloadingStop) : t('dossiers.overview.notFilled')}
                {unloadingStop && stopTiming(t, unloadingStop) && <small>{stopTiming(t, unloadingStop)}</small>}
              </span>
            </li>
          </ol>
          )}
          <p className="dossier-ov-inline">
            <Calendar size={16} aria-hidden />
            <span>{t('dossiers.overview.planning')}</span>
            <Badge tone={plannedStop ? 'info' : 'neutral'}>
              {plannedStop ? (stopTiming(t, plannedStop) ?? t('dossiers.overview.notPlanned')) : t('dossiers.overview.notPlanned')}
            </Badge>
          </p>
        </OverviewCard>
      )}

      <OverviewCard
        tab="activiteiten"
        icon={ListChecks}
        title={t('dossiers.overview.activitiesTitle')}
        status={{ label: t('dossiers.overview.activityCount', { count: activities.length }), tone: 'neutral' }}
        action={t('dossiers.overview.openActivities')}
      >
        {activities.length === 0 && <p className="placeholder-text">{t('dossiers.overview.noActivities')}</p>}
        {activities.length > 0 && (
          <ul className="dossier-ov-list">
            {activities.slice(0, MAX_ACTIVITY_PREVIEW).map((activity) => (
              <li key={activity.id}>
                {createElement(activityTypeIcon(activity.icon), { size: 18, 'aria-hidden': true })}
                <span className="dossier-ov-list-main">
                  <strong>{activity.activityTypeName}</strong>
                  {activity.label && <span className="dossier-ov-muted"> · {activity.label}</span>}
                  <span className="dossier-ov-list-sub">{activitySecondary(activity)}</span>
                  {activityFacts(activity)}
                </span>
              </li>
            ))}
          </ul>
        )}
        {activities.length > MAX_ACTIVITY_PREVIEW && (
          <p className="dossier-ov-muted">{t('dossiers.overview.moreActivities', { count: activities.length - MAX_ACTIVITY_PREVIEW })}</p>
        )}
      </OverviewCard>

      <OverviewCard tab="prijs" icon={Tag} title={t('dossiers.overview.priceTitle')} status={priceStatus} action={t('dossiers.overview.openPrice')}>
        {isPriced ? (
          <p className="dossier-ov-amount">
            <strong>{euro(fin.agreedOrderTotal)}</strong>
            {zero > 0 && <span className="dossier-ov-zero" title={t('dossiers.overview.priceZero')}>⚠</span>}
          </p>
        ) : (
          <p className="dossier-ov-amount">
            <strong className="dossier-price-none">{t('dossierSheet.price.noPrice')}</strong>
          </p>
        )}
        {billable > 0 && <p className="dossier-ov-muted">{t('dossierSheet.price.partialTotal', { priced, total: billable })}</p>}
        {missingPrices.length > 0 && (
          <ul className="dossier-ov-missing" aria-label={t('dossierActivities.overview.missingPrices')}>
            {missingPrices.map((activity) => (
              <li key={activity.id}>
                {activityReference(activity)}
                {activity.linkedOrderNumber && <span className="dossier-ov-muted"> · {activity.activityTypeName}</span>}:{' '}
                {t(PRICE_STATUS_LABEL_KEYS[activityPriceStatus(dossier, activity) ?? 'NotPriced'])}
              </li>
            ))}
          </ul>
        )}
        {!isPriced && (
          <p className="dossier-ov-muted">
            {t('dossiers.overview.priceNoneBody')} {t('dossiers.overview.priceNoneHint')}
          </p>
        )}
        {zero > 0 && <p className="dossier-price-zero-warning">{t('dossiers.overview.priceZero')}</p>}
      </OverviewCard>

      {hasGoods && (
        <OverviewCard
          tab="goederen"
          icon={Package}
          title={t('dossiers.overview.goodsTitle')}
          status={
            cargo.length > 0
              ? { label: t('dossiers.overview.goodsCount', { count: cargo.length }), tone: 'neutral' }
              : liftingWithoutGoods
                ? { label: t('dossierActivities.overview.goodsNotApplicable'), tone: 'neutral' }
                : { label: t('dossiers.overview.goodsNone'), tone: 'neutral' }
          }
          action={t('dossiers.overview.openGoods')}
        >
          {liftingWithoutGoods && <p>{t('dossiers.goods.noGoodsOnSiteLifting')}</p>}
          {cargo.length === 0 && !liftingWithoutGoods && (
            <>
              <p>{firstOrder?.goodsDescription ?? t('dossiers.overview.goodsNoneBody')}</p>
              {!firstLinkedOrderId && <p className="dossier-ov-muted">{t('dossiers.overview.goodsNoneHint')}</p>}
            </>
          )}
          {cargo.length > 0 && (
            <p className="dossier-ov-amount">
              <strong>{[...cargoByUnit].map(([unit, qty]) => `${qty} × ${unit}`).join(' · ')}</strong>
              {totalWeight > 0 && <span className="dossier-ov-muted">{t('dossiers.overview.goodsWeight', { kg: totalWeight })}</span>}
            </p>
          )}
        </OverviewCard>
      )}

      <OverviewCard
        tab="documenten"
        icon={FileText}
        title={t('dossiers.overview.documentsTitle')}
        status={{ label: t('dossiers.overview.documentCount', { count: documentCount }), tone: 'neutral' }}
        action={t('dossiers.overview.openDocuments')}
      >
        {documentCount === 0 ? (
          <>
            <p>{t('dossiers.overview.documentsNone')}</p>
            <p className="dossier-ov-muted">{t('dossiers.overview.documentsNoneHint')}</p>
          </>
        ) : (
          <p>{documentTypes.join(' · ')}</p>
        )}
      </OverviewCard>

      <OverviewCard
        tab="historiek"
        icon={History}
        title={t('dossiers.overview.historyTitle')}
        status={{ label: t('dossiers.overview.notesCount', { count: noteCount }), tone: 'neutral' }}
        action={t('dossiers.overview.openHistory')}
      >
        {noteCount > 0 ? (
          <p>
            <Link to={dossierTabPath(dossier.id, 'historiek')}>{t('dossierActivities.overview.dossierNotes', { count: noteCount })}</Link>
          </p>
        ) : (
          <>
            <p>{t('dossiers.overview.noNotes')}</p>
            <p className="dossier-ov-muted">{t('dossiers.overview.noNotesHint')}</p>
          </>
        )}
        {dossier.lastChangedAt && (
          <p className="dossier-ov-muted">{t('dossiers.overview.lastChanged', { when: formatDateTime(dossier.lastChangedAt) })}</p>
        )}
      </OverviewCard>
    </div>
  )
}
