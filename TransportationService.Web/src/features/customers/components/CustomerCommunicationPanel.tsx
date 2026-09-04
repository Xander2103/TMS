import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import {
  NOTIFICATION_GROUP_KEYS,
  NOTIFICATION_OPTION_KEYS,
  getNotificationOverview,
  type CustomerNotificationGroup,
  type NotificationOverviewLine,
} from '../api/customerNotificationsApi'
import {
  createCommunicationRule,
  deleteCommunicationRule,
  listCommunicationRules,
  updateCommunicationRule,
} from '../api/customerCommunicationApi'
import {
  COMMUNICATION_TYPES,
  COMMUNICATION_TYPE_LABEL_KEYS,
  communicationTypeLabel,
  type CustomerCommunicationRule,
  type CustomerCommunicationType,
  type CustomerContact,
  type SaveCommunicationRuleInput,
} from '../types'

interface CustomerCommunicationPanelProps {
  customerId: string
  contacts: CustomerContact[]
}

type DialogState = { mode: 'create' } | { mode: 'edit'; rule: CustomerCommunicationRule } | null

function contactName(contact: CustomerContact): string {
  return contact.displayName?.trim() || `${contact.firstName} ${contact.lastName}`
}

/** Communicatieregels: welk type melding naar welke contactpersonen gaat (mutaties customers.manage_communication). */
export function CustomerCommunicationPanel({ customerId, contacts }: CustomerCommunicationPanelProps) {
  const toast = useToast()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('customers.manage_communication')

  const [rules, setRules] = useState<CustomerCommunicationRule[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [dialog, setDialog] = useState<DialogState>(null)
  const [removeTarget, setRemoveTarget] = useState<CustomerCommunicationRule | null>(null)
  const [busy, setBusy] = useState(false)
  // Bumped whenever a rule changes so the overview above re-reads the recipients.
  const [overviewToken, setOverviewToken] = useState(0)

  const contactsById = useMemo(() => new Map(contacts.map((contact) => [contact.id, contact])), [contacts])

  const reload = useCallback(() => {
    listCommunicationRules(customerId)
      .then((data) => {
        setRules(data)
        setLoadError(null)
      })
      .catch(() => setLoadError(t('customers.communication.loadFailed')))
  }, [customerId, t])

  /** After a rule mutation: re-read the rules AND bump the overview so it re-reads recipients. */
  const reloadAfterMutation = useCallback(() => {
    setOverviewToken((token) => token + 1)
    reload()
  }, [reload])

  useEffect(() => {
    reload()
  }, [reload])

  function describeContacts(rule: CustomerCommunicationRule): string {
    const names = rule.contactIds
      .map((id) => {
        const contact = contactsById.get(id)
        return contact ? contactName(contact) : null
      })
      .filter((name): name is string => name !== null)
    return names.length > 0 ? names.join(', ') : '—'
  }

  const columns: Column<CustomerCommunicationRule>[] = [
    {
      key: 'type',
      header: t('customers.communication.columnType'),
      render: (rule) => communicationTypeLabel(t, rule.type, rule.customTypeLabel),
    },
    { key: 'contacts', header: t('customers.communication.columnContacts'), render: (rule) => describeContacts(rule) },
    { key: 'cc', header: t('customers.communication.columnCc'), render: (rule) => rule.ccEmail ?? '—' },
    { key: 'language', header: t('customers.communication.columnLanguage'), render: (rule) => rule.languageCode ?? '—' },
    {
      key: 'active',
      header: t('customers.communication.columnStatus'),
      render: (rule) =>
        rule.isActive ? (
          <Badge tone="success">{t('ui.statusBadges.active')}</Badge>
        ) : (
          <Badge tone="neutral">{t('ui.statusBadges.inactive')}</Badge>
        ),
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: t('customers.communication.columnActions'),
            render: (rule: CustomerCommunicationRule) => (
              <span className="customer-contact-actions">
                <Button variant="ghost" onClick={() => setDialog({ mode: 'edit', rule })}>
                  {t('ui.actions.edit')}
                </Button>
                <Button variant="ghost" onClick={() => setRemoveTarget(rule)}>
                  {t('ui.actions.delete')}
                </Button>
              </span>
            ),
          },
        ]
      : []),
  ]

  async function handleSave(input: SaveCommunicationRuleInput): Promise<{ ok: boolean; error?: string; fieldErrors?: FieldErrors }> {
    if (!dialog) return { ok: false }
    setBusy(true)
    try {
      if (dialog.mode === 'edit') {
        await updateCommunicationRule(customerId, dialog.rule.id, input)
        toast.showSuccess(t('customers.communication.updated'))
      } else {
        await createCommunicationRule(customerId, input)
        toast.showSuccess(t('customers.communication.added'))
      }
      setDialog(null)
      reloadAfterMutation()
      return { ok: true }
    } catch (err) {
      const described = describeApiError(err, t('customers.communication.saveFailed'))
      return { ok: false, error: described.message, fieldErrors: described.fieldErrors }
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="customer-contacts">
      <NotificationOverview customerId={customerId} reloadToken={overviewToken} />

      <details className="customer-communication-advanced">
        <summary>{t('customers.communication.advancedSummary')}</summary>
        <p className="customer-form-muted">{t('customers.communication.advancedHint')}</p>
      <div className="page-header">
        <h3 style={{ margin: 0 }}>{t('customers.communication.title')}</h3>
        {canManage && (
          <Button variant="secondary" onClick={() => setDialog({ mode: 'create' })}>
            {t('customers.communication.addRule')}
          </Button>
        )}
      </div>

      <DataTable
        columns={columns}
        rows={rules ?? []}
        rowKey={(rule) => rule.id}
        isLoading={rules === null && loadError === null}
        error={loadError}
        emptyMessage={t('customers.communication.empty')}
      />

      {dialog && (
        <CommunicationRuleDialog
          rule={dialog.mode === 'edit' ? dialog.rule : undefined}
          contacts={contacts}
          isSubmitting={busy}
          onClose={() => setDialog(null)}
          onSubmit={handleSave}
        />
      )}

      {removeTarget && (
        <ConfirmDialog
          title={t('customers.communication.removeTitle')}
          message={t('customers.communication.removeMessage', {
            label: communicationTypeLabel(t, removeTarget.type, removeTarget.customTypeLabel),
          })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={busy}
          onConfirm={async () => {
            setBusy(true)
            try {
              await deleteCommunicationRule(customerId, removeTarget.id)
              toast.showSuccess(t('customers.communication.removed'))
              setRemoveTarget(null)
              reloadAfterMutation()
            } catch (err) {
              toast.showError(describeApiError(err, t('customers.communication.removeFailed')).message)
            } finally {
              setBusy(false)
            }
          }}
          onCancel={() => setRemoveTarget(null)}
        />
      )}
      </details>
    </div>
  )
}

/**
 * Sprint 3C — the readable answer to "who receives what?": one line per notification type with
 * the people behind it. CC mailboxes and fallback contacts are routing detail and are only
 * shown when the user asks for them.
 */
function NotificationOverview({ customerId, reloadToken }: { customerId: string; reloadToken: number }) {
  const { t } = useLocale()
  const [lines, setLines] = useState<NotificationOverviewLine[] | null>(null)
  const [showAdvanced, setShowAdvanced] = useState(false)

  useEffect(() => {
    let active = true
    void getNotificationOverview(customerId)
      .then((data) => {
        if (active) setLines(data)
      })
      .catch(() => {
        if (active) setLines([])
      })
    return () => {
      active = false
    }
  }, [customerId, reloadToken])

  if (lines === null) return <p className="placeholder-text">{t('customers.communication.overviewLoading')}</p>

  const configured = lines.filter((line) =>
    line.recipients.some((r) => showAdvanced || !r.isAdvanced),
  )

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>{t('customers.communication.overviewTitle')}</h3>
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={showAdvanced} onChange={(e) => setShowAdvanced(e.target.checked)} />
          {t('customers.communication.showAdvancedRouting')}
        </label>
      </div>
      <p className="customer-form-muted">{t('customers.communication.overviewHint')}</p>

      {configured.length === 0 && <p className="placeholder-text">{t('customers.communication.overviewEmpty')}</p>}

      {(['Transport', 'Facturatie', 'Algemeen'] as CustomerNotificationGroup[]).map((group) => {
        const groupLines = configured.filter((line) => line.group === group)
        if (groupLines.length === 0) return null
        return (
          <div key={group} className="customer-notification-group">
            <div className="nav-subgroup-label">{t(NOTIFICATION_GROUP_KEYS[group])}</div>
            {groupLines.map((line) => (
              <div key={line.optionKey} className="customer-notification-line">
                <strong>{t(NOTIFICATION_OPTION_KEYS[line.optionKey] ?? line.optionKey)}</strong>
                <ul>
                  {line.recipients
                    .filter((r) => showAdvanced || !r.isAdvanced)
                    .map((r, index) => (
                      <li key={`${r.contactId ?? r.email}-${index}`}>
                        {r.name}
                        {!r.isActive && <span className="customer-form-muted"> {t('customers.form.inactiveSuffix')}</span>}
                        {r.isAdvanced && (
                          <span className="customer-form-muted"> ({t('customers.communication.advancedRoutingBadge')})</span>
                        )}
                      </li>
                    ))}
                </ul>
              </div>
            ))}
          </div>
        )
      })}
    </section>
  )
}

function CommunicationRuleDialog({
  rule,
  contacts,
  isSubmitting,
  onSubmit,
  onClose,
}: {
  rule?: CustomerCommunicationRule
  contacts: CustomerContact[]
  isSubmitting: boolean
  onSubmit: (input: SaveCommunicationRuleInput) => Promise<{ ok: boolean; error?: string; fieldErrors?: FieldErrors }>
  onClose: () => void
}) {
  const { t } = useLocale()
  const [type, setType] = useState<CustomerCommunicationType>(rule?.type ?? 'PlanningAlert')
  const [customTypeLabel, setCustomTypeLabel] = useState(rule?.customTypeLabel ?? '')
  const [contactIds, setContactIds] = useState<string[]>(rule?.contactIds ?? [])
  const [ccEmail, setCcEmail] = useState(rule?.ccEmail ?? '')
  const [languageCode, setLanguageCode] = useState(rule?.languageCode ?? '')
  const [fallbackContactId, setFallbackContactId] = useState(rule?.fallbackContactId ?? '')
  const [isActive, setIsActive] = useState(rule?.isActive ?? true)
  const [localErrors, setLocalErrors] = useState<{ contactIds?: string; customTypeLabel?: string; ccEmail?: string }>({})
  const [serverError, setServerError] = useState<string | null>(null)
  const [serverFieldErrors, setServerFieldErrors] = useState<FieldErrors>({})

  // Actieve contacten zijn kiesbaar; reeds gekoppelde (intussen inactieve) contacten blijven
  // zichtbaar zodat bewerken ze niet stilzwijgend laat vallen.
  const selectable = contacts.filter((contact) => contact.isActive || contactIds.includes(contact.id))

  function toggleContact(id: string) {
    setContactIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const next: { contactIds?: string; customTypeLabel?: string; ccEmail?: string } = {}
    if (contactIds.length === 0) next.contactIds = t('customers.communication.contactsRequired')
    if (type === 'Other' && !customTypeLabel.trim()) next.customTypeLabel = t('customers.communication.customLabelRequired')
    if (ccEmail.trim() && !ccEmail.includes('@')) next.ccEmail = t('customers.communication.ccInvalid')
    setLocalErrors(next)
    if (next.contactIds || next.customTypeLabel || next.ccEmail) return

    setServerError(null)
    setServerFieldErrors({})
    const result = await onSubmit({
      type,
      customTypeLabel: type === 'Other' ? customTypeLabel.trim() : null,
      ccEmail: ccEmail.trim() || null,
      languageCode: languageCode.trim() || null,
      fallbackContactId: fallbackContactId || null,
      isActive,
      contactIds,
    })
    if (!result.ok) {
      setServerError(result.error ?? null)
      setServerFieldErrors(result.fieldErrors ?? {})
    }
  }

  return (
    <Modal
      title={rule ? t('customers.communication.editTitle') : t('customers.communication.newTitle')}
      onClose={onClose}
      busy={isSubmitting}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="communication-rule-form" disabled={isSubmitting}>
            {isSubmitting ? t('customers.common.saving') : t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <form id="communication-rule-form" onSubmit={handleSubmit} className="customer-form">
        {serverError && (
          <p className="customer-import-message customer-import-message-error" role="alert">
            {serverError}
          </p>
        )}
        <FormField label={t('customers.communication.columnType')} htmlFor="comm-type" required>
          <select id="comm-type" value={type} onChange={(e) => setType(e.target.value as CustomerCommunicationType)}>
            {COMMUNICATION_TYPES.map((value) => (
              <option key={value} value={value}>
                {t(COMMUNICATION_TYPE_LABEL_KEYS[value])}
              </option>
            ))}
          </select>
        </FormField>
        {type === 'Other' && (
          <FormField
            label={t('customers.communication.customLabelField')}
            htmlFor="comm-custom-label"
            required
            error={localErrors.customTypeLabel ?? getFieldError(serverFieldErrors, 'customTypeLabel')}
          >
            <input
              id="comm-custom-label"
              value={customTypeLabel}
              onChange={(e) => setCustomTypeLabel(e.target.value)}
              maxLength={100}
              aria-invalid={localErrors.customTypeLabel ? 'true' : undefined}
            />
          </FormField>
        )}
        <FormField
          label={t('customers.communication.columnContacts')}
          htmlFor="comm-contacts"
          required
          hint={t('customers.communication.contactsHint')}
          error={localErrors.contactIds ?? getFieldError(serverFieldErrors, 'contactIds')}
        >
          <div
            id="comm-contacts"
            className="customer-comm-contact-list"
            role="group"
            aria-label={t('customers.communication.columnContacts')}
          >
            {selectable.length === 0 && (
              <p className="customer-form-muted">{t('customers.communication.noContacts')}</p>
            )}
            {selectable.map((contact) => (
              <label key={contact.id} className="customer-form-checkbox">
                <input
                  type="checkbox"
                  checked={contactIds.includes(contact.id)}
                  onChange={() => toggleContact(contact.id)}
                />
                {contactName(contact)}
                {contact.email ? ` (${contact.email})` : ''}
                {!contact.isActive ? ` ${t('customers.communication.inactiveSuffix')}` : ''}
              </label>
            ))}
          </div>
        </FormField>
        <FormField
          label={t('customers.communication.ccField')}
          htmlFor="comm-cc"
          hint={t('customers.communication.ccHint')}
          error={localErrors.ccEmail ?? getFieldError(serverFieldErrors, 'ccEmail')}
        >
          <input
            id="comm-cc"
            type="email"
            value={ccEmail}
            onChange={(e) => setCcEmail(e.target.value)}
            maxLength={250}
            aria-invalid={localErrors.ccEmail ? 'true' : undefined}
          />
        </FormField>
        <FormField label={t('customers.communication.columnLanguage')} htmlFor="comm-language" hint={t('customers.communication.languageHint')}>
          <input id="comm-language" value={languageCode} onChange={(e) => setLanguageCode(e.target.value)} maxLength={10} />
        </FormField>
        <FormField
          label={t('customers.communication.fallbackField')}
          htmlFor="comm-fallback"
          hint={t('customers.communication.fallbackHint')}
        >
          <select id="comm-fallback" value={fallbackContactId} onChange={(e) => setFallbackContactId(e.target.value)}>
            <option value="">{t('customers.form.noneOption')}</option>
            {contacts
              .filter((contact) => contact.isActive)
              .map((contact) => (
                <option key={contact.id} value={contact.id}>
                  {contactName(contact)}
                </option>
              ))}
          </select>
        </FormField>
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          {t('ui.statusBadges.active')}
        </label>
      </form>
    </Modal>
  )
}
