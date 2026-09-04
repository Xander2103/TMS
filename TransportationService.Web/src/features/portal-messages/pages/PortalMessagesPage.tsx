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
import { localizeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
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
import { formatDateTime } from '../../../utils/dates'
import './portal-messages.css'

/** Translation keys per priority; render via t(PRIORITY_LABELS[priority]). */
const PRIORITY_LABELS: Record<PortalMessagePriority, string> = {
  Normal: 'portalMessages.priority.Normal',
  High: 'portalMessages.priority.High',
  Urgent: 'portalMessages.priority.Urgent',
}

/** Translation keys per display mode; render via t(DISPLAY_MODE_LABELS[mode]). */
const DISPLAY_MODE_LABELS: Record<PortalMessageDisplayMode, string> = {
  Notification: 'portalMessages.displayMode.Notification',
  DashboardBanner: 'portalMessages.displayMode.DashboardBanner',
  BlockingAcknowledgement: 'portalMessages.displayMode.BlockingAcknowledgement',
}

/** Translation keys per display-mode hint; render via t(DISPLAY_MODE_HINTS[mode]). */
const DISPLAY_MODE_HINTS: Record<PortalMessageDisplayMode, string> = {
  Notification: 'portalMessages.displayModeHint.Notification',
  DashboardBanner: 'portalMessages.displayModeHint.DashboardBanner',
  BlockingAcknowledgement: 'portalMessages.displayModeHint.BlockingAcknowledgement',
}

function formatTimestamp(iso: string): string {
  return formatDateTime(iso)
}

function fromDateTimeLocal(value: string): string | null {
  return value ? new Date(value).toISOString() : null
}

function statusBadge(t: TranslateFn, row: PortalMessageAdminItem) {
  if (row.cancelledAt) return <Badge tone="neutral">{t('portalMessages.status.cancelled')}</Badge>
  const now = Date.now()
  if (row.expiresAt && new Date(row.expiresAt).getTime() < now)
    return <Badge tone="neutral">{t('portalMessages.status.expired')}</Badge>
  if (row.visibleFrom && new Date(row.visibleFrom).getTime() > now)
    return <Badge tone="info">{t('portalMessages.status.scheduled')}</Badge>
  return <Badge tone="success">{t('portalMessages.status.active')}</Badge>
}

/** Staff management of customer-portal messages (Beheer → Portaalberichten). */
export function PortalMessagesPage() {
  const { t } = useLocale()
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
        setError(t('portalMessages.page.loadFailed'))
        setLoaded(true)
      })
  }

  useEffect(load, [t])

  async function handleCancel() {
    if (!cancelTarget) return
    setCancelBusy(true)
    try {
      await cancelPortalMessage(cancelTarget.id)
      toast.showSuccess(t('portalMessages.cancelDialog.cancelled'))
      setCancelTarget(null)
      load()
    } catch (err) {
      toast.showError(localizeApiError(t, err, t('portalMessages.cancelDialog.cancelFailed')))
    } finally {
      setCancelBusy(false)
    }
  }

  const columns: Column<PortalMessageAdminItem>[] = [
    { key: 'title', header: t('portalMessages.columns.title'), render: (row) => row.titleNl },
    {
      key: 'customers',
      header: t('portalMessages.columns.customers'),
      render: (row) => (row.customerNames.length > 0 ? row.customerNames.join(', ') : '—'),
    },
    {
      key: 'displayMode',
      header: t('portalMessages.columns.displayMode'),
      render: (row) => (DISPLAY_MODE_LABELS[row.displayMode] ? t(DISPLAY_MODE_LABELS[row.displayMode]) : row.displayMode),
    },
    {
      key: 'priority',
      header: t('portalMessages.columns.priority'),
      render: (row) => (PRIORITY_LABELS[row.priority] ? t(PRIORITY_LABELS[row.priority]) : row.priority),
    },
    { key: 'status', header: t('portalMessages.columns.status'), render: (row) => statusBadge(t, row) },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <span className="pm-actions">
          <Button variant="secondary" onClick={() => setStatusTarget(row)}>
            {t('portalMessages.actions.deliveryStatus')}
          </Button>
          {row.cancelledAt === null && canSend && (
            <Button variant="secondary" onClick={() => setCancelTarget(row)}>
              {t('portalMessages.actions.cancel')}
            </Button>
          )}
        </span>
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('portalMessages.page.breadcrumbAdmin') }, { label: t('portalMessages.page.title') }]} />
      <PageHeader
        title={t('portalMessages.page.title')}
        subtitle={t('portalMessages.page.subtitle')}
        action={canSend && <Button onClick={() => setComposeOpen(true)}>{t('portalMessages.page.newMessage')}</Button>}
      />
      <DataTable
        columns={columns}
        rows={messages}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage={t('portalMessages.page.empty')}
        loadingMessage={t('portalMessages.page.loading')}
      />

      {composeOpen && (
        <ComposeMessageDialog
          onClose={(sentOk) => {
            setComposeOpen(false)
            if (sentOk) {
              toast.showSuccess(t('portalMessages.page.sent'))
              load()
            }
          }}
        />
      )}

      {statusTarget && <PortalDeliveryStatusDialog message={statusTarget} onClose={() => setStatusTarget(null)} />}

      {cancelTarget && (
        <ConfirmDialog
          title={t('portalMessages.cancelDialog.title')}
          message={t('portalMessages.cancelDialog.message', { title: cancelTarget.titleNl })}
          confirmLabel={t('portalMessages.cancelDialog.confirm')}
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
  const { t } = useLocale()
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
      .catch(() => setError(t('portalMessages.compose.customersLoadFailed')))
    // eslint-disable-next-line react-hooks/exhaustive-deps
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
      setError(localizeApiError(t, err, t('portalMessages.compose.sendFailed')))
      setSaving(false)
    }
  }

  return (
    <Modal
      title={t('portalMessages.compose.title')}
      onClose={() => onClose(false)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(false)} disabled={saving}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="portal-message-form" disabled={saving}>
            {saving ? t('portalMessages.compose.sending') : t('portalMessages.compose.send')}
          </Button>
        </>
      }
    >
      <form id="portal-message-form" onSubmit={(e) => void handleSubmit(e)} noValidate>
        {error && <p className="placeholder-text" role="alert">{error}</p>}

        <fieldset className="pm-lang-section">
          <legend>{t('portalMessages.compose.legendNl')}</legend>
          <FormField label={t('portalMessages.compose.titleNl')} htmlFor="pm-title-nl" required>
            <input id="pm-title-nl" value={titleNl} onChange={(e) => setTitleNl(e.target.value)} maxLength={200} disabled={saving} />
          </FormField>
          <FormField label={t('portalMessages.compose.bodyNl')} htmlFor="pm-body-nl" required>
            <textarea id="pm-body-nl" rows={4} value={bodyNl} onChange={(e) => setBodyNl(e.target.value)} maxLength={8000} disabled={saving} />
          </FormField>
        </fieldset>

        <fieldset className="pm-lang-section">
          <legend>{t('portalMessages.compose.legendFr')}</legend>
          <FormField label={t('portalMessages.compose.titleFr')} htmlFor="pm-title-fr">
            <input id="pm-title-fr" value={titleFr} onChange={(e) => setTitleFr(e.target.value)} maxLength={200} disabled={saving} />
          </FormField>
          <FormField label={t('portalMessages.compose.bodyFr')} htmlFor="pm-body-fr">
            <textarea id="pm-body-fr" rows={4} value={bodyFr} onChange={(e) => setBodyFr(e.target.value)} maxLength={8000} disabled={saving} />
          </FormField>
        </fieldset>

        <fieldset className="pm-lang-section">
          <legend>{t('portalMessages.compose.legendEn')}</legend>
          <FormField label={t('portalMessages.compose.titleEn')} htmlFor="pm-title-en">
            <input id="pm-title-en" value={titleEn} onChange={(e) => setTitleEn(e.target.value)} maxLength={200} disabled={saving} />
          </FormField>
          <FormField label={t('portalMessages.compose.bodyEn')} htmlFor="pm-body-en">
            <textarea id="pm-body-en" rows={4} value={bodyEn} onChange={(e) => setBodyEn(e.target.value)} maxLength={8000} disabled={saving} />
          </FormField>
        </fieldset>

        <FormField
          label={t('portalMessages.compose.customers')}
          htmlFor="pm-customers"
          hint={t('portalMessages.compose.customersHint')}
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
            {t('portalMessages.compose.bulkWarning')}
          </p>
        )}

        <FormField label={t('portalMessages.compose.displayMode')} htmlFor="pm-display-mode" hint={t(DISPLAY_MODE_HINTS[displayMode])}>
          <select
            id="pm-display-mode"
            value={displayMode}
            onChange={(e) => setDisplayMode(e.target.value as PortalMessageDisplayMode)}
            disabled={saving}
          >
            {(Object.keys(DISPLAY_MODE_LABELS) as PortalMessageDisplayMode[]).map((mode) => (
              <option key={mode} value={mode}>
                {t(DISPLAY_MODE_LABELS[mode])}
              </option>
            ))}
          </select>
        </FormField>

        <FormField label={t('portalMessages.compose.priority')} htmlFor="pm-priority">
          <select
            id="pm-priority"
            value={priority}
            onChange={(e) => setPriority(e.target.value as PortalMessagePriority)}
            disabled={saving}
          >
            <option value="Normal">{t(PRIORITY_LABELS.Normal)}</option>
            <option value="High">{t(PRIORITY_LABELS.High)}</option>
            <option value="Urgent">{t(PRIORITY_LABELS.Urgent)}</option>
          </select>
        </FormField>

        <FormField label={t('portalMessages.compose.options')} htmlFor="pm-ack">
          <label className="customer-form-checkbox">
            <input
              id="pm-ack"
              type="checkbox"
              checked={requiresAcknowledgement}
              onChange={(e) => setRequiresAcknowledgement(e.target.checked)}
              disabled={saving}
            />
            {t('portalMessages.compose.requiresAck')}
          </label>
          <label className="customer-form-checkbox">
            <input
              id="pm-email"
              type="checkbox"
              checked={sendEmail}
              onChange={(e) => setSendEmail(e.target.checked)}
              disabled={saving}
            />
            {t('portalMessages.compose.sendEmail')}
          </label>
        </FormField>

        <FormField label={t('portalMessages.compose.visibleFrom')} htmlFor="pm-visible-from" hint={t('portalMessages.compose.visibleFromHint')}>
          <input
            id="pm-visible-from"
            type="datetime-local"
            value={visibleFrom}
            onChange={(e) => setVisibleFrom(e.target.value)}
            disabled={saving}
          />
        </FormField>
        <FormField label={t('portalMessages.compose.expiresAt')} htmlFor="pm-expires-at" hint={t('portalMessages.compose.expiresAtHint')}>
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
  const { t } = useLocale()
  const [status, setStatus] = useState<PortalMessageDeliveryStatus | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getPortalMessageDeliveryStatus(message.id)
      .then(setStatus)
      .catch(() => setError(t('portalMessages.delivery.loadFailed')))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [message.id])

  return (
    <Modal title={t('portalMessages.delivery.title')} onClose={onClose} footer={<Button onClick={onClose}>{t('ui.actions.close')}</Button>}>
      {error && <p className="placeholder-text" role="alert">{error}</p>}
      {!error && status === null && <LoadingState message={t('portalMessages.delivery.loading')} />}
      {status && (
        <>
          <p className="pm-status-meta">
            {status.titleNl} · {t('portalMessages.delivery.createdAt', { dateTime: formatTimestamp(status.createdAt) })}
            {status.cancelledAt &&
              ` · ${t('portalMessages.delivery.cancelledAt', { dateTime: formatTimestamp(status.cancelledAt) })}`}
          </p>
          <table className="pm-status-table">
            <thead>
              <tr>
                <th>{t('portalMessages.delivery.columns.name')}</th>
                <th>{t('portalMessages.delivery.columns.customer')}</th>
                <th>{t('portalMessages.delivery.columns.read')}</th>
                {status.requiresAcknowledgement && <th>{t('portalMessages.delivery.columns.acknowledged')}</th>}
                <th>{t('portalMessages.delivery.columns.email')}</th>
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
