import { useEffect, useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import {
  listCustomerMessages,
  markCustomerMessagesRead,
  sendCustomerMessage,
  type CustomerMessage,
} from '../api/customerMessagesApi'
import { formatDateTime } from '../../../utils/dates'
import './customerMessagesPanel.css'

interface CustomerMessagesPanelProps {
  customerId: string
  /** When set, scopes the thread to one order (used from the order-detail portal panel). */
  orderId?: string
  /** Fired once the thread has been marked read for the current user (e.g. to clear an unread badge). */
  onMarkedRead?: () => void
}

/** Staff/customer messaging thread — customer detail "Berichten" tab and the order-detail panel. */
export function CustomerMessagesPanel({ customerId, orderId, onMarkedRead }: CustomerMessagesPanelProps) {
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const canSend = hasPermission('customer_messages.send')
  const [messages, setMessages] = useState<CustomerMessage[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [body, setBody] = useState('')
  const [sending, setSending] = useState(false)

  function load() {
    listCustomerMessages(customerId, orderId)
      .then((rows) => {
        setMessages(rows)
        setLoaded(true)
        setError(null)
      })
      .catch(() => {
        setError('customers.messages.loadFailed')
        setLoaded(true)
      })
  }

  useEffect(() => {
    load()
    void markCustomerMessagesRead(customerId, orderId ?? null)
      .then(() => onMarkedRead?.())
      .catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [customerId, orderId])

  async function handleSend(event: FormEvent) {
    event.preventDefault()
    if (!body.trim()) return
    setSending(true)
    try {
      await sendCustomerMessage(customerId, orderId ?? null, body.trim())
      setBody('')
      load()
    } catch {
      setError('customers.messages.sendFailed')
    } finally {
      setSending(false)
    }
  }

  if (!loaded) return <LoadingState message={t('customers.messages.loading')} />

  return (
    <div className="cmp-panel">
      <div className="cmp-thread" role="log" aria-label={t('customers.messages.threadAria')}>
        {messages.length === 0 && <p className="placeholder-text">{t('customers.messages.empty')}</p>}
        {messages.map((m) => (
          <div key={m.id} className={m.authorIsStaff ? 'cmp-message cmp-message-staff' : 'cmp-message'}>
            <span className="cmp-message-meta">
              {m.authorName} · {formatDateTime(m.createdAt)}
            </span>
            <span className="cmp-message-body">{m.body}</span>
          </div>
        ))}
      </div>

      {error && <p className="placeholder-text" role="alert">{t(error)}</p>}

      {canSend && (
        <form className="cmp-compose" onSubmit={(e) => void handleSend(e)}>
          <textarea
            aria-label={t('customers.messages.newMessageAria')}
            value={body}
            onChange={(e) => setBody(e.target.value)}
            placeholder={t('customers.messages.composePlaceholder')}
            maxLength={4000}
            disabled={sending}
          />
          <Button type="submit" disabled={sending || !body.trim()}>
            {t('customers.messages.send')}
          </Button>
        </form>
      )}
    </div>
  )
}
