import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers } from '../../customers/api/customersApi'
import type { CustomerListItem } from '../../customers/types'
import {
  cancelPortalMessage,
  getPortalMessageDeliveryStatus,
  listPortalMessagesAdmin,
  sendPortalMessage,
  type PortalMessageAdminItem,
  type PortalMessageDeliveryStatus,
  type PortalMessageDisplayMode,
  type PortalMessagePriority,
} from '../api/portalMessagesApi'
import './portal-messages.css'

const PRIORITY_LABELS: Record<PortalMessagePriority, string> = { Normal: 'Normaal', High: 'Hoog', Urgent: 'Dringend' }

const DISPLAY_MODE_LABELS: Record<PortalMessageDisplayMode, string> = {
  Notification: 'Melding',
  DashboardBanner: 'Dashboardbanner',
  BlockingAcknowledgement: 'Blokkerende bevestiging',
}

const DISPLAY_MODE_HINTS: Record<PortalMessageDisplayMode, string> = {
  Notification: 'Verschijnt in de mededelingenlijst van het portaal.',
  DashboardBanner: 'Verschijnt bovendien als banner bovenaan het portaaldashboard.',
  BlockingAcknowledgement:
    'Blokkerend: het dashboard wordt afgedekt tot de gebruiker bevestigt — spaarzaam gebruiken.',
}

function formatTimestamp(iso: string): string {
  return iso.slice(0, 16).replace('T', ' ')
}

function fromDateTimeLocal(value: string): string | null {
  return value ? new Date(value).toISOString() : null
}

function statusBadge(row: PortalMessageAdminItem) {
  if (row.cancelledAt) return <Badge tone="neutral">Ingetrokken</Badge>
  const now = Date.now()
  if (row.expiresAt && new Date(row.expiresAt).getTime() < now) return <Badge tone="neutral">Verlopen</Badge>
  if (row.visibleFrom && new Date(row.visibleFrom).getTime() > now) return <Badge tone="info">Gepland</Badge>
  return <Badge tone="success">Actief</Badge>
}

/** Staff management of customer-portal messages (Beheer → Portaalberichten). */
export function PortalMessagesPage() {
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canSend = hasPermission('portal_messages.send')

  const [messages, setMessages] = useState<PortalMessageAdminItem[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [composeOpen, setComposeOpen] = useState(false)
  const [statusTarget, setStatusTarget] = useState<PortalMessageAdminItem | null>(null)
  const [cancelTarget, setCancelTarget] = useState<PortalMessageAdminItem | null>(null)
  const [cancelBusy, setCancelBusy] = useState(false)

  function load() {
    listPortalMessagesAdmin()
      .then((rows) => {
        setMessages(rows)
        setLoaded(true)
        setError(null)
      })
      .catch(() => {
        setError('De portaalberichten konden niet worden geladen.')
        setLoaded(true)
      })
  }

  useEffect(load, [])

  async function handleCancel() {
    if (!cancelTarget) return
    setCancelBusy(true)
    try {
      await cancelPortalMessage(cancelTarget.id)
      toast.showSuccess('Bericht ingetrokken.')
      setCancelTarget(null)
      load()
    } catch (err) {
      toast.showError(describeApiError(err, 'Het bericht kon niet worden ingetrokken.').message)
    } finally {
      setCancelBusy(false)
    }
  }

  const columns: Column<PortalMessageAdminItem>[] = [
    { key: 'title', header: 'Titel (NL)', render: (row) => row.titleNl },
    {
      key: 'customers',
      header: 'Klanten',
      render: (row) => (row.customerNames.length > 0 ? row.customerNames.join(', ') : '—'),
    },
    { key: 'displayMode', header: 'Weergave', render: (row) => DISPLAY_MODE_LABELS[row.displayMode] ?? row.displayMode },
    {
      key: 'priority',
      header: 'Prioriteit',
      render: (row) => PRIORITY_LABELS[row.priority] ?? row.priority,
    },
    { key: 'status', header: 'Status', render: statusBadge },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <span className="pm-actions">
          <Button variant="secondary" onClick={() => setStatusTarget(row)}>
            Bezorgstatus
          </Button>
          {row.cancelledAt === null && canSend && (
            <Button variant="secondary" onClick={() => setCancelTarget(row)}>
              Intrekken
            </Button>
          )}
        </span>
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Beheer' }, { label: 'Portaalberichten' }]} />
      <PageHeader
        title="Portaalberichten"
        subtitle="Berichten aan klantportaalgebruikers, met vertalingen en bezorgopvolging."
        action={canSend && <Button onClick={() => setComposeOpen(true)}>Nieuw bericht</Button>}
      />
      <DataTable
        columns={columns}
        rows={messages}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage="Nog geen portaalberichten."
        loadingMessage="Portaalberichten laden..."
      />

      {composeOpen && (
        <ComposeMessageDialog
          onClose={(sentOk) => {
            setComposeOpen(false)
            if (sentOk) {
              toast.showSuccess('Portaalbericht verzonden.')
              load()
            }
          }}
        />
      )}

      {statusTarget && <PortalDeliveryStatusDialog message={statusTarget} onClose={() => setStatusTarget(null)} />}

      {cancelTarget && (
        <ConfirmDialog
          title="Bericht intrekken"
          message={`Weet u zeker dat u "${cancelTarget.titleNl}" wilt intrekken? Portaalgebruikers zien het bericht daarna niet meer.`}
          confirmLabel="Intrekken"
          destructive
          busy={cancelBusy}
          onConfirm={() => void handleCancel()}
          onCancel={() => setCancelTarget(null)}
        />
      )}
    </div>
  )
}

function ComposeMessageDialog({ onClose }: { onClose: (sent: boolean) => void }) {
  const [customers, setCustomers] = useState<CustomerListItem[]>([])
  const [selectedCustomers, setSelectedCustomers] = useState<Set<string>>(new Set())
  const [titleNl, setTitleNl] = useState('')
  const [bodyNl, setBodyNl] = useState('')
  const [titleFr, setTitleFr] = useState('')
  const [bodyFr, setBodyFr] = useState('')
  const [titleEn, setTitleEn] = useState('')
  const [bodyEn, setBodyEn] = useState('')
  const [priority, setPriority] = useState<PortalMessagePriority>('Normal')
  const [displayMode, setDisplayMode] = useState<PortalMessageDisplayMode>('Notification')
  const [requiresAcknowledgement, setRequiresAcknowledgement] = useState(false)
  const [visibleFrom, setVisibleFrom] = useState('')
  const [expiresAt, setExpiresAt] = useState('')
  const [sendEmail, setSendEmail] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    searchCustomers({ page: 1, pageSize: 500, isActive: true })
      .then((result) => setCustomers(result.items))
      .catch(() => setError('De klantenlijst kon niet worden geladen.'))
  }, [])

  function toggleCustomer(id: string) {
    setSelectedCustomers((current) => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      await sendPortalMessage({
        titleNl: titleNl.trim(),
        bodyNl: bodyNl.trim(),
        titleFr: titleFr.trim() || null,
        bodyFr: bodyFr.trim() || null,
        titleEn: titleEn.trim() || null,
        bodyEn: bodyEn.trim() || null,
        customerIds: [...selectedCustomers],
        priority,
        displayMode,
        requiresAcknowledgement,
        visibleFrom: fromDateTimeLocal(visibleFrom),
        expiresAt: fromDateTimeLocal(expiresAt),
        sendEmail,
      })
      onClose(true)
    } catch (err) {
      setError(describeApiError(err, 'Het portaalbericht kon niet worden verzonden.').message)
      setSaving(false)
    }
  }

  return (
    <Modal
      title="Nieuw portaalbericht"
      onClose={() => onClose(false)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(false)} disabled={saving}>
            Annuleren
          </Button>
          <Button type="submit" form="portal-message-form" disabled={saving}>
            {saving ? 'Verzenden…' : 'Verzenden'}
          </Button>
        </>
      }
    >
      <form id="portal-message-form" onSubmit={(e) => void handleSubmit(e)} noValidate>
        {error && <p className="placeholder-text" role="alert">{error}</p>}

        <fieldset className="pm-lang-section">
          <legend>Nederlands (verplicht)</legend>
          <FormField label="Titel (NL)" htmlFor="pm-title-nl" required>
            <input id="pm-title-nl" value={titleNl} onChange={(e) => setTitleNl(e.target.value)} maxLength={200} disabled={saving} />
          </FormField>
          <FormField label="Inhoud (NL)" htmlFor="pm-body-nl" required>
            <textarea id="pm-body-nl" rows={4} value={bodyNl} onChange={(e) => setBodyNl(e.target.value)} maxLength={8000} disabled={saving} />
          </FormField>
        </fieldset>

        <fieldset className="pm-lang-section">
          <legend>Frans (optioneel)</legend>
          <FormField label="Titel (FR)" htmlFor="pm-title-fr">
            <input id="pm-title-fr" value={titleFr} onChange={(e) => setTitleFr(e.target.value)} maxLength={200} disabled={saving} />
          </FormField>
          <FormField label="Inhoud (FR)" htmlFor="pm-body-fr">
            <textarea id="pm-body-fr" rows={4} value={bodyFr} onChange={(e) => setBodyFr(e.target.value)} maxLength={8000} disabled={saving} />
          </FormField>
        </fieldset>

        <fieldset className="pm-lang-section">
          <legend>Engels (optioneel)</legend>
          <FormField label="Titel (EN)" htmlFor="pm-title-en">
            <input id="pm-title-en" value={titleEn} onChange={(e) => setTitleEn(e.target.value)} maxLength={200} disabled={saving} />
          </FormField>
          <FormField label="Inhoud (EN)" htmlFor="pm-body-en">
            <textarea id="pm-body-en" rows={4} value={bodyEn} onChange={(e) => setBodyEn(e.target.value)} maxLength={8000} disabled={saving} />
          </FormField>
        </fieldset>

        <FormField
          label="Klanten"
          htmlFor="pm-customers"
          hint="Meerdere klanten selecteren vereist de bulkpermissie (portal_messages.send_bulk)."
        >
          <div id="pm-customers" className="pm-customer-list">
            {customers.map((customer) => (
              <label key={customer.id} className="customer-form-checkbox">
                <input
                  type="checkbox"
                  checked={selectedCustomers.has(customer.id)}
                  onChange={() => toggleCustomer(customer.id)}
                  disabled={saving}
                />
                {customer.name}
              </label>
            ))}
          </div>
        </FormField>
        {selectedCustomers.size > 1 && (
          <p className="pm-bulk-warning" role="note">
            Let op: u verstuurt naar meerdere klanten. Dit vereist de permissie portal_messages.send_bulk.
          </p>
        )}

        <FormField label="Weergavemodus" htmlFor="pm-display-mode" hint={DISPLAY_MODE_HINTS[displayMode]}>
          <select
            id="pm-display-mode"
            value={displayMode}
            onChange={(e) => setDisplayMode(e.target.value as PortalMessageDisplayMode)}
            disabled={saving}
          >
            {(Object.keys(DISPLAY_MODE_LABELS) as PortalMessageDisplayMode[]).map((mode) => (
              <option key={mode} value={mode}>
                {DISPLAY_MODE_LABELS[mode]}
              </option>
            ))}
          </select>
        </FormField>

        <FormField label="Prioriteit" htmlFor="pm-priority">
          <select
            id="pm-priority"
            value={priority}
            onChange={(e) => setPriority(e.target.value as PortalMessagePriority)}
            disabled={saving}
          >
            <option value="Normal">{PRIORITY_LABELS.Normal}</option>
            <option value="High">{PRIORITY_LABELS.High}</option>
            <option value="Urgent">{PRIORITY_LABELS.Urgent}</option>
          </select>
        </FormField>

        <FormField label="Opties" htmlFor="pm-ack">
          <label className="customer-form-checkbox">
            <input
              id="pm-ack"
              type="checkbox"
              checked={requiresAcknowledgement}
              onChange={(e) => setRequiresAcknowledgement(e.target.checked)}
              disabled={saving}
            />
            Bevestiging vereist
          </label>
          <label className="customer-form-checkbox">
            <input
              id="pm-email"
              type="checkbox"
              checked={sendEmail}
              onChange={(e) => setSendEmail(e.target.checked)}
              disabled={saving}
            />
            Ook per e-mail versturen
          </label>
        </FormField>

        <FormField label="Zichtbaar vanaf" htmlFor="pm-visible-from" hint="Leeg = direct zichtbaar">
          <input
            id="pm-visible-from"
            type="datetime-local"
            value={visibleFrom}
            onChange={(e) => setVisibleFrom(e.target.value)}
            disabled={saving}
          />
        </FormField>
        <FormField label="Verloopt op" htmlFor="pm-expires-at" hint="Leeg = geen einddatum">
          <input
            id="pm-expires-at"
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

function PortalDeliveryStatusDialog({ message, onClose }: { message: PortalMessageAdminItem; onClose: () => void }) {
  const [status, setStatus] = useState<PortalMessageDeliveryStatus | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getPortalMessageDeliveryStatus(message.id)
      .then(setStatus)
      .catch(() => setError('De bezorgstatus kon niet worden geladen.'))
  }, [message.id])

  return (
    <Modal title="Bezorgstatus" onClose={onClose} footer={<Button onClick={onClose}>Sluiten</Button>}>
      {error && <p className="placeholder-text" role="alert">{error}</p>}
      {!error && status === null && <LoadingState message="Bezorgstatus laden..." />}
      {status && (
        <>
          <p className="pm-status-meta">
            {status.titleNl} · aangemaakt {formatTimestamp(status.createdAt)}
            {status.cancelledAt && ` · Ingetrokken op ${formatTimestamp(status.cancelledAt)}`}
          </p>
          <table className="pm-status-table">
            <thead>
              <tr>
                <th>Naam</th>
                <th>Klant</th>
                <th>Gelezen</th>
                {status.requiresAcknowledgement && <th>Bevestigd</th>}
                <th>E-mail</th>
              </tr>
            </thead>
            <tbody>
              {status.recipients.map((recipient) => (
                <tr key={recipient.userId}>
                  <td>{recipient.name}</td>
                  <td>{recipient.customerName}</td>
                  <td>{recipient.readAt ? formatTimestamp(recipient.readAt) : '—'}</td>
                  {status.requiresAcknowledgement && (
                    <td>{recipient.acknowledgedAt ? formatTimestamp(recipient.acknowledgedAt) : '—'}</td>
                  )}
                  <td>
                    {recipient.emailStatus}
                    {recipient.emailFailureReason && <span className="pm-email-failure"> — {recipient.emailFailureReason}</span>}
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
