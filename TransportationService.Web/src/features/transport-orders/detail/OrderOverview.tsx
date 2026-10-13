import { useEffect, useState, type ComponentType, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ArrowRight, Boxes, Clock, Euro, FileText, History, Image, MapPin, MessageSquare, Package } from 'lucide-react'
import { Badge } from '../../../components/ui/Badge'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { formatDateTime } from '../../../utils/dates'
import { formatCurrency, formatQuantity } from '../../../utils/numbers'
import { listCustomerMessages } from '../../customers/api/customerMessagesApi'
import { listOrderPackages } from '../../packages/api/packagesApi'
import { listOrderDocuments, ORDER_DOCUMENT_TYPE_LABELS, type OrderDocument } from '../api/orderDocumentsApi'
import { getTransportOrderTimeline, type OrderTimelineEvent } from '../api/transportOrdersApi'
import { OrderPortalReviewPanel } from '../components/OrderPortalReviewPanel'
import { ORDER_STATUS_LABELS, ORDER_STATUS_TONE, STOP_TYPE_LABELS } from '../types'
import { useOrderDetail } from './orderDetailContext'
import { orderTabPath, type OrderTab } from './orderSections'

interface CardProps {
  tab: OrderTab
  /** Unique per card (two cards may open the same tab): drives the heading id / aria-labelledby. */
  cardKey?: string
  icon: ComponentType<{ size?: number; 'aria-hidden'?: boolean }>
  title: string
  meta?: ReactNode
  action: string
  className?: string
  children: ReactNode
}

/** One summary panel of the overview; the header action is a real link to the subsection. */
function OverviewCard({ tab, cardKey, icon: Icon, title, meta, action, className, children }: CardProps) {
  const { order } = useOrderDetail()
  const key = cardKey ?? tab
  return (
    <article className={`tod-card${className ? ` ${className}` : ''}`} aria-labelledby={`tod-${key}-title`}>
      <header className="tod-card-head">
        <span className="tod-card-icon" aria-hidden>
          <Icon size={18} />
        </span>
        <h2 id={`tod-${key}-title`}>{title}</h2>
        {meta && <span className="tod-card-meta">{meta}</span>}
        <Link to={orderTabPath(order.id, tab)} className="tod-card-action">
          {action}
          <ArrowRight size={15} aria-hidden />
        </Link>
      </header>
      <div className="tod-card-body">{children}</div>
    </article>
  )
}

interface OverviewData {
  documents: OrderDocument[] | null
  packageCount: number | null
  timeline: OrderTimelineEvent[] | null
  messageCount: number | null
}

/**
 * The light lists the subsections use anyway, fetched once for the summary cards and gated by
 * the same permissions as those subsections. A failed call leaves its card in the "unknown"
 * state instead of breaking the overview.
 */
function useOverviewData(orderId: string, customerId: string, canViewPackages: boolean, canViewMessages: boolean): OverviewData {
  const [data, setData] = useState<OverviewData>({ documents: null, packageCount: null, timeline: null, messageCount: null })
  useEffect(() => {
    let mounted = true
    const safe = <T,>(promise: Promise<T>): Promise<T | null> => promise.catch(() => null)
    void Promise.all([
      safe(listOrderDocuments(orderId)),
      canViewPackages ? safe(listOrderPackages(orderId)) : Promise.resolve(null),
      safe(getTransportOrderTimeline(orderId)),
      canViewMessages ? safe(listCustomerMessages(customerId, orderId)) : Promise.resolve(null),
    ]).then(([documents, packages, timeline, messages]) => {
      if (!mounted) return
      setData({
        documents,
        packageCount: packages ? packages.length : null,
        timeline,
        messageCount: messages ? messages.length : null,
      })
    })
    return () => {
      mounted = false
    }
  }, [orderId, customerId, canViewPackages, canViewMessages])
  return data
}

function stopAddress(stop: { address: string | null; postalCode: string | null; city: string | null }): string {
  return [stop.address, [stop.postalCode, stop.city].filter(Boolean).join(' ')].filter(Boolean).join(', ')
}

/**
 * Redesign 2026-09-12 — summary-first overview (reference: TransportOpdracht.png): route,
 * sales & price, cargo, colli, photos & documents, history and portal messages as scannable
 * panels, each with one "Open …" link into the subsection. No editing controls live here.
 */
export function OrderOverview() {
  const { t } = useLocale()
  const { hasAnyPermission } = useAuth()
  const ws = useOrderDetail()
  const { order, priceDisplay, totalPrice, unitLabel, canViewPackages, canViewMessages, canChangeStatus } = ws
  const data = useOverviewData(order.id, order.customerId, canViewPackages, canViewMessages)

  const cargoTotals = ws.aggregateCargo(order.cargoItems)
  const quantityLabel =
    cargoTotals.length > 0
      ? cargoTotals.map((c) => `${formatQuantity(c.total)} ${c.unit}`).join(' · ')
      : order.quantity !== null
        ? `${order.quantity} ${unitLabel(order.quantityUnitCode, order.quantityUnit)}`.trim()
        : '—'
  const features = [
    order.adrRequired ? 'ADR' : null,
    order.craneRequired ? 'Kraan' : null,
    order.plateauRequired ? 'Plateau' : null,
    order.moffettRequired ? 'Moffett' : null,
    order.isReturnMovement ? 'Retour' : null,
  ].filter((f): f is string => f !== null)
  const documents = data.documents ?? []
  const attachments = documents.filter((d) => d.hasAttachment)

  return (
    <div className="tod-overview">
      {!ws.editing && canChangeStatus && <OrderPortalReviewPanel order={order} onReviewed={ws.setOrder} />}

      <div className="tod-overview-grid">
        <OverviewCard
          tab="stops"
          icon={MapPin}
          title="Route / Stops"
          meta={`${order.stops.length} ${order.stops.length === 1 ? 'stop' : 'stops'}`}
          action="Open stops"
        >
          {order.stops.length === 0 && <p className="tod-muted">Nog geen stops ingevuld.</p>}
          {order.stops.length > 0 && (
            <ol className="tod-route">
              {order.stops.map((stop) => (
                <li key={stop.id} className={stop.stopType === 'Loading' ? 'is-loading' : 'is-unloading'}>
                  <Badge tone={stop.stopType === 'Loading' ? 'info' : 'success'}>
                    {stop.sequence}. {t(STOP_TYPE_LABELS[stop.stopType])}
                  </Badge>
                  <span className="tod-route-place">
                    <strong>
                      {stop.locationName}
                      {stop.locationCode && <span className="to-loc-code"> ({stop.locationCode})</span>}
                    </strong>
                    <span className="tod-muted">{stopAddress(stop) || '—'}</span>
                  </span>
                  <span className="tod-route-time">
                    <Clock size={14} aria-hidden />
                    {stop.plannedFrom ? formatDateTime(stop.plannedFrom) : stop.plannedTo ? `tot ${formatDateTime(stop.plannedTo)}` : 'Niet gepland'}
                  </span>
                </li>
              ))}
            </ol>
          )}
        </OverviewCard>

        <OverviewCard tab="prijs" icon={Euro} title="Verkoop & prijs" action="Open prijsdetails">
          <dl className="tod-kv tod-kv-price">
            <div>
              <dt>Totaalprijs</dt>
              <dd className="tod-amount">{totalPrice !== null ? formatCurrency(totalPrice) : '—'}</dd>
            </div>
            <div>
              <dt>Prijsstatus</dt>
              <dd>
                {order.pricingSnapshot || totalPrice !== null ? (
                  <Badge tone={priceDisplay.tone}>{t(priceDisplay.labelKey)}</Badge>
                ) : (
                  <span className="tod-muted">Nog geen prijs</span>
                )}
              </dd>
            </div>
            <div>
              <dt>Opdracht</dt>
              <dd>
                <Badge tone={ORDER_STATUS_TONE[order.status]}>{t(ORDER_STATUS_LABELS[order.status])}</Badge>
              </dd>
            </div>
          </dl>
          {order.pricingSnapshot?.confirmedAtUtc && (
            <p className="tod-muted">
              Bevestigd op {formatDateTime(order.pricingSnapshot.confirmedAtUtc)}
              {order.pricingSnapshot.confirmedByName ? ` door ${order.pricingSnapshot.confirmedByName}` : ''}.
            </p>
          )}
        </OverviewCard>

        <OverviewCard tab="lading" icon={Package} title="Lading / Goederen" action="Open lading">
          <dl className="tod-kv">
            <div>
              <dt>Goederensoort</dt>
              <dd>{order.goodsDescription ?? '—'}</dd>
            </div>
            <div>
              <dt>Aantal</dt>
              <dd>{quantityLabel}</dd>
            </div>
            <div>
              <dt>Gewicht</dt>
              <dd>{order.weightKg !== null ? `${formatQuantity(order.weightKg)} kg` : '—'}</dd>
            </div>
            <div>
              <dt>Volume</dt>
              <dd>{order.volumeM3 !== null ? `${formatQuantity(order.volumeM3)} m³` : '—'}</dd>
            </div>
            <div>
              <dt>Palletten</dt>
              <dd>{order.palletCount ?? '—'}</dd>
            </div>
            <div>
              <dt>Kenmerken</dt>
              <dd>{features.length > 0 ? features.map((f) => <Badge key={f} tone="info">{f}</Badge>) : '—'}</dd>
            </div>
          </dl>
        </OverviewCard>

        <OverviewCard tab="colli" icon={Boxes} title="Colli" action="Open colli">
          {canViewPackages ? (
            <>
              <p className="tod-amount">{data.packageCount === null ? '…' : `${data.packageCount} colli`}</p>
              <p className="tod-muted">
                {data.packageCount === 0
                  ? 'Nog geen stukgoedige colli. Goederenlijnen zonder colli blijven op aantal gestaafd.'
                  : 'Scanbare colli van deze opdracht; details en labels in de Colli-sectie.'}
              </p>
            </>
          ) : (
            <p className="tod-muted">Colli van deze opdracht worden in de Colli-sectie samengevat.</p>
          )}
        </OverviewCard>

        <OverviewCard
          tab="lading"
          cardKey="fotos"
          icon={Image}
          title="Foto's & documenten"
          meta={data.documents === null ? undefined : `${documents.length} ${documents.length === 1 ? 'document' : 'documenten'}`}
          action="Open foto's"
          className="tod-card-media"
        >
          {documents.length === 0 && <p className="tod-muted">Nog geen foto's of documenten toegevoegd.</p>}
          {documents.length > 0 && (
            <ul className="tod-media-list">
              {documents.slice(0, 4).map((doc) => (
                <li key={doc.id} className="tod-media-item" title={doc.title}>
                  {hasAnyPermission(['orders.view', 'orders.edit', 'orders.manage']) && doc.hasAttachment ? <Image size={18} aria-hidden /> : <FileText size={18} aria-hidden />}
                  <span className="tod-media-label">{doc.title}</span>
                  <span className="tod-muted">{t(ORDER_DOCUMENT_TYPE_LABELS[doc.documentType])}</span>
                </li>
              ))}
            </ul>
          )}
          {documents.length > 0 && (
            <p className="tod-muted">
              {attachments.length} met bestand
              {documents.length > 4 ? ` · + ${documents.length - 4} meer` : ''}
            </p>
          )}
        </OverviewCard>

        <OverviewCard tab="historiek" icon={History} title="Historiek" action="Open historiek">
          {data.timeline === null && <p className="tod-muted">…</p>}
          {data.timeline && data.timeline.length === 0 && <p className="tod-muted">Nog geen gebeurtenissen.</p>}
          {data.timeline && data.timeline.length > 0 && (
            <ul className="tod-timeline">
              {data.timeline.slice(0, 3).map((event, index) => (
                <li key={`${event.timestamp}-${index}`}>
                  <span className="tod-timeline-time">{formatDateTime(event.timestamp)}</span>
                  <span className="tod-timeline-title">{event.title}</span>
                  {event.userName && <span className="tod-muted">— {event.userName}</span>}
                </li>
              ))}
            </ul>
          )}
        </OverviewCard>

        <OverviewCard tab="berichten" icon={MessageSquare} title="Berichten (klantportaal)" action="Open berichten">
          {!canViewMessages && <p className="tod-muted">Je hebt geen toegang tot de portaalberichten van deze opdracht.</p>}
          {canViewMessages && data.messageCount === 0 && (
            <p className="tod-empty-chat">
              <MessageSquare size={16} aria-hidden /> Nog geen berichten in dit gesprek.
            </p>
          )}
          {canViewMessages && data.messageCount !== null && data.messageCount > 0 && (
            <p>
              <strong>{data.messageCount}</strong> {data.messageCount === 1 ? 'bericht' : 'berichten'} in dit gesprek.
            </p>
          )}
        </OverviewCard>
      </div>
    </div>
  )
}
