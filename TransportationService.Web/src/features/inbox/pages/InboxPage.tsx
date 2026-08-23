import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { Tabs, TabPanel } from '../../../components/ui/Tabs'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { EmptyState } from '../../../components/ui/EmptyState'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { getRoles } from '../../roles/api/rolesApi'
import type { Role } from '../../roles/types/role'
import type { LookupOption } from '../../master-data/types'
import {
  acknowledgeMessage,
  cancelInternalMessage,
  getMessageDeliveryStatus,
  listDepartmentOptions,
  listInboxMessages,
  listMessageRecipients,
  listSentMessages,
  markMessageRead,
  sendInternalMessage,
  type EmailDeliveryStatus,
  type InboxMessage,
  type MessageDeliveryStatus,
  type MessagePriority,
  type MessageRecipientOption,
} from '../api/inboxApi'
import { formatDateTime } from '../../../utils/dates'
import './inbox.css'

/** Translation keys per code; render sites resolve them via t(). */
const PRIORITY_KEYS: Record<MessagePriority, string> = {
  Normal: 'inboxPage.priority.Normal',
  High: 'inboxPage.priority.High',
  Urgent: 'inboxPage.priority.Urgent',
}

const EMAIL_STATUS_KEYS: Record<EmailDeliveryStatus, string> = {
  Geen: 'inboxPage.emailStatus.Geen',
  Pending: 'inboxPage.emailStatus.Pending',
  Sent: 'inboxPage.emailStatus.Sent',
  Failed: 'inboxPage.emailStatus.Failed',
  Suppressed: 'inboxPage.emailStatus.Suppressed',
  Duplicate: 'inboxPage.emailStatus.Duplicate',
}

function formatTimestamp(value: string): string {
  return formatDateTime(value)
}

function fromDateTimeLocal(value: string): string | null {
  return value ? new Date(value).toISOString() : null
}

/** Hoog/Dringend get a visible pill; Normal renders nothing to keep the list quiet. */
function PriorityBadge({ priority, t }: { priority: MessagePriority; t: TranslateFn }) {
  if (priority === 'High') return <Badge tone="warning">{t(PRIORITY_KEYS.High)}</Badge>
  if (priority === 'Urgent') return <Badge tone="danger">{t(PRIORITY_KEYS.Urgent)}</Badge>
  return null
}

/** Personal in-app inbox; sending is gated on messages.send (HR/office roles). */
export function InboxPage() {
  const toast = useToast()
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const canSend = hasPermission('messages.send')

  const [tab, setTab] = useState('inbox')
  const [inbox, setInbox] = useState<InboxMessage[] | null>(null)
  const [sent, setSent] = useState<InboxMessage[] | null>(null)
  const [openMessage, setOpenMessage] = useState<InboxMessage | null>(null)
  const [composeOpen, setComposeOpen] = useState(false)
  const [statusMessage, setStatusMessage] = useState<InboxMessage | null>(null)
  const [cancelTarget, setCancelTarget] = useState<InboxMessage | null>(null)
  const [cancelBusy, setCancelBusy] = useState(false)
  const [ackBusy, setAckBusy] = useState(false)

  const reload = useCallback(() => {
    listInboxMessages().then(setInbox).catch(() => setInbox([]))
    if (canSend) {
      listSentMessages().then(setSent).catch(() => setSent([]))
    }
  }, [canSend])

  useEffect(() => {
    reload()
  }, [reload])

  function openAndMarkRead(message: InboxMessage) {
    setOpenMessage(message)
    if (message.readAt === null) {
      void markMessageRead(message.id).then(reload).catch(() => {})
    }
  }

  async function handleAcknowledge(message: InboxMessage) {
    setAckBusy(true)
    try {
      await acknowledgeMessage(message.id)
      const acknowledgedAt = new Date().toISOString()
      setOpenMessage((current) => (current && current.id === message.id ? { ...current, acknowledgedAt } : current))
      toast.showSuccess(t('inboxPage.toasts.acknowledged'))
      reload()
    } catch (err) {
      toast.showError(describeApiError(err, t('inboxPage.toasts.acknowledgeFailed')).message)
    } finally {
      setAckBusy(false)
    }
  }

  async function handleCancel() {
    if (!cancelTarget) return
    setCancelBusy(true)
    try {
      await cancelInternalMessage(cancelTarget.id)
      toast.showSuccess(t('inboxPage.toasts.withdrawn'))
      setCancelTarget(null)
      reload()
    } catch (err) {
      toast.showError(describeApiError(err, t('inboxPage.toasts.withdrawFailed')).message)
    } finally {
      setCancelBusy(false)
    }
  }

  /** Gelezen vs bevestigd: "nieuw" tracks readAt, the acknowledgement pill tracks acknowledgedAt. */
  function renderAckBadge(message: InboxMessage) {
    if (!message.requiresAcknowledgement) return null
    return message.acknowledgedAt === null ? (
      <Badge tone="warning">{t('inboxPage.list.ackPending')}</Badge>
    ) : (
      <Badge tone="success">{t('inboxPage.list.ackDone')}</Badge>
    )
  }

  function renderList(messages: InboxMessage[] | null, kind: 'inbox' | 'sent') {
    if (messages === null) return <LoadingState message={t('inboxPage.list.loading')} />
    if (messages.length === 0) {
      return <EmptyState message={kind === 'inbox' ? t('inboxPage.list.emptyInbox') : t('inboxPage.list.emptySent')} />
    }

    return (
      <ul className="inbox-list">
        {messages.map((message) => (
          <li key={message.id} className={kind === 'sent' && message.cancelledAt ? 'inbox-row inbox-cancelled' : 'inbox-row'}>
            <button
              type="button"
              className={`inbox-item ${kind === 'inbox' && message.readAt === null ? 'inbox-unread' : ''}`}
              onClick={() => openAndMarkRead(message)}
            >
              <span className="inbox-subject">
                {kind === 'inbox' && message.readAt === null && <Badge tone="info">{t('inboxPage.list.newBadge')}</Badge>}{' '}
                <PriorityBadge priority={message.priority} t={t} /> {kind === 'inbox' && renderAckBadge(message)}{' '}
                {message.subject}
              </span>
              <span className="inbox-meta">
                {kind === 'inbox' ? message.senderName : t('inboxPage.list.recipients', { count: message.recipientCount })} ·{' '}
                {formatTimestamp(message.sentAt)}
                {kind === 'sent' && message.cancelledAt && ` · ${t('inboxPage.list.cancelledOn', { dateTime: formatTimestamp(message.cancelledAt) })}`}
              </span>
            </button>
            {kind === 'sent' && (
              <span className="inbox-actions">
                <Button variant="secondary" onClick={() => setStatusMessage(message)}>
                  {t('inboxPage.list.deliveryStatus')}
                </Button>
                {message.cancelledAt === null && (
                  <Button variant="secondary" onClick={() => setCancelTarget(message)}>
                    {t('inboxPage.list.withdraw')}
                  </Button>
                )}
              </span>
            )}
          </li>
        ))}
      </ul>
    )
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('inboxPage.title') }]} />
      <PageHeader
        title={t('inboxPage.title')}
        subtitle={t('inboxPage.subtitle')}
        action={canSend && <Button onClick={() => setComposeOpen(true)}>{t('inboxPage.newMessage')}</Button>}
      />

      {canSend ? (
        <>
          <Tabs
            tabs={[
              { id: 'inbox', label: t('inboxPage.tabs.inbox'), badge: inbox?.filter((m) => m.readAt === null).length || undefined },
              { id: 'sent', label: t('inboxPage.tabs.sent') },
            ]}
            activeId={tab}
            onChange={setTab}
          />
          {tab === 'inbox' && <TabPanel tabId="inbox">{renderList(inbox, 'inbox')}</TabPanel>}
          {tab === 'sent' && <TabPanel tabId="sent">{renderList(sent, 'sent')}</TabPanel>}
        </>
      ) : (
        renderList(inbox, 'inbox')
      )}

      {openMessage && (
        <Modal
          title={openMessage.subject}
          onClose={() => setOpenMessage(null)}
          busy={ackBusy}
          footer={
            <>
              {openMessage.requiresAcknowledgement && openMessage.acknowledgedAt === null && (
                <Button onClick={() => void handleAcknowledge(openMessage)} disabled={ackBusy}>
                  {ackBusy ? t('inboxPage.detail.acknowledging') : t('inboxPage.detail.acknowledge')}
                </Button>
              )}
              <Button variant="secondary" onClick={() => setOpenMessage(null)} disabled={ackBusy}>
                {t('ui.actions.close')}
              </Button>
            </>
          }
        >
          <p className="inbox-meta">
            {openMessage.senderName} · {formatTimestamp(openMessage.sentAt)} <PriorityBadge priority={openMessage.priority} t={t} />
          </p>
          <p className="inbox-body">{openMessage.body}</p>
          {openMessage.requiresAcknowledgement &&
            (openMessage.acknowledgedAt !== null ? (
              <p className="inbox-ack-note">{t('inboxPage.detail.acknowledgedAt', { dateTime: formatTimestamp(openMessage.acknowledgedAt) })}</p>
            ) : (
              <p className="inbox-ack-note">{t('inboxPage.detail.acknowledgementRequired')}</p>
            ))}
        </Modal>
      )}

      {statusMessage && <DeliveryStatusDialog message={statusMessage} onClose={() => setStatusMessage(null)} />}

      {cancelTarget && (
        <ConfirmDialog
          title={t('inboxPage.cancelDialog.title')}
          message={t('inboxPage.cancelDialog.message', { subject: cancelTarget.subject })}
          confirmLabel={t('inboxPage.cancelDialog.confirm')}
          destructive
          busy={cancelBusy}
          onConfirm={() => void handleCancel()}
          onCancel={() => setCancelTarget(null)}
        />
      )}

      {composeOpen && (
        <ComposeDialog
          canBulk={hasPermission('messages.send_bulk')}
          onClose={(sentOk) => {
            setComposeOpen(false)
            if (sentOk) {
              toast.showSuccess(t('inboxPage.toasts.sent'))
              reload()
            }
          }}
        />
      )}
    </div>
  )
}

/** Per-recipient read/acknowledge/e-mail state of a sent message. */
function DeliveryStatusDialog({ message, onClose }: { message: InboxMessage; onClose: () => void }) {
  const { t } = useLocale()
  const [status, setStatus] = useState<MessageDeliveryStatus | null>(null)
  const [loadError, setLoadError] = useState(false)

  useEffect(() => {
    getMessageDeliveryStatus(message.id)
      .then(setStatus)
      .catch(() => setLoadError(true))
  }, [message.id])

  return (
    <Modal title={t('inboxPage.delivery.title')} onClose={onClose} footer={<Button onClick={onClose}>{t('ui.actions.close')}</Button>}>
      {loadError && <p className="placeholder-text" role="alert">{t('inboxPage.delivery.loadFailed')}</p>}
      {!loadError && status === null && <LoadingState message={t('inboxPage.delivery.loading')} />}
      {status && (
        <>
          <p className="inbox-meta">
            {status.subject} · {t('inboxPage.delivery.sentOn', { dateTime: formatTimestamp(status.sentAt) })}
            {status.cancelledAt && ` · ${t('inboxPage.list.cancelledOn', { dateTime: formatTimestamp(status.cancelledAt) })}`}
          </p>
          <table className="inbox-status-table">
            <thead>
              <tr>
                <th>{t('inboxPage.delivery.columnName')}</th>
                <th>{t('inboxPage.delivery.columnRead')}</th>
                {status.requiresAcknowledgement && <th>{t('inboxPage.delivery.columnAcknowledged')}</th>}
                <th>{t('inboxPage.delivery.columnEmail')}</th>
              </tr>
            </thead>
            <tbody>
              {status.recipients.map((recipient) => (
                <tr key={recipient.userId}>
                  <td>{recipient.name}</td>
                  <td>{recipient.readAt ? formatTimestamp(recipient.readAt) : '—'}</td>
                  {status.requiresAcknowledgement && (
                    <td>{recipient.acknowledgedAt ? formatTimestamp(recipient.acknowledgedAt) : '—'}</td>
                  )}
                  <td>
                    {EMAIL_STATUS_KEYS[recipient.emailStatus] ? t(EMAIL_STATUS_KEYS[recipient.emailStatus]) : recipient.emailStatus}
                    {recipient.emailFailureReason && (
                      <span className="inbox-email-failure"> — {recipient.emailFailureReason}</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </Modal>
  )
}

type ComposeTarget = 'users' | 'role' | 'department' | 'all'

/** Translation keys per compose target; resolved via t() at render time. */
const TARGET_KEYS: Record<ComposeTarget, string> = {
  users: 'inboxPage.compose.targetUsers',
  role: 'inboxPage.compose.targetRole',
  department: 'inboxPage.compose.targetDepartment',
  all: 'inboxPage.compose.targetAll',
}

function ComposeDialog({ canBulk, onClose }: { canBulk: boolean; onClose: (sent: boolean) => void }) {
  const { t } = useLocale()
  const [recipients, setRecipients] = useState<MessageRecipientOption[]>([])
  const [roles, setRoles] = useState<Role[]>([])
  const [departments, setDepartments] = useState<LookupOption[]>([])
  const [target, setTarget] = useState<ComposeTarget>('users')
  const [selectedUsers, setSelectedUsers] = useState<Set<string>>(new Set())
  const [roleId, setRoleId] = useState('')
  const [departmentId, setDepartmentId] = useState('')
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')
  const [priority, setPriority] = useState<MessagePriority>('Normal')
  const [requiresAcknowledgement, setRequiresAcknowledgement] = useState(false)
  const [visibleFrom, setVisibleFrom] = useState('')
  const [expiresAt, setExpiresAt] = useState('')
  const [sendEmail, setSendEmail] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    listMessageRecipients()
      .then(setRecipients)
      .catch(() => setError(t('inboxPage.compose.recipientsLoadFailed')))
    if (canBulk) {
      getRoles()
        .then((data) => setRoles(data.filter((role) => role.isActive)))
        .catch(() => {})
      listDepartmentOptions()
        .then(setDepartments)
        .catch(() => {})
    }
    // t is stable enough for this one-shot fetch; the error text renders in the active language.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canBulk])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    setFieldErrors({})
    try {
      await sendInternalMessage({
        subject: subject.trim(),
        body: body.trim(),
        userIds: target === 'users' ? [...selectedUsers] : [],
        roleId: target === 'role' ? roleId || null : null,
        departmentId: target === 'department' ? departmentId || null : null,
        allEmployees: target === 'all',
        priority,
        requiresAcknowledgement,
        visibleFrom: fromDateTimeLocal(visibleFrom),
        expiresAt: fromDateTimeLocal(expiresAt),
        sendEmail,
      })
      onClose(true)
    } catch (err) {
      const described = describeApiError(err, t('inboxPage.compose.sendFailed'))
      setError(described.message)
      setFieldErrors(described.fieldErrors)
      setSaving(false)
    }
  }

  return (
    <Modal
      title={t('inboxPage.compose.title')}
      onClose={() => onClose(false)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(false)} disabled={saving}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="compose-form" disabled={saving}>
            {saving ? t('inboxPage.compose.sending') : t('inboxPage.compose.send')}
          </Button>
        </>
      }
    >
      <form id="compose-form" onSubmit={handleSubmit} noValidate>
        <ValidationSummary
          message={error}
          fieldErrors={fieldErrors}
          fieldLabels={{ subject: t('inboxPage.compose.subjectField'), body: t('inboxPage.compose.bodyField') }}
        />

        {canBulk && (
          <FormField label={t('inboxPage.compose.targetField')} htmlFor="cm-target">
            <select
              id="cm-target"
              value={target}
              onChange={(e) => setTarget(e.target.value as ComposeTarget)}
              disabled={saving}
            >
              {(Object.keys(TARGET_KEYS) as ComposeTarget[]).map((value) => (
                <option key={value} value={value}>
                  {t(TARGET_KEYS[value])}
                </option>
              ))}
            </select>
          </FormField>
        )}

        {canBulk && target === 'role' && (
          <FormField label={t('inboxPage.compose.roleField')} htmlFor="cm-role" hint={t('inboxPage.compose.roleHint')}>
            <select id="cm-role" value={roleId} onChange={(e) => setRoleId(e.target.value)} disabled={saving}>
              <option value="">{t('inboxPage.compose.chooseRole')}</option>
              {roles.map((role) => (
                <option key={role.id} value={role.id}>
                  {role.name}
                </option>
              ))}
            </select>
          </FormField>
        )}

        {canBulk && target === 'department' && (
          <FormField label={t('inboxPage.compose.departmentField')} htmlFor="cm-department" hint={t('inboxPage.compose.departmentHint')}>
            <select
              id="cm-department"
              value={departmentId}
              onChange={(e) => setDepartmentId(e.target.value)}
              disabled={saving}
            >
              <option value="">{t('inboxPage.compose.chooseDepartment')}</option>
              {departments.map((department) => (
                <option key={department.id} value={department.id}>
                  {department.name}
                </option>
              ))}
            </select>
          </FormField>
        )}

        {canBulk && target === 'all' && (
          <p className="inbox-target-note">{t('inboxPage.compose.allNote')}</p>
        )}

        {target === 'users' && (
          <FormField label={t('inboxPage.compose.toUsersField')} htmlFor="cm-users">
            <div className="inbox-recipients">
              {recipients.map((recipient) => (
                <label key={recipient.userId} className="customer-form-checkbox">
                  <input
                    type="checkbox"
                    checked={selectedUsers.has(recipient.userId)}
                    onChange={() =>
                      setSelectedUsers((current) => {
                        const next = new Set(current)
                        if (next.has(recipient.userId)) next.delete(recipient.userId)
                        else next.add(recipient.userId)
                        return next
                      })
                    }
                    disabled={saving}
                  />
                  {recipient.name}
                </label>
              ))}
            </div>
          </FormField>
        )}

        <FormField label={t('inboxPage.compose.subjectField')} htmlFor="cm-subject" required error={getFieldError(fieldErrors, 'subject')}>
          <input id="cm-subject" value={subject} onChange={(e) => setSubject(e.target.value)} maxLength={200} disabled={saving} />
        </FormField>
        <FormField label={t('inboxPage.compose.bodyField')} htmlFor="cm-body" required error={getFieldError(fieldErrors, 'body')}>
          <textarea id="cm-body" rows={5} value={body} onChange={(e) => setBody(e.target.value)} maxLength={8000} disabled={saving} />
        </FormField>

        <FormField label={t('inboxPage.compose.priorityField')} htmlFor="cm-priority">
          <select
            id="cm-priority"
            value={priority}
            onChange={(e) => setPriority(e.target.value as MessagePriority)}
            disabled={saving}
          >
            <option value="Normal">{t(PRIORITY_KEYS.Normal)}</option>
            <option value="High">{t(PRIORITY_KEYS.High)}</option>
            <option value="Urgent">{t(PRIORITY_KEYS.Urgent)}</option>
          </select>
        </FormField>

        <FormField label={t('inboxPage.compose.optionsField')} htmlFor="cm-ack">
          <label className="customer-form-checkbox">
            <input
              id="cm-ack"
              type="checkbox"
              checked={requiresAcknowledgement}
              onChange={(e) => setRequiresAcknowledgement(e.target.checked)}
              disabled={saving}
            />
            {t('inboxPage.compose.requireAcknowledgement')}
          </label>
          <label className="customer-form-checkbox">
            <input
              id="cm-email"
              type="checkbox"
              checked={sendEmail}
              onChange={(e) => setSendEmail(e.target.checked)}
              disabled={saving}
            />
            {t('inboxPage.compose.alsoEmail')}
          </label>
        </FormField>

        <FormField label={t('inboxPage.compose.visibleFromField')} htmlFor="cm-visible-from" hint={t('inboxPage.compose.visibleFromHint')}>
          <input
            id="cm-visible-from"
            type="datetime-local"
            value={visibleFrom}
            onChange={(e) => setVisibleFrom(e.target.value)}
            disabled={saving}
          />
        </FormField>
        <FormField label={t('inboxPage.compose.expiresAtField')} htmlFor="cm-expires-at" hint={t('inboxPage.compose.expiresAtHint')}>
          <input
            id="cm-expires-at"
            type="datetime-local"
            value={expiresAt}
            onChange={(e) => setExpiresAt(e.target.value)}
            disabled={saving}
          />
        </FormField>
      </form>
    </Modal>
  )
}
