import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { LoadingState } from '../../../components/feedback/LoadingState'
import {
  acknowledgePortalFeedMessage,
  listPortalFeedMessages,
  markPortalFeedMessageRead,
  type PortalFeedMessage,
  type PortalFeedPriority,
} from '../api/customerPortalApi'
import './customer-portal-pages.css'

function formatDateTime(iso: string): string {
  const date = new Date(iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`)
  return date.toLocaleString('nl-BE', { dateStyle: 'short', timeStyle: 'short' })
}

/** Hoog/Dringend get a pill; Normal renders nothing. */
function PriorityBadge({ priority }: { priority: PortalFeedPriority }) {
  if (priority === 'High') return <Badge tone="warning">Hoog</Badge>
  if (priority === 'Urgent') return <Badge tone="danger">Dringend</Badge>
  return null
}

/** Staff-authored announcements feed for the customer portal (route /klantportaal/mededelingen). */
export function CustomerPortalNoticesPage() {
  const navigate = useNavigate()
  const [messages, setMessages] = useState<PortalFeedMessage[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [openMessage, setOpenMessage] = useState<PortalFeedMessage | null>(null)
  const [ackBusy, setAckBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    listPortalFeedMessages()
      .then((rows) => {
        if (!mounted) return
        setMessages(rows)
        setLoaded(true)
      })
      .catch(() => {
        if (!mounted) return
        setError('De mededelingen konden niet worden geladen.')
        setLoaded(true)
      })
    return () => {
      mounted = false
    }
  }, [])

  function openMessageDetail(message: PortalFeedMessage) {
    setOpenMessage(message)
    if (message.readAt === null) {
      const readAt = new Date().toISOString()
      void markPortalFeedMessageRead(message.id)
        .then(() => {
          setMessages((current) => current.map((m) => (m.id === message.id ? { ...m, readAt } : m)))
          setOpenMessage((current) => (current && current.id === message.id ? { ...current, readAt } : current))
        })
        .catch(() => {})
    }
  }

  async function handleAcknowledge(message: PortalFeedMessage) {
    setAckBusy(true)
    try {
      await acknowledgePortalFeedMessage(message.id)
      const acknowledgedAt = new Date().toISOString()
      setMessages((current) => current.map((m) => (m.id === message.id ? { ...m, acknowledgedAt } : m)))
      setOpenMessage((current) => (current && current.id === message.id ? { ...current, acknowledgedAt } : current))
    } catch {
      setError('Het bericht kon niet worden bevestigd.')
    } finally {
      setAckBusy(false)
    }
  }

  function relatedLink(message: PortalFeedMessage) {
    if (!message.relatedEntityType || !message.relatedEntityId) return null
    const to =
      message.relatedEntityType === 'order'
        ? `/klantportaal/orders/${message.relatedEntityId}`
        : `/klantportaal/facturen/${message.relatedEntityId}`
    const label = message.relatedEntityType === 'order' ? 'Bekijk opdracht' : 'Bekijk factuur'
    return (
      <Button variant="secondary" onClick={() => navigate(to)}>
        {label}
      </Button>
    )
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Klantportaal', to: '/klantportaal' }, { label: 'Mededelingen' }]} />
      <PageHeader title="Mededelingen" subtitle="Berichten van uw transporteur." />

      {error && <p className="placeholder-text" role="alert">{error}</p>}
      {!loaded && <LoadingState message="Mededelingen laden..." />}
      {loaded && messages.length === 0 && <p className="placeholder-text">Er zijn nog geen mededelingen.</p>}

      {loaded && messages.length > 0 && (
        <ul className="cpp-list">
          {messages.map((message) => (
            <li key={message.id}>
              <button
                type="button"
                className={message.readAt === null ? 'cpp-row cpp-notice-unread' : 'cpp-row'}
                onClick={() => openMessageDetail(message)}
              >
                <span className="cpp-notice-title">
                  {message.readAt === null && <span className="cpp-notice-dot" aria-label="Ongelezen" />}
                  <PriorityBadge priority={message.priority} />
                  {message.requiresAcknowledgement &&
                    (message.acknowledgedAt === null ? (
                      <Badge tone="warning">te bevestigen</Badge>
                    ) : (
                      <Badge tone="success">bevestigd</Badge>
                    ))}
                  <strong>{message.title}</strong>
                </span>
                <span>{formatDateTime(message.publishedAt)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}

      {openMessage && (
        <Modal
          title={openMessage.title}
          onClose={() => setOpenMessage(null)}
          busy={ackBusy}
          footer={
            <>
              {relatedLink(openMessage)}
              {openMessage.requiresAcknowledgement && openMessage.acknowledgedAt === null && (
                <Button onClick={() => void handleAcknowledge(openMessage)} disabled={ackBusy}>
                  {ackBusy ? 'Bevestigen…' : 'Bevestigen'}
                </Button>
              )}
              <Button variant="secondary" onClick={() => setOpenMessage(null)} disabled={ackBusy}>
                Sluiten
              </Button>
            </>
          }
        >
          <p className="cpp-message-meta">
            Gepubliceerd op {formatDateTime(openMessage.publishedAt)} <PriorityBadge priority={openMessage.priority} />
          </p>
          <p className="cpp-notice-body">{openMessage.body}</p>
          {openMessage.requiresAcknowledgement && openMessage.acknowledgedAt !== null && (
            <p className="cpp-message-meta">Bevestigd op {formatDateTime(openMessage.acknowledgedAt)}</p>
          )}
        </Modal>
      )}
    </div>
  )
}
