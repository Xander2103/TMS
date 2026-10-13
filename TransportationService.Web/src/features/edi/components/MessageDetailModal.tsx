import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { getMessage, replayMessage, STATUS_LABELS, STATUS_TONE, type EdiMessageDetail } from '../api/ediApi'
import { formatDateTime } from '../../../utils/dates'

interface MessageDetailModalProps {
  id: string
  canRetry: boolean
  onClose: () => void
  /** Fired after a successful replay so the caller can refresh its list and close the modal. */
  onReplayed: () => void
}

const RESULT_ROUTES: Record<string, string> = {
  TransportOrder: '/transport-orders',
}

/** Structured EDI message detail: header fields, payload, result link and replay — not a raw JSON dump. */
export function MessageDetailModal({ id, canRetry, onClose, onReplayed }: MessageDetailModalProps) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [message, setMessage] = useState<EdiMessageDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    getMessage(id)
      .then((data) => {
        if (mounted) setMessage(data)
      })
      .catch(() => {
        if (mounted) setError(t('edi.detail.openFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id])

  async function replay() {
    setBusy(true)
    try {
      await replayMessage(id)
      showSuccess(t('edi.messages.replayed'))
      onReplayed()
    } catch (err) {
      showError(localizeApiError(t, err, t('edi.messages.replayFailed')))
    } finally {
      setBusy(false)
    }
  }

  const canShowReplay = canRetry && message && (message.status === 'Failed' || message.status === 'DeadLettered')
  const resultRoute = message?.resultEntityType ? RESULT_ROUTES[message.resultEntityType] : undefined

  return (
    <Modal
      title={t('edi.detail.title')}
      onClose={onClose}
      busy={busy}
      footer={
        canShowReplay ? (
          <>
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              {t('edi.detail.close')}
            </Button>
            <Button onClick={() => void replay()} disabled={busy}>
              {busy ? t('edi.detail.busy') : t('edi.detail.replay')}
            </Button>
          </>
        ) : (
          <Button variant="secondary" onClick={onClose}>
            {t('edi.detail.close')}
          </Button>
        )
      }
    >
      {error && <p className="placeholder-text">{error}</p>}
      {!error && !message && <p className="placeholder-text">{t('edi.detail.loading')}</p>}
      {message && (
        <div className="edi-detail">
          <dl className="edi-detail-grid">
            <dt>{t('edi.detail.partner')}</dt>
            <dd>
              <code>{message.partnerCode}</code>
            </dd>
            <dt>{t('edi.detail.direction')}</dt>
            <dd>{message.direction === 'Inbound' ? t('edi.detail.inbound') : t('edi.detail.outbound')}</dd>
            <dt>{t('edi.detail.status')}</dt>
            <dd>
              <Badge tone={STATUS_TONE[message.status]}>{t(STATUS_LABELS[message.status])}</Badge>
              {message.mappingIssue && <Badge tone="warning">{t('edi.messages.mappingBadge')}</Badge>}
            </dd>
            <dt>{t('edi.detail.attempt')}</dt>
            <dd>{message.attemptCount} / 3</dd>
            <dt>{t('edi.detail.date')}</dt>
            <dd>{formatDateTime(message.createdAt)}</dd>
            <dt>{t('edi.detail.externalReference')}</dt>
            <dd>{message.externalReference ?? '—'}</dd>
            <dt>{t('edi.detail.error')}</dt>
            <dd>{message.errorDetail ?? '—'}</dd>
          </dl>

          {message.validationErrors && message.validationErrors.length > 0 && (
            <div className="edi-detail-errors">
              <h4>{t('edi.detail.validationErrors')}</h4>
              <ul>
                {message.validationErrors.map((e, i) => (
                  <li key={i}>{e}</li>
                ))}
              </ul>
            </div>
          )}

          {resultRoute && message.resultEntityId && (
            <p>
              <Link to={`${resultRoute}/${message.resultEntityId}`}>{t('edi.detail.viewOrder')}</Link>
            </p>
          )}

          <h4>{t('edi.detail.payload')}</h4>
          <pre className="edi-payload">{message.payloadJson}</pre>
        </div>
      )}
    </Modal>
  )
}
