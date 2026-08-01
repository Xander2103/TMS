import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useAuth } from '../../auth/authContextValue'
import { euro } from '../../invoices/types'
import { INVOICE_STATUS_LABELS, INVOICE_STATUS_TONE, type InvoiceStatus } from '../../invoices/types'
import {
  acknowledgePortalFeedMessage,
  getPortalDashboard,
  listPortalFeedMessages,
  type PortalDashboard,
  type PortalFeedMessage,
} from '../api/customerPortalApi'
import './customer-portal-pages.css'

function formatDateTime(iso: string): string {
  const date = new Date(iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`)
  return date.toLocaleString('nl-BE', { dateStyle: 'short', timeStyle: 'short' })
}

/** Portal landing page: at-a-glance cards linking into every other portal module. */
export function CustomerPortalDashboardPage() {
  const { user, hasPermission } = useAuth()
  const navigate = useNavigate()
  const [dashboard, setDashboard] = useState<PortalDashboard | null>(null)
  const [feedMessages, setFeedMessages] = useState<PortalFeedMessage[]>([])
  const [error, setError] = useState<string | null>(null)
  const [ackBusy, setAckBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    getPortalDashboard()
      .then((data) => {
        if (mounted) setDashboard(data)
      })
      .catch(() => {
        if (mounted) setError('Het dashboard kon niet worden geladen.')
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

  if (error) return <ErrorState message={error} />
  if (!dashboard) return <LoadingState message="Dashboard laden..." />

  const bannerMessages = feedMessages.filter((m) => m.displayMode === 'DashboardBanner')
  // Only the dashboard is covered; the portal navigation around it stays usable.
  const blockingMessage = feedMessages.find(
    (m) => m.displayMode === 'BlockingAcknowledgement' && m.acknowledgedAt === null,
  )

  return (
    <div className="cpp-dashboard">
      <PageHeader title={`Welkom${user?.firstName ? `, ${user.firstName}` : ''}`} subtitle="Klantportaal" />

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
              {ackBusy ? 'Bevestigen…' : 'Ik bevestig'}
            </Button>
          </div>
        </div>
      )}

      <div className="cpp-cards">
        <button type="button" className="cpp-card" onClick={() => navigate('/klantportaal')}>
          <span className="cpp-card-label">Actieve opdrachten</span>
          <span className="cpp-card-value">{dashboard.activeOrders}</span>
        </button>
        <button
          type="button"
          className={dashboard.problemOrders > 0 ? 'cpp-card cpp-card-alert' : 'cpp-card'}
          onClick={() => navigate('/klantportaal')}
        >
          <span className="cpp-card-label">Aandachtspunten</span>
          <span className="cpp-card-value">{dashboard.problemOrders}</span>
        </button>
        {hasPermission('customer_portal.messages') && (
          <button
            type="button"
            className={dashboard.unreadMessages > 0 ? 'cpp-card cpp-card-alert' : 'cpp-card'}
            onClick={() => navigate('/klantportaal/berichten')}
          >
            <span className="cpp-card-label">Ongelezen berichten</span>
            <span className="cpp-card-value">{dashboard.unreadMessages}</span>
          </button>
        )}
        {hasPermission('customer_portal.view_invoices') && (
          <button type="button" className="cpp-card" onClick={() => navigate('/klantportaal/facturen')}>
            <span className="cpp-card-label">Recente facturen</span>
            <span className="cpp-card-value">{dashboard.recentInvoices.length}</span>
          </button>
        )}
      </div>

      <section className="cpp-panel">
        <h2>Aankomende leveringen (7 dagen)</h2>
        {dashboard.upcomingDeliveries.length === 0 && <p className="placeholder-text">Geen leveringen gepland.</p>}
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
          <h2>Recente facturen</h2>
          {dashboard.recentInvoices.length === 0 && <p className="placeholder-text">Nog geen facturen.</p>}
          {dashboard.recentInvoices.length > 0 && (
            <ul className="cpp-list">
              {dashboard.recentInvoices.map((i) => (
                <li key={i.id}>
                  <button type="button" className="cpp-row" onClick={() => navigate(`/klantportaal/facturen/${i.id}`)}>
                    <span>
                      <strong>{i.invoiceNumber}</strong> — {i.invoiceDate}
                    </span>
                    <span>
                      {euro(i.total)}{' '}
                      <Badge tone={INVOICE_STATUS_TONE[i.status as InvoiceStatus] ?? 'neutral'}>
                        {INVOICE_STATUS_LABELS[i.status as InvoiceStatus] ?? i.status}
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
