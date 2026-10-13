import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { useLocale } from '../../../i18n/localeContext'
import {
  acknowledgePortalFeedMessage,
  listPortalFeedMessages,
  markPortalFeedMessageRead,
  type PortalFeedMessage,
  type PortalFeedPriority,
} from '../api/customerPortalApi'
import './customer-portal-pages.css'

/** Hoog/Dringend get a pill; Normal renders nothing. */
function PriorityBadge({ priority }: { priority: PortalFeedPriority }) {
  const { t } = useLocale()
  if (priority === 'High') return <Badge tone="warning">{t('notifications.priority.high')}</Badge>
  if (priority === 'Urgent') return <Badge tone="danger">{t('notifications.priority.urgent')}</Badge>
  return null
}

/** Staff-authored announcements feed for the customer portal (route /klantportaal/mededelingen). */
export function CustomerPortalNoticesPage() {
  const navigate = useNavigate()
  const { t, formatDateTime } = useLocale()
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
        setError(t('notifications.loadError'))
        setLoaded(true)
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
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
      setError(t('notifications.ackFailed'))
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
    const label = message.relatedEntityType === 'order' ? t('notifications.viewOrder') : t('notifications.viewInvoice')
    return (
      <Button variant="secondary" onClick={() => navigate(to)}>
        {label}
      </Button>
    )
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.portalName'), to: '/klantportaal' }, { label: t('notifications.title') }]} />
      <PageHeader title={t('notifications.title')} subtitle={t('notifications.subtitle')} />

      {error && <p className="placeholder-text" role="alert">{error}</p>}
      {!loaded && <LoadingState message={t('notifications.loading')} />}
      {loaded && messages.length === 0 && <p className="placeholder-text">{t('notifications.empty')}</p>}

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
                  {message.readAt === null && <span className="cpp-notice-dot" aria-label={t('notifications.unread')} />}
                  <PriorityBadge priority={message.priority} />
                  {message.requiresAcknowledgement &&
                    (message.acknowledgedAt === null ? (
                      <Badge tone="warning">{t('notifications.toAcknowledge')}</Badge>
                    ) : (
                      <Badge tone="success">{t('notifications.acknowledged')}</Badge>
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
                  {ackBusy ? t('notifications.acknowledging') : t('notifications.acknowledge')}
                </Button>
              )}
              <Button variant="secondary" onClick={() => setOpenMessage(null)} disabled={ackBusy}>
                {t('common.actions.close')}
              </Button>
            </>
          }
        >
          <p className="cpp-message-meta">
            {t('notifications.publishedOn', { date: formatDateTime(openMessage.publishedAt) })}{' '}
            <PriorityBadge priority={openMessage.priority} />
          </p>
          <p className="cpp-notice-body">{openMessage.body}</p>
          {openMessage.requiresAcknowledgement && openMessage.acknowledgedAt !== null && (
            <p className="cpp-message-meta">
              {t('notifications.acknowledgedOn', { date: formatDateTime(openMessage.acknowledgedAt) })}
            </p>
          )}
        </Modal>
      )}
    </div>
  )
}
