import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { Pagination } from '../../../components/ui/Pagination'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { apiClient, ApiError } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import { formatDateTime } from '../../../utils/dates'
import './messaging.css'

type MessageChannel = 'Email' | 'Sms'
type OutboxStatus = 'Pending' | 'Sent' | 'Failed' | 'Suppressed'

interface OutboxRow {
  id: string
  channel: MessageChannel
  kind: string
  recipientAddress: string
  recipientName: string | null
  subject: string | null
  status: OutboxStatus
  attemptCount: number
  nextAttemptAt: string | null
  sentAt: string | null
  failureReason: string | null
  createdAt: string
  isFallback: boolean
}

interface Template {
  id: string
  kind: string
  channel: MessageChannel
  language: string
  subject: string | null
  body: string
  isActive: boolean
}

const STATUS_TONE: Record<OutboxStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Pending: 'info',
  Sent: 'success',
  Failed: 'danger',
  Suppressed: 'neutral',
}

/** Translation keys per outbox status; render via t(STATUS_LABELS[status]). */
const STATUS_LABELS: Record<OutboxStatus, string> = {
  Pending: 'messaging.status.Pending',
  Sent: 'messaging.status.Sent',
  Failed: 'messaging.status.Failed',
  Suppressed: 'messaging.status.Suppressed',
}

const PAGE_SIZE = 25

/** Outbox monitor + template editor for the provider-neutral messaging layer. */
export function MessagingPage() {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const canTemplates = hasPermission('message_templates.manage')

  const [statusFilter, setStatusFilter] = useState<OutboxStatus | ''>('')
  const [page, setPage] = useState(1)
  const [reloadToken, setReloadToken] = useState(0)
  const [outbox, setOutbox] = useState<PagedResult<OutboxRow> | null>(null)

  const [templates, setTemplates] = useState<Template[] | null>(null)
  const [kinds, setKinds] = useState<string[]>([])
  const [editorOpen, setEditorOpen] = useState(false)
  const [tplKind, setTplKind] = useState('pod_available')
  const [tplChannel, setTplChannel] = useState<MessageChannel>('Email')
  const [tplLanguage, setTplLanguage] = useState('nl')
  const [tplSubject, setTplSubject] = useState('')
  const [tplBody, setTplBody] = useState('')
  const [preview, setPreview] = useState<{ subject: string | null; body: string } | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    const query = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
    if (statusFilter) query.set('status', statusFilter)
    apiClient
      .getJson<PagedResult<OutboxRow>>(`/api/messaging/outbox?${query.toString()}`)
      .then((data) => {
        if (mounted) setOutbox(data)
      })
      .catch(() => {
        if (mounted) showError(t('messaging.page.loadFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, statusFilter, reloadToken])

  useEffect(() => {
    if (!canTemplates) return
    let mounted = true
    apiClient.getJson<Template[]>('/api/message-templates').then((data) => {
      if (mounted) setTemplates(data)
    }).catch(() => {})
    apiClient.getJson<string[]>('/api/message-templates/kinds').then((data) => {
      if (mounted) setKinds(data)
    }).catch(() => {})
    return () => {
      mounted = false
    }
  }, [canTemplates, reloadToken])

  async function retry(id: string) {
    try {
      await apiClient.postJson(`/api/messaging/outbox/${id}/retry`, {})
      showSuccess(t('messaging.outbox.retried'))
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('messaging.outbox.retryFailed'))
    }
  }

  async function saveTemplate(event: FormEvent) {
    event.preventDefault()
    if (!tplBody.trim()) {
      showError(t('messaging.templates.bodyRequired'))
      return
    }
    setBusy(true)
    try {
      await apiClient.postJson('/api/message-templates', {
        kind: tplKind,
        channel: tplChannel,
        language: tplLanguage,
        subject: tplSubject.trim() || null,
        body: tplBody,
        isActive: true,
      })
      showSuccess(t('messaging.templates.saved'))
      setEditorOpen(false)
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('messaging.templates.saveFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function loadPreview() {
    setBusy(true)
    try {
      setPreview(
        await apiClient.postJson<{ subject: string | null; body: string }, object>('/api/message-templates/preview', {
          kind: tplKind,
          channel: tplChannel,
          language: tplLanguage,
          tokens: null,
        }),
      )
    } catch {
      showError(t('messaging.templates.previewFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function removeTemplate(id: string) {
    try {
      await apiClient.deleteRequest(`/api/message-templates/${id}`)
      showSuccess(t('messaging.templates.deleted'))
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('messaging.templates.deleteFailed'))
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('messaging.page.title') }]} />
      <PageHeader
        title={t('messaging.page.title')}
        subtitle={t('messaging.page.subtitle')}
        action={
          canTemplates ? (
            <Button
              variant="secondary"
              onClick={() => {
                setPreview(null)
                setEditorOpen(true)
              }}
            >
              {t('messaging.page.editTemplate')}
            </Button>
          ) : undefined
        }
      />

      <div className="msg-filters">
        <select
          value={statusFilter}
          onChange={(e) => { setStatusFilter(e.target.value as OutboxStatus | ''); setPage(1) }}
          aria-label={t('messaging.page.statusAria')}
        >
          <option value="">{t('messaging.page.allStatuses')}</option>
          {(Object.keys(STATUS_LABELS) as OutboxStatus[]).map((status) => (
            <option key={status} value={status}>
              {t(STATUS_LABELS[status])}
            </option>
          ))}
        </select>
      </div>

      <table className="to-stops-table">
        <thead>
          <tr>
            <th>{t('messaging.columns.created')}</th>
            <th>{t('messaging.columns.channel')}</th>
            <th>{t('messaging.columns.kind')}</th>
            <th>{t('messaging.columns.recipient')}</th>
            <th>{t('messaging.columns.status')}</th>
            <th>{t('messaging.columns.attempts')}</th>
            <th>{t('messaging.columns.detail')}</th>
            <th aria-label={t('messaging.columns.actionsAria')} />
          </tr>
        </thead>
        <tbody>
          {(outbox?.items ?? []).map((row) => (
            <tr key={row.id}>
              <td>{formatDateTime(row.createdAt)}</td>
              <td>
                {row.channel === 'Email' ? '✉' : '📱'} {row.channel}
                {row.isFallback && <Badge tone="warning">{t('messaging.outbox.fallback')}</Badge>}
              </td>
              <td>
                <code>{row.kind}</code>
              </td>
              <td title={row.subject ?? undefined}>
                {row.recipientName ? `${row.recipientName} · ` : ''}
                {row.recipientAddress}
              </td>
              <td>
                <Badge tone={STATUS_TONE[row.status]}>{t(STATUS_LABELS[row.status])}</Badge>
              </td>
              <td>{row.attemptCount}</td>
              <td className="msg-failure" title={row.failureReason ?? undefined}>
                {row.status === 'Sent' && row.sentAt
                  ? t('messaging.outbox.sentAtTime', { time: row.sentAt.slice(11, 16) })
                  : row.failureReason ?? '—'}
              </td>
              <td>
                {(row.status === 'Failed' || row.status === 'Suppressed') && (
                  <Button variant="ghost" onClick={() => void retry(row.id)}>
                    {t('messaging.outbox.retry')}
                  </Button>
                )}
              </td>
            </tr>
          ))}
          {outbox !== null && outbox.items.length === 0 && (
            <tr>
              <td colSpan={8}>{t('messaging.page.empty')}</td>
            </tr>
          )}
        </tbody>
      </table>
      {outbox && <Pagination page={page} pageSize={PAGE_SIZE} totalCount={outbox.totalCount} onPageChange={setPage} />}

      {canTemplates && templates && templates.length > 0 && (
        <section className="to-section">
          <h2>{t('messaging.templates.heading')}</h2>
          <ul className="msg-templates">
            {templates.map((template) => (
              <li key={template.id}>
                <code>{template.kind}</code> · {template.channel} · {template.language}
                {!template.isActive && <Badge tone="neutral">{t('messaging.templates.inactive')}</Badge>}
                <button
                  type="button"
                  className="msg-template-edit"
                  onClick={() => {
                    setTplKind(template.kind)
                    setTplChannel(template.channel)
                    setTplLanguage(template.language)
                    setTplSubject(template.subject ?? '')
                    setTplBody(template.body)
                    setPreview(null)
                    setEditorOpen(true)
                  }}
                >
                  {t('messaging.templates.edit')}
                </button>
                <button type="button" className="msg-template-delete" onClick={() => void removeTemplate(template.id)}>
                  {t('messaging.templates.delete')}
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      {editorOpen && (
        <Modal
          title={t('messaging.templates.modalTitle')}
          onClose={() => setEditorOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => void loadPreview()} disabled={busy}>
                {t('messaging.templates.preview')}
              </Button>
              <Button type="submit" form="msg-template-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="msg-template-form" className="msg-form" onSubmit={saveTemplate} noValidate>
            <div className="msg-form-row">
              <FormField label={t('messaging.templates.form.kind')} htmlFor="msg-kind">
                <select id="msg-kind" value={tplKind} onChange={(e) => setTplKind(e.target.value)} disabled={busy}>
                  {kinds.map((kind) => (
                    <option key={kind} value={kind}>
                      {kind}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label={t('messaging.templates.form.channel')} htmlFor="msg-channel">
                <select id="msg-channel" value={tplChannel} onChange={(e) => setTplChannel(e.target.value as MessageChannel)} disabled={busy}>
                  <option value="Email">{t('messaging.templates.form.channelEmail')}</option>
                  <option value="Sms">{t('messaging.templates.form.channelSms')}</option>
                </select>
              </FormField>
              <FormField label={t('messaging.templates.form.language')} htmlFor="msg-language">
                <input id="msg-language" value={tplLanguage} onChange={(e) => setTplLanguage(e.target.value)} disabled={busy} maxLength={5} />
              </FormField>
            </div>
            {tplChannel === 'Email' && (
              <FormField label={t('messaging.templates.form.subject')} htmlFor="msg-subject" hint={t('messaging.templates.form.subjectHint')}>
                <input id="msg-subject" value={tplSubject} onChange={(e) => setTplSubject(e.target.value)} disabled={busy} maxLength={300} />
              </FormField>
            )}
            <FormField label={t('messaging.templates.form.body')} htmlFor="msg-body" required>
              <textarea id="msg-body" rows={6} value={tplBody} onChange={(e) => setTplBody(e.target.value)} disabled={busy} maxLength={8000} />
            </FormField>
            {preview && (
              <div className="msg-preview">
                <h3>{t('messaging.templates.preview')}</h3>
                {preview.subject && <p className="msg-preview-subject">{preview.subject}</p>}
                <pre>{preview.body}</pre>
              </div>
            )}
          </form>
        </Modal>
      )}
    </div>
  )
}
