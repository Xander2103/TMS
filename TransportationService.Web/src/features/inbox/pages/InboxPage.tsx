import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { Tabs, TabPanel } from '../../../components/ui/Tabs'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { EmptyState } from '../../../components/ui/EmptyState'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { useToast } from '../../../components/ui/toastContext'
import { apiClient } from '../../../api/apiClient'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { getRoles } from '../../roles/api/rolesApi'
import type { Role } from '../../roles/types/role'
import './inbox.css'

interface InboxMessage {
  id: string
  subject: string
  body: string
  senderName: string
  sentAt: string
  readAt: string | null
  recipientCount: number
}

function formatTimestamp(value: string): string {
  return value.slice(0, 16).replace('T', ' ')
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

  const reload = useCallback(() => {
    apiClient.getJson<InboxMessage[]>('/api/internal-messages/inbox').then(setInbox).catch(() => setInbox([]))
    if (canSend) {
      apiClient.getJson<InboxMessage[]>('/api/internal-messages/sent').then(setSent).catch(() => setSent([]))
    }
  }, [canSend])

  useEffect(() => {
    reload()
  }, [reload])

  function openAndMarkRead(message: InboxMessage) {
    setOpenMessage(message)
    if (message.readAt === null) {
      void apiClient
        .postJson<void, Record<string, never>>(`/api/internal-messages/${message.id}/read`, {})
        .then(reload)
        .catch(() => {})
    }
  }

  function renderList(messages: InboxMessage[] | null, kind: 'inbox' | 'sent') {
    if (messages === null) return <LoadingState message="Berichten laden..." />
    if (messages.length === 0) {
      return <EmptyState message={kind === 'inbox' ? 'Geen berichten in je inbox.' : 'Nog geen verzonden berichten.'} />
    }

    return (
      <ul className="inbox-list">
        {messages.map((message) => (
          <li key={message.id}>
            <button
              type="button"
              className={`inbox-item ${kind === 'inbox' && message.readAt === null ? 'inbox-unread' : ''}`}
              onClick={() => openAndMarkRead(message)}
            >
              <span className="inbox-subject">
                {kind === 'inbox' && message.readAt === null && <Badge tone="info">nieuw</Badge>} {message.subject}
              </span>
              <span className="inbox-meta">
                {kind === 'inbox' ? message.senderName : `${message.recipientCount} ontvanger(s)`} ·{' '}
                {formatTimestamp(message.sentAt)}
              </span>
            </button>
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
          footer={<Button onClick={() => setOpenMessage(null)}>Sluiten</Button>}
        >
          <p className="inbox-meta">
            {openMessage.senderName} · {formatTimestamp(openMessage.sentAt)}
          </p>
          <p className="inbox-body">{openMessage.body}</p>
        </Modal>
      )}

      {composeOpen && (
        <ComposeDialog
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

function ComposeDialog({ onClose }: { onClose: (sent: boolean) => void }) {
  const [recipients, setRecipients] = useState<{ userId: string; name: string }[]>([])
  const [roles, setRoles] = useState<Role[]>([])
  const [selectedUsers, setSelectedUsers] = useState<Set<string>>(new Set())
  const [roleId, setRoleId] = useState('')
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    apiClient
      .getJson<{ userId: string; name: string }[]>('/api/internal-messages/recipients')
      .then(setRecipients)
      .catch(() => setError('Ontvangers konden niet worden geladen.'))
    getRoles()
      .then((data) => setRoles(data.filter((role) => role.isActive)))
      .catch(() => {})
  }, [])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    setFieldErrors({})
    try {
      await apiClient.postJson<{ recipients: number }, object>('/api/internal-messages', {
        subject: subject.trim(),
        body: body.trim(),
        userIds: [...selectedUsers],
        roleId: roleId || null,
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
        <FormField label="Aan (rol)" htmlFor="cm-role" hint="Alle actieve leden van de rol ontvangen het bericht.">
          <select id="cm-role" value={roleId} onChange={(e) => setRoleId(e.target.value)} disabled={saving}>
            <option value="">— Geen rol —</option>
            {roles.map((role) => (
              <option key={role.id} value={role.id}>
                {role.name}
              </option>
            ))}
          </select>
        </FormField>
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
        <FormField label="Onderwerp" htmlFor="cm-subject" required error={getFieldError(fieldErrors, 'subject')}>
          <input id="cm-subject" value={subject} onChange={(e) => setSubject(e.target.value)} maxLength={200} disabled={saving} />
        </FormField>
        <FormField label="Bericht" htmlFor="cm-body" required error={getFieldError(fieldErrors, 'body')}>
          <textarea id="cm-body" rows={5} value={body} onChange={(e) => setBody(e.target.value)} maxLength={8000} disabled={saving} />
        </FormField>
      </form>
    </Modal>
  )
}
