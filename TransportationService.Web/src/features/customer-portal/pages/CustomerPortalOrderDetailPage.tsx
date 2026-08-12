import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { BackButton } from '../../../components/ui/BackButton'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useAuth } from '../../auth/authContextValue'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { ORDER_STATUS_TONE } from '../../transport-orders/types'
import {
  downloadPortalDocument,
  getPortalOrder,
  listPortalDocuments,
  type PortalDocument,
  type PortalOrderDetail,
} from '../api/customerPortalApi'
import { exceptionStatusLabel, orderStatusLabel, stopTypeLabel, unitTypeLabel } from './portalStatusLabels'
import './customer-portal-pages.css'

function formatWindow(t: TranslateFn, from: string | null, to: string | null): string {
  const fmt = (value: string) => value.slice(0, 16).replace('T', ' ')
  if (from && to) return `${fmt(from)} – ${fmt(to)}`
  if (from) return t('orders.detail.windowFrom', { time: fmt(from) })
  if (to) return t('orders.detail.windowTo', { time: fmt(to) })
  return '—'
}

/** Customer-facing order detail: status, stops and cargo — no internal pricing or planning. */
export function CustomerPortalOrderDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const { hasPermission } = useAuth()
  const { t, formatDate } = useLocale()
  const [order, setOrder] = useState<PortalOrderDetail | null>(null)
  const [documents, setDocuments] = useState<PortalDocument[]>([])
  const [error, setError] = useState(false)
  const [downloadError, setDownloadError] = useState(false)

  useEffect(() => {
    let mounted = true
    getPortalOrder(id)
      .then((data) => {
        if (mounted) setOrder(data)
      })
      .catch(() => {
        if (mounted) setError(true)
      })
    if (hasPermission('customer_portal.view_documents')) {
      listPortalDocuments()
        .then((rows) => {
          if (mounted) setDocuments(rows.filter((d) => d.orderId === id))
        })
        .catch(() => {
          // Non-fatal: the documents section just stays empty.
        })
    }
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id])

  if (error) return <ErrorState message={t('orders.detail.loadError')} />
  if (!order) return <LoadingState message={t('orders.detail.loading')} />

  const subtitle = `${t('orders.detail.submittedFor', { date: formatDate(order.orderDate) })}${
    order.customerReference ? ` · ${t('orders.detail.yourRef', { reference: order.customerReference })}` : ''
  }`

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.portalName'), to: '/klantportaal' }, { label: order.orderNumber }]} />
      <BackButton to="/klantportaal" label={t('orders.detail.back')} />
      <PageHeader
        title={order.orderNumber}
        subtitle={subtitle}
        action={
          <>
            <Badge tone={ORDER_STATUS_TONE[order.status]}>{orderStatusLabel(t, order.status)}</Badge>{' '}
            {hasPermission('customer_portal.messages') && (
              <Link to={`/klantportaal/berichten?orderId=${order.id}`}>
                <Button variant="secondary">{t('orders.detail.messagesButton')}</Button>
              </Link>
            )}
          </>
        }
      />

      {order.cancellationReason && (
        <p className="to-cancel-reason" role="note">
          {t('orders.detail.cancelledReason', { reason: order.cancellationReason })}
        </p>
      )}

      {order.expectedDeliveryEta && (
        <p className="cpp-eta" role="status">
          {t('orders.detail.expectedDelivery')}:{' '}
          <strong>{new Date(order.expectedDeliveryEta).toLocaleString()}</strong>
        </p>
      )}

      {order.timeline.length > 0 && (
        <section className="cpp-panel">
          <h2>{t('orders.detail.statusTitle')}</h2>
          <ul className="cpp-list">
            {order.timeline.map((event, index) => (
              <li key={index} className="cpp-row">
                <span>
                  <Badge tone={ORDER_STATUS_TONE[event.status]}>{orderStatusLabel(t, event.status)}</Badge>
                  {event.reason ? ` — ${event.reason}` : ''}
                </span>
                <span>{event.changedAt.slice(0, 16).replace('T', ' ')}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {order.exceptions.length > 0 && (
        <section className="cpp-panel">
          <h2>{t('orders.detail.attentionTitle')}</h2>
          <ul className="cpp-list">
            {order.exceptions.map((exception, index) => (
              <li key={index} className="cpp-row">
                <span>{exception.description}</span>
                <Badge tone="warning">{exceptionStatusLabel(t, exception.status)}</Badge>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section className="to-section">
        <h2>{t('orders.detail.stopsTitle')}</h2>
        <table className="to-stops-table">
          <thead>
            <tr>
              <th>#</th>
              <th>{t('orders.detail.stopColumns.type')}</th>
              <th>{t('orders.detail.stopColumns.location')}</th>
              <th>{t('orders.detail.stopColumns.window')}</th>
              <th>{t('orders.detail.stopColumns.reference')}</th>
            </tr>
          </thead>
          <tbody>
            {order.stops.map((stop) => (
              <tr key={stop.sequence}>
                <td>{stop.sequence}</td>
                <td>{stopTypeLabel(t, stop.stopType)}</td>
                <td>
                  {stop.locationName}
                  {stop.city ? `, ${stop.city}` : ''}
                </td>
                <td>{formatWindow(t, stop.requestedFrom, stop.requestedTo)}</td>
                <td>{stop.reference ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      {order.cargoItems.length > 0 && (
        <section className="to-section">
          <h2>{t('orders.detail.goodsTitle')}</h2>
          <table className="to-stops-table">
            <thead>
              <tr>
                <th>#</th>
                <th>{t('orders.detail.goodsColumns.description')}</th>
                <th>{t('orders.detail.goodsColumns.quantity')}</th>
                <th>{t('orders.detail.goodsColumns.type')}</th>
                <th>{t('orders.detail.goodsColumns.adr')}</th>
              </tr>
            </thead>
            <tbody>
              {order.cargoItems.map((item) => (
                <tr key={item.sequence}>
                  <td>{item.sequence}</td>
                  <td>{item.description}</td>
                  <td>
                    {item.expectedQuantity} {item.quantityUnit ?? ''}
                  </td>
                  <td>{item.unitType ? unitTypeLabel(t, item.unitType) : '—'}</td>
                  <td>{item.adrRequired ? <Badge tone="danger">ADR</Badge> : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      {order.notes && (
        <section className="to-section">
          <h2>{t('orders.detail.remarksTitle')}</h2>
          <p>{order.notes}</p>
        </section>
      )}

      {hasPermission('customer_portal.view_documents') && documents.length > 0 && (
        <section className="cpp-panel">
          <h2>{t('orders.detail.documentsTitle')}</h2>
          {downloadError && <p className="placeholder-text" role="alert">{t('errors.documentDownload')}</p>}
          <ul className="cpp-list">
            {documents.map((doc) => (
              <li key={`${doc.source}-${doc.id}`}>
                <button
                  type="button"
                  className="link-button"
                  onClick={() =>
                    void downloadPortalDocument(doc.source, doc.id, doc.fileName ?? doc.title).catch(() =>
                      setDownloadError(true),
                    )
                  }
                >
                  {doc.title}
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}
