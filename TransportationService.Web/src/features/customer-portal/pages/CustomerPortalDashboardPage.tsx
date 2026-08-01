import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { INVOICE_STATUS_TONE, type InvoiceStatus } from '../../invoices/types'
import {
  acknowledgePortalFeedMessage,
  getPortalDashboard,
  listPortalFeedMessages,
  type PortalDashboard,
  type PortalFeedMessage,
} from '../api/customerPortalApi'
import { invoiceStatusLabel } from './portalStatusLabels'
import './customer-portal-pages.css'

/** Portal landing page: at-a-glance cards linking into every other portal module. */
export function CustomerPortalDashboardPage() {
  const { user, hasPermission } = useAuth()
  const { t, formatDate, formatDateTime, formatCurrency } = useLocale()
  const navigate = useNavigate()
  const [dashboard, setDashboard] = useState<PortalDashboard | null>(null)
  const [feedMessages, setFeedMessages] = useState<PortalFeedMessage[]>([])
  const [error, setError] = useState(false)
  const [ackBusy, setAckBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    getPortalDashboard()
      .then((data) => {
        if (mounted) setDashboard(data)
      })
      .catch(() => {
        if (mounted) setError(true)
      })
    // Staff-authored portal messages drive the dashboard banners and the blocking overlay;
    // a failure here must never take the dashboard down.
    listPortalFeedMessages()
      .then((rows) => {
        if (mounted) setFeedMessages(rows)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  async function handleBlockingAcknowledge(message: PortalFeedMessage) {
    setAckBusy(true)
    try {
      await acknowledgePortalFeedMessage(message.id)
      const acknowledgedAt = new Date().toISOString()
      setFeedMessages((current) => current.map((m) => (m.id === message.id ? { ...m, acknowledgedAt } : m)))
    } catch {
      // Keep the overlay: the confirmation did not reach the server.
    } finally {
      setAckBusy(false)
    }
  }

  if (error) return <ErrorState message={t('dashboard.loadError')} />
  if (!dashboard) return <LoadingState message={t('dashboard.loading')} />

  const bannerMessages = feedMessages.filter((m) => m.displayMode === 'DashboardBanner')
  // Only the dashboard is covered; the portal navigation around it stays usable.
  const blockingMessage = feedMessages.find(
    (m) => m.displayMode === 'BlockingAcknowledgement' && m.acknowledgedAt === null,
  )

  return (
    <div className="cpp-dashboard">
      <PageHeader
        title={user?.firstName ? t('dashboard.welcomeNamed', { name: user.firstName }) : t('dashboard.welcome')}
        subtitle={t('navigation.portalName')}
      />

      {dashboard.announcements.map((a) => (
        <div key={a.id} className="cpp-announcement">
          <h3>{a.title}</h3>
          <p>{a.body}</p>
        </div>
      ))}

      {bannerMessages.map((m) => (
        <div key={m.id} className={m.priority === 'Urgent' ? 'cpp-announcement cpp-announcement-urgent' : 'cpp-announcement'}>
          <h3>{m.title}</h3>
          <p>{m.body}</p>
        </div>
      ))}

      {blockingMessage && (
        <div className="cpp-blocking-overlay" role="alertdialog" aria-modal="true" aria-label={blockingMessage.title}>
          <div className="cpp-blocking-card">
            <h2>{blockingMessage.title}</h2>
            <p>{blockingMessage.body}</p>
            <Button onClick={() => void handleBlockingAcknowledge(blockingMessage)} disabled={ackBusy}>
              {ackBusy ? t('dashboard.acknowledging') : t('dashboard.acknowledge')}
            </Button>
          </div>
        </div>
      )}

      <div className="cpp-cards">
        <button type="button" className="cpp-card" onClick={() => navigate('/klantportaal')}>
          <span className="cpp-card-label">{t('dashboard.cards.activeOrders')}</span>
          <span className="cpp-card-value">{dashboard.activeOrders}</span>
        </button>
        <button
          type="button"
          className={dashboard.problemOrders > 0 ? 'cpp-card cpp-card-alert' : 'cpp-card'}
          onClick={() => navigate('/klantportaal')}
        >
          <span className="cpp-card-label">{t('dashboard.cards.attention')}</span>
          <span className="cpp-card-value">{dashboard.problemOrders}</span>
        </button>
        {hasPermission('customer_portal.messages') && (
          <button
            type="button"
            className={dashboard.unreadMessages > 0 ? 'cpp-card cpp-card-alert' : 'cpp-card'}
            onClick={() => navigate('/klantportaal/berichten')}
          >
            <span className="cpp-card-label">{t('dashboard.cards.unreadMessages')}</span>
            <span className="cpp-card-value">{dashboard.unreadMessages}</span>
          </button>
        )}
        {hasPermission('customer_portal.view_invoices') && (
          <button type="button" className="cpp-card" onClick={() => navigate('/klantportaal/facturen')}>
            <span className="cpp-card-label">{t('dashboard.cards.recentInvoices')}</span>
            <span className="cpp-card-value">{dashboard.recentInvoices.length}</span>
          </button>
        )}
      </div>

      <section className="cpp-panel">
        <h2>{t('dashboard.upcomingTitle')}</h2>
        {dashboard.upcomingDeliveries.length === 0 && <p className="placeholder-text">{t('dashboard.noDeliveries')}</p>}
        {dashboard.upcomingDeliveries.length > 0 && (
          <ul className="cpp-list">
            {dashboard.upcomingDeliveries.map((d) => (
              <li key={d.orderId}>
                <button type="button" className="cpp-row" onClick={() => navigate(`/klantportaal/orders/${d.orderId}`)}>
                  <span>
                    <strong>{d.orderNumber}</strong> {d.city ? `— ${d.city}` : ''}
                  </span>
                  <span>{formatDateTime(d.plannedAt)}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      {hasPermission('customer_portal.view_invoices') && (
        <section className="cpp-panel">
          <h2>{t('dashboard.recentInvoicesTitle')}</h2>
          {dashboard.recentInvoices.length === 0 && <p className="placeholder-text">{t('dashboard.noInvoices')}</p>}
          {dashboard.recentInvoices.length > 0 && (
            <ul className="cpp-list">
              {dashboard.recentInvoices.map((i) => (
                <li key={i.id}>
                  <button type="button" className="cpp-row" onClick={() => navigate(`/klantportaal/facturen/${i.id}`)}>
                    <span>
                      <strong>{i.invoiceNumber}</strong> — {formatDate(i.invoiceDate)}
                    </span>
                    <span>
                      {formatCurrency(i.total)}{' '}
                      <Badge tone={INVOICE_STATUS_TONE[i.status as InvoiceStatus] ?? 'neutral'}>
                        {invoiceStatusLabel(t, i.status)}
                      </Badge>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}
    </div>
  )
}
