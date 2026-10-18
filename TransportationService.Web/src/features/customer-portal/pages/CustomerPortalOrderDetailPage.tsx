import { useEffect, useState, type FormEvent } from 'react'
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
import {
  downloadPortalDocument,
  getPortalOrder,
  listPortalDocuments,
  sendPortalMessage,
  type PortalDocument,
  type PortalOrderDetail,
} from '../api/customerPortalApi'
import {
  documentSourceLabel,
  exceptionStatusLabel,
  isShortfallStatus,
  portalExceptionTypeLabel,
  portalOrderStatus,
  stopTypeLabel,
  unitTypeLabel,
} from './portalStatusLabels'
import './customer-portal-pages.css'

/**
 * C-03: the requested window is a UTC instant on the wire. It used to be shown by slicing the
 * ISO string, which printed UTC to the customer while the internal screens printed something
 * else. It now runs through the locale formatter, which renders the DATE in the customer's
 * language but the CLOCK in the carrier's (tenant) zone — the dock appointment is the same
 * moment for both sides of the order.
 */
function formatWindow(
  fmt: (iso: string) => string, t: TranslateFn, from: string | null, to: string | null,
): string {
  if (from && to) return `${fmt(from)} – ${fmt(to)}`
  if (from) return t('orders.detail.windowFrom', { time: fmt(from) })
  if (to) return t('orders.detail.windowTo', { time: fmt(to) })
  return '—'
}

/** Customer-facing order detail: status, stops and cargo — no internal pricing or planning. */
export function CustomerPortalOrderDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const { hasPermission } = useAuth()
  const { t, formatDate, formatDateTime } = useLocale()
  const [order, setOrder] = useState<PortalOrderDetail | null>(null)
  const [documents, setDocuments] = useState<PortalDocument[]>([])
  const [error, setError] = useState(false)
  const [downloadError, setDownloadError] = useState(false)
  const [answer, setAnswer] = useState('')
  const [answering, setAnswering] = useState(false)
  const [answerError, setAnswerError] = useState<string | null>(null)

  async function submitAnswer(event: FormEvent) {
    event.preventDefault()
    if (!order || answering) return
    if (!answer.trim()) {
      setAnswerError(t('orders.detail.infoRequest.answerRequired'))
      return
    }
    setAnswering(true)
    setAnswerError(null)
    try {
      await sendPortalMessage(order.id, answer.trim())
      setAnswer('')
      setOrder(await getPortalOrder(order.id))
    } catch {
      setAnswerError(t('orders.detail.infoRequest.answerFailed'))
    } finally {
      setAnswering(false)
    }
  }

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

  const shownStatus = portalOrderStatus(t, order.status)
  const canMessage = hasPermission('customer_portal.messages')

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.portalName'), to: '/klantportaal' }, { label: order.orderNumber }]} />
      <BackButton to="/klantportaal" label={t('orders.detail.back')} />
      <PageHeader
        title={order.orderNumber}
        subtitle={subtitle}
        action={
          <>
            <Badge tone={shownStatus.tone}>{shownStatus.label}</Badge>{' '}
            {order.invoiceAvailable && (
              // Invoicing is shown next to — never instead of — the delivery status.
              <Link to="/klantportaal/facturen" className="cpp-invoice-link">
                <Badge tone="info">{t('orders.detail.invoiceAvailable')}</Badge>
              </Link>
            )}{' '}
            {hasPermission('customer_portal.messages') && (
              <Link to={`/klantportaal/berichten?orderId=${order.id}`}>
                <Button variant="secondary">{t('orders.detail.messagesButton')}</Button>
              </Link>
            )}
          </>
        }
      />

      {order.status === 'Received' && <p className="cpp-eta" role="status">{t('orders.detail.statusHint.Received')}</p>}
      {order.status === 'InReview' && !order.infoRequest?.answeredAt && (
        <p className="cpp-eta" role="status">{t('orders.detail.statusHint.InReview')}</p>
      )}
      {(order.status === 'Rejected' || order.status === 'Cancelled') && (
        <p className="cpp-eta" role="status">{t(`orders.detail.statusHint.${order.status}`)}</p>
      )}

      {order.infoRequest && (
        <section className="cpp-panel cpp-info-request" aria-labelledby="cpp-info-request-title">
          <h2 id="cpp-info-request-title">
            {order.infoRequest.answeredAt ? t('orders.detail.infoRequest.answeredTitle') : t('orders.detail.infoRequest.title')}
          </h2>
          <p className="cpp-info-question">
            <span className="cpp-info-label">{t('orders.detail.infoRequest.question', { time: formatDateTime(order.infoRequest.askedAt) })}</span>
            <q>{order.infoRequest.question}</q>
          </p>
          {order.infoRequest.answeredAt ? (
            <p role="status">{t('orders.detail.infoRequest.answered', { time: formatDateTime(order.infoRequest.answeredAt) })}</p>
          ) : canMessage ? (
            <form onSubmit={submitAnswer} noValidate>
              <label htmlFor="cpp-info-answer">{t('orders.detail.infoRequest.answerLabel')}</label>
              <textarea
                id="cpp-info-answer"
                rows={3}
                maxLength={4000}
                value={answer}
                onChange={(e) => setAnswer(e.target.value)}
                disabled={answering}
                aria-invalid={answerError ? true : undefined}
              />
              {answerError && <p className="cpp-field-error" role="alert">{answerError}</p>}
              <Button type="submit" disabled={answering}>
                {answering ? t('orders.detail.infoRequest.sending') : t('orders.detail.infoRequest.send')}
              </Button>
            </form>
          ) : (
            <p>{t('orders.detail.infoRequest.noMessageRight')}</p>
          )}
        </section>
      )}

      {isShortfallStatus(order.status) && (
        // EX-08: the truthful outcome, and the redelivery once the customer may see that order.
        <p className="cpp-eta" role="status">
          {t(`orders.detail.deliveryOutcome.${order.status}`)}
          {order.redeliveryOrderId && order.redeliveryOrderNumber && (
            <>
              {' '}
              <Link to={`/klantportaal/orders/${order.redeliveryOrderId}`}>{t('orders.detail.redeliveryLink', { order: order.redeliveryOrderNumber })}</Link>
            </>
          )}
        </p>
      )}

      {(order.notices ?? []).length > 0 && (
        <section className="cpp-panel">
          <h2>{t('orders.detail.noticesTitle')}</h2>
          <ul className="cpp-list">
            {(order.notices ?? []).map((notice, index) => (
              <li key={index} className="cpp-row">
                <span>{notice.message}</span>
                <span>{formatDateTime(notice.at)}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {order.expectedDeliveryEta && (
        <p className="cpp-eta" role="status">
          {t('orders.detail.expectedDelivery')}:{' '}
          <strong>{formatDateTime(order.expectedDeliveryEta)}</strong>
        </p>
      )}

      {order.pod && (
        <section className="cpp-panel">
          <h2>{t('orders.detail.podTitle')}</h2>
          <ul className="cpp-list">
            <li className="cpp-row">
              <span>{t('orders.detail.podDeliveredAt', { time: formatDateTime(order.pod.deliveredAt) })}</span>
              {order.pod.outcome && (
                <Badge tone={order.pod.outcome === 'Complete' ? 'success' : 'warning'}>
                  {t(`orders.detail.podOutcomes.${order.pod.outcome}`)}
                </Badge>
              )}
            </li>
            <li className="cpp-row">
              <span>{t('orders.detail.podRecipient', { name: order.pod.recipientName })}</span>
            </li>
          </ul>
        </section>
      )}

      {order.timeline.length > 0 && (
        <section className="cpp-panel">
          <h2>{t('orders.detail.statusTitle')}</h2>
          <ul className="cpp-list">
            {order.timeline.map((event, index) => (
              <li key={index} className="cpp-row">
                <span>
                  {/* Customer language per step, decided server-side; the close of a not (fully)
                      delivered order reads "Uitvoering afgesloten — niet volledig geleverd". */}
                  <Badge tone={portalOrderStatus(t, event.status).tone}>{portalOrderStatus(t, event.status).label}</Badge>
                </span>
                <span>{formatDateTime(event.changedAt)}</span>
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
                {/* Customer-safe: the office's message, else only the kind of problem — never the driver's report. */}
                <span>{exception.description || portalExceptionTypeLabel(t, exception.type)}</span>
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
                <td>{formatWindow(formatDateTime, t, stop.requestedFrom, stop.requestedTo)}</td>
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
                {doc.source === 'IssuedTransportDocument' && <span className="cpp-doc-kind"> · {documentSourceLabel(t, doc.source)}</span>}
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}
