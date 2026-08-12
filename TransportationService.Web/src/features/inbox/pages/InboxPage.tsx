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

const PRIORITY_LABELS: Record<MessagePriority, string> = { Normal: 'Normaal', High: 'Hoog', Urgent: 'Dringend' }

const EMAIL_STATUS_LABELS: Record<EmailDeliveryStatus, string> = {
  Geen: 'Geen',
  Pending: 'In wachtrij',
  Sent: 'Verzonden',
  Failed: 'Mislukt',
  Suppressed: 'Onderdrukt',
  Duplicate: 'Duplicaat',
}

function formatTimestamp(value: string): string {
  return formatDateTime(value)
}

function fromDateTimeLocal(value: string): string | null {
  return value ? new Date(value).toISOString() : null
}

/** Hoog/Dringend get a visible pill; Normal renders nothing to keep the list quiet. */
function PriorityBadge({ priority }: { priority: MessagePriority }) {
  if (priority === 'High') return <Badge tone="warning">{PRIORITY_LABELS.High}</Badge>
  if (priority === 'Urgent') return <Badge tone="danger">{PRIORITY_LABELS.Urgent}</Badge>
  return null
}

/** Personal in-app inbox; sending is gated on messages.send (HR/office roles). */
export function InboxPage() {
  const toast = useToast()
  const { hasPermission } = useAuth()
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
      toast.showSuccess('Bericht bevestigd.')
      reload()
    } catch (err) {
      toast.showError(describeApiError(err, 'Het bericht kon niet worden bevestigd.').message)
    } finally {
      setAckBusy(false)
    }
  }

  async function handleCancel() {
    if (!cancelTarget) return
    setCancelBusy(true)
    try {
      await cancelInternalMessage(cancelTarget.id)
      toast.showSuccess('Bericht ingetrokken.')
      setCancelTarget(null)
      reload()
    } catch (err) {
      toast.showError(describeApiError(err, 'Het bericht kon niet worden ingetrokken.').message)
    } finally {
      setCancelBusy(false)
    }
  }

  /** Gelezen vs bevestigd: "nieuw" tracks readAt, the acknowledgement pill tracks acknowledgedAt. */
  function renderAckBadge(message: InboxMessage) {
    if (!message.requiresAcknowledgement) return null
    return message.acknowledgedAt === null ? (
      <Badge tone="warning">te bevestigen</Badge>
    ) : (
      <Badge tone="success">bevestigd</Badge>
    )
  }

  function renderList(messages: InboxMessage[] | null, kind: 'inbox' | 'sent') {
    if (messages === null) return <LoadingState message="Berichten laden..." />
    if (messages.length === 0) {
      return <EmptyState message={kind === 'inbox' ? 'Geen berichten in je inbox.' : 'Nog geen verzonden berichten.'} />
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
                {kind === 'inbox' && message.readAt === null && <Badge tone="info">nieuw</Badge>}{' '}
                <PriorityBadge priority={message.priority} /> {kind === 'inbox' && renderAckBadge(message)}{' '}
                {message.subject}
              </span>
              <span className="inbox-meta">
                {kind === 'inbox' ? message.senderName : `${message.recipientCount} ontvanger(s)`} ·{' '}
                {formatTimestamp(message.sentAt)}
                {kind === 'sent' && message.cancelledAt && ` · Ingetrokken op ${formatTimestamp(message.cancelledAt)}`}
              </span>
            </button>
            {kind === 'sent' && (
              <span className="inbox-actions">
                <Button variant="secondary" onClick={() => setStatusMessage(message)}>
                  Bezorgstatus
                </Button>
                {message.cancelledAt === null && (
                  <Button variant="secondary" onClick={() => setCancelTarget(message)}>
                    Intrekken
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
      <Breadcrumbs items={[{ label: 'Berichten' }]} />
      <PageHeader
        title="Berichten"
        subtitle="Interne berichten binnen het bedrijf."
        action={canSend && <Button onClick={() => setComposeOpen(true)}>Nieuw bericht</Button>}
      />

      {canSend ? (
        <>
          <Tabs
            tabs={[
              { id: 'inbox', label: 'Ontvangen', badge: inbox?.filter((m) => m.readAt === null).length || undefined },
              { id: 'sent', label: 'Verzonden' },
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
                  {ackBusy ? 'Bevestigen…' : 'Bevestigen'}
                </Button>
              )}
              <Button variant="secondary" onClick={() => setOpenMessage(null)} disabled={ackBusy}>
                Sluiten
              </Button>
            </>
          }
        >
          <p className="inbox-meta">
            {openMessage.senderName} · {formatTimestamp(openMessage.sentAt)} <PriorityBadge priority={openMessage.priority} />
          </p>
          <p className="inbox-body">{openMessage.body}</p>
          {openMessage.requiresAcknowledgement &&
            (openMessage.acknowledgedAt !== null ? (
              <p className="inbox-ack-note">Bevestigd op {formatTimestamp(openMessage.acknowledgedAt)}</p>
            ) : (
              <p className="inbox-ack-note">Dit bericht vraagt om een expliciete bevestiging.</p>
            ))}
        </Modal>
      )}

      {statusMessage && <DeliveryStatusDialog message={statusMessage} onClose={() => setStatusMessage(null)} />}

      {cancelTarget && (
        <ConfirmDialog
          title="Bericht intrekken"
          message={`Weet u zeker dat u "${cancelTarget.subject}" wilt intrekken? Ontvangers zien het bericht daarna niet meer.`}
          confirmLabel="Intrekken"
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
              toast.showSuccess('Bericht verzonden.')
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
  const [status, setStatus] = useState<MessageDeliveryStatus | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getMessageDeliveryStatus(message.id)
      .then(setStatus)
      .catch(() => setError('De bezorgstatus kon niet worden geladen.'))
  }, [message.id])

  return (
    <Modal title="Bezorgstatus" onClose={onClose} footer={<Button onClick={onClose}>Sluiten</Button>}>
      {error && <p className="placeholder-text" role="alert">{error}</p>}
      {!error && status === null && <LoadingState message="Bezorgstatus laden..." />}
      {status && (
        <>
          <p className="inbox-meta">
            {status.subject} · verzonden {formatTimestamp(status.sentAt)}
            {status.cancelledAt && ` · Ingetrokken op ${formatTimestamp(status.cancelledAt)}`}
          </p>
          <table className="inbox-status-table">
            <thead>
              <tr>
                <th>Naam</th>
                <th>Gelezen</th>
                {status.requiresAcknowledgement && <th>Bevestigd</th>}
                <th>E-mail</th>
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
                    {EMAIL_STATUS_LABELS[recipient.emailStatus] ?? recipient.emailStatus}
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

const TARGET_LABELS: Record<ComposeTarget, string> = {
  users: 'Specifieke personen',
  role: 'Iedereen met een rol',
  department: 'Een afdeling',
  all: 'Alle medewerkers',
}

function ComposeDialog({ canBulk, onClose }: { canBulk: boolean; onClose: (sent: boolean) => void }) {
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
      .catch(() => setError('Ontvangers konden niet worden geladen.'))
    if (canBulk) {
      getRoles()
        .then((data) => setRoles(data.filter((role) => role.isActive)))
        .catch(() => {})
      listDepartmentOptions()
        .then(setDepartments)
        .catch(() => {})
    }
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
      const described = describeApiError(err, 'Het bericht kon niet worden verzonden.')
      setError(described.message)
      setFieldErrors(described.fieldErrors)
      setSaving(false)
    }
  }

  return (
    <Modal
      title="Nieuw intern bericht"
      onClose={() => onClose(false)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(false)} disabled={saving}>
            Annuleren
          </Button>
          <Button type="submit" form="compose-form" disabled={saving}>
            {saving ? 'Verzenden…' : 'Verzenden'}
          </Button>
        </>
      }
    >
      <form id="compose-form" onSubmit={handleSubmit} noValidate>
        <ValidationSummary message={error} fieldErrors={fieldErrors} fieldLabels={{ subject: 'Onderwerp', body: 'Bericht' }} />

        {canBulk && (
          <FormField label="Doelgroep" htmlFor="cm-target">
            <select
              id="cm-target"
              value={target}
              onChange={(e) => setTarget(e.target.value as ComposeTarget)}
              disabled={saving}
            >
              {(Object.keys(TARGET_LABELS) as ComposeTarget[]).map((value) => (
                <option key={value} value={value}>
                  {TARGET_LABELS[value]}
                </option>
              ))}
            </select>
          </FormField>
        )}

        {canBulk && target === 'role' && (
          <FormField label="Rol" htmlFor="cm-role" hint="Alle actieve leden van de rol ontvangen het bericht.">
            <select id="cm-role" value={roleId} onChange={(e) => setRoleId(e.target.value)} disabled={saving}>
              <option value="">— Kies een rol —</option>
              {roles.map((role) => (
                <option key={role.id} value={role.id}>
                  {role.name}
                </option>
              ))}
            </select>
          </FormField>
        )}

        {canBulk && target === 'department' && (
          <FormField label="Afdeling" htmlFor="cm-department" hint="Alle medewerkers van de afdeling ontvangen het bericht.">
            <select
              id="cm-department"
              value={departmentId}
              onChange={(e) => setDepartmentId(e.target.value)}
              disabled={saving}
            >
              <option value="">— Kies een afdeling —</option>
              {departments.map((department) => (
                <option key={department.id} value={department.id}>
                  {department.name}
                </option>
              ))}
            </select>
          </FormField>
        )}

        {canBulk && target === 'all' && (
          <p className="inbox-target-note">Het bericht wordt aan alle actieve medewerkers bezorgd.</p>
        )}

        {target === 'users' && (
          <FormField label="Aan (personen)" htmlFor="cm-users">
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

        <FormField label="Onderwerp" htmlFor="cm-subject" required error={getFieldError(fieldErrors, 'subject')}>
          <input id="cm-subject" value={subject} onChange={(e) => setSubject(e.target.value)} maxLength={200} disabled={saving} />
        </FormField>
        <FormField label="Bericht" htmlFor="cm-body" required error={getFieldError(fieldErrors, 'body')}>
          <textarea id="cm-body" rows={5} value={body} onChange={(e) => setBody(e.target.value)} maxLength={8000} disabled={saving} />
        </FormField>

        <FormField label="Prioriteit" htmlFor="cm-priority">
          <select
            id="cm-priority"
            value={priority}
            onChange={(e) => setPriority(e.target.value as MessagePriority)}
            disabled={saving}
          >
            <option value="Normal">{PRIORITY_LABELS.Normal}</option>
            <option value="High">{PRIORITY_LABELS.High}</option>
            <option value="Urgent">{PRIORITY_LABELS.Urgent}</option>
          </select>
        </FormField>

        <FormField label="Opties" htmlFor="cm-ack">
          <label className="customer-form-checkbox">
            <input
              id="cm-ack"
              type="checkbox"
              checked={requiresAcknowledgement}
              onChange={(e) => setRequiresAcknowledgement(e.target.checked)}
              disabled={saving}
            />
            Bevestiging vereist
          </label>
          <label className="customer-form-checkbox">
            <input
              id="cm-email"
              type="checkbox"
              checked={sendEmail}
              onChange={(e) => setSendEmail(e.target.checked)}
              disabled={saving}
            />
            Ook per e-mail versturen
          </label>
        </FormField>

        <FormField label="Zichtbaar vanaf" htmlFor="cm-visible-from" hint="Leeg = direct zichtbaar">
          <input
            id="cm-visible-from"
            type="datetime-local"
            value={visibleFrom}
            onChange={(e) => setVisibleFrom(e.target.value)}
            disabled={saving}
          />
        </FormField>
        <FormField label="Verloopt op" htmlFor="cm-expires-at" hint="Leeg = geen einddatum">
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
