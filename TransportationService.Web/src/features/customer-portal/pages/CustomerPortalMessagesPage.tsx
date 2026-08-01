import { useEffect, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { useLocale } from '../../../i18n/localeContext'
import {
  listPortalMessages,
  markPortalMessagesRead,
  sendPortalMessage,
  type CustomerMessage,
} from '../api/customerPortalApi'
import './customer-portal-pages.css'

/** Portal messages: general thread by default, or a specific order's thread via ?orderId=. */
export function CustomerPortalMessagesPage() {
  const [searchParams] = useSearchParams()
  const orderId = searchParams.get('orderId') ?? undefined
  const { t, formatDateTime } = useLocale()

  const [messages, setMessages] = useState<CustomerMessage[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [body, setBody] = useState('')
  const [sending, setSending] = useState(false)

  function load() {
    listPortalMessages(orderId)
      .then((rows) => {
        setMessages(rows)
        setLoaded(true)
        setError(null)
      })
      .catch(() => {
        setError(t('messages.loadError'))
        setLoaded(true)
      })
  }

  useEffect(() => {
    load()
    void markPortalMessagesRead(orderId ?? null).catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderId])

  async function handleSend(event: FormEvent) {
    event.preventDefault()
    if (!body.trim()) return
    setSending(true)
    try {
      await sendPortalMessage(orderId ?? null, body.trim())
      setBody('')
      load()
    } catch {
      setError(t('messages.sendError'))
    } finally {
      setSending(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.portalName'), to: '/klantportaal' }, { label: t('messages.title') }]} />
      <PageHeader
        title={t('messages.title')}
        subtitle={orderId ? t('messages.orderSubtitle', { orderId }) : t('messages.general')}
      />

      {!loaded && <LoadingState message={t('messages.loading')} />}
      {loaded && (
        <>
          <div className="cpp-thread" role="log" aria-label={t('messages.threadLabel')}>
            {messages.length === 0 && <p className="placeholder-text">{t('messages.empty')}</p>}
            {messages.map((m) => (
              <div key={m.id} className={m.authorIsStaff ? 'cpp-message cpp-message-staff' : 'cpp-message'}>
                <span className="cpp-message-meta">
                  {m.authorIsStaff ? m.authorName : t('messages.you')} · {formatDateTime(m.createdAt)}
                </span>
                <span className="cpp-message-body">{m.body}</span>
              </div>
            ))}
          </div>

          {error && <p className="placeholder-text" role="alert">{error}</p>}

          <form className="cpp-compose" onSubmit={(e) => void handleSend(e)}>
            <textarea
              aria-label={t('messages.newMessageLabel')}
              value={body}
              onChange={(e) => setBody(e.target.value)}
              placeholder={t('messages.placeholder')}
              maxLength={4000}
              disabled={sending}
            />
            <Button type="submit" disabled={sending || !body.trim()}>
              {t('messages.send')}
            </Button>
          </form>
        </>
      )}
    </div>
  )
}
