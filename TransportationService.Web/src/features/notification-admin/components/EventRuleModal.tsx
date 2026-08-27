import { useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { usePermissions } from '../../roles/hooks/usePermissions'
import { COMMUNICATION_TYPES, COMMUNICATION_TYPE_LABEL_KEYS, type CustomerCommunicationType } from '../../customers/types'
import { updateNotificationRule } from '../api/notificationAdminApi'
import {
  RECIPIENT_TYPES_WITH_VALUE,
  RECIPIENT_TYPE_LABELS,
  type NotificationRecipientType,
  type NotificationRule,
  type RecipientSpec,
} from '../types'

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const RECIPIENT_TYPES = Object.keys(RECIPIENT_TYPE_LABELS) as NotificationRecipientType[]

interface EventRuleModalProps {
  rule: NotificationRule
  onClose: () => void
  onSaved: () => void
}

/** Recipients + channel/enable editor for one notification event (Gebeurtenissen tab). */
export function EventRuleModal({ rule, onClose, onSaved }: EventRuleModalProps) {
  const { t } = useLocale()
  const { showSuccess } = useToast()
  const { permissions } = usePermissions()

  const [enabled, setEnabled] = useState(rule.enabled)
  const [inAppEnabled, setInAppEnabled] = useState(rule.inAppEnabled)
  const [emailEnabled, setEmailEnabled] = useState(rule.emailEnabled)
  const [allowCustomerOverride, setAllowCustomerOverride] = useState(rule.allowCustomerOverride)
  const [recipients, setRecipients] = useState<RecipientSpec[]>(rule.recipients)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function updateRecipient(index: number, patch: Partial<RecipientSpec>) {
    setRecipients((rows) => rows.map((r, i) => (i === index ? { ...r, ...patch } : r)))
  }

  function removeRecipient(index: number) {
    setRecipients((rows) => rows.filter((_, i) => i !== index))
  }

  function addRecipient() {
    setRecipients((rows) => [...rows, { type: 'ExplicitEmail', value: '' }])
  }

  function validate(): string | null {
    for (const recipient of recipients) {
      const value = recipient.value?.trim() ?? ''
      if (recipient.type === 'ExplicitEmail' && !EMAIL_PATTERN.test(value)) {
        return t('notificationAdmin.ruleModal.invalidEmail')
      }
      if (RECIPIENT_TYPES_WITH_VALUE.includes(recipient.type) && recipient.type !== 'ExplicitEmail' && !value) {
        return t('notificationAdmin.ruleModal.valueRequired', { type: t(RECIPIENT_TYPE_LABELS[recipient.type]) })
      }
    }
    return null
  }

  async function handleSave() {
    const validationError = validate()
    if (validationError) {
      setError(validationError)
      return
    }
    setBusy(true)
    setError(null)
    try {
      await updateNotificationRule(rule.eventKey, {
        enabled,
        inAppEnabled,
        emailEnabled,
        allowCustomerOverride,
        recipients: recipients.map((r) => ({
          type: r.type,
          value: RECIPIENT_TYPES_WITH_VALUE.includes(r.type) ? (r.value?.trim() || null) : null,
        })),
        // Echo the effective review setting — the modal edits recipients/channels, not the hold.
        requiresReview: rule.requiresReview,
      })
      showSuccess(t('notificationAdmin.ruleModal.saved'))
      onSaved()
    } catch (err) {
      setError(localizeApiError(t, err, t('notificationAdmin.ruleModal.saveFailed')))
    } finally {
      setBusy(false)
    }
  }

  const permissionOptions = permissions.map((p) => ({
    value: p.code,
    label: p.description || p.code,
    keywords: p.code,
  }))

  return (
    <Modal
      title={t('notificationAdmin.ruleModal.title', { label: rule.label })}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button onClick={() => void handleSave()} disabled={busy}>
            {t('ui.actions.save')}
          </Button>
        </>
      }
    >
      {rule.peppolPending && (
        <p className="notification-admin-peppol-note">
          <Badge tone="warning">{t('notificationAdmin.events.peppolPending')}</Badge>{' '}
          {t('notificationAdmin.ruleModal.peppolNote')}
        </p>
      )}
      {error && (
        <div role="alert" className="notification-admin-form-error">
          {error}
        </div>
      )}

      <div className="notification-admin-modal-row">
        <label className="notification-admin-checkbox">
          <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} disabled={busy} />
          {t('notificationAdmin.ruleModal.active')}
        </label>
        <label className="notification-admin-checkbox">
          <input type="checkbox" checked={inAppEnabled} onChange={(e) => setInAppEnabled(e.target.checked)} disabled={busy} />
          {t('notificationAdmin.events.inApp')}
        </label>
        <label className="notification-admin-checkbox">
          <input type="checkbox" checked={emailEnabled} onChange={(e) => setEmailEnabled(e.target.checked)} disabled={busy} />
          {t('notificationAdmin.events.email')}
        </label>
        <label className="notification-admin-checkbox notification-admin-checkbox-disabled">
          <input type="checkbox" checked={false} disabled title={t('notificationAdmin.events.smsUnavailable')} />
          {t('notificationAdmin.ruleModal.smsSoon')}
        </label>
      </div>

      <label className="notification-admin-checkbox">
        <input
          type="checkbox"
          checked={allowCustomerOverride}
          onChange={(e) => setAllowCustomerOverride(e.target.checked)}
          disabled={busy}
        />
        {t('notificationAdmin.ruleModal.allowOverride')}
      </label>

      <fieldset className="notification-admin-recipients">
        <legend>{t('notificationAdmin.ruleModal.recipientsLegend')}</legend>
        {recipients.length === 0 && (
          <p className="placeholder-text">{t('notificationAdmin.ruleModal.noRecipients')}</p>
        )}
        {recipients.map((recipient, index) => (
          <div key={index} className="notification-admin-recipient-row">
            <select
              aria-label={t('notificationAdmin.ruleModal.typeAria', { number: index + 1 })}
              value={recipient.type}
              disabled={busy}
              onChange={(e) => updateRecipient(index, { type: e.target.value as NotificationRecipientType, value: null })}
            >
              {RECIPIENT_TYPES.map((type) => (
                <option key={type} value={type}>
                  {t(RECIPIENT_TYPE_LABELS[type])}
                </option>
              ))}
            </select>

            {recipient.type === 'ExplicitEmail' && (
              <input
                aria-label={t('notificationAdmin.ruleModal.emailAria', { number: index + 1 })}
                type="email"
                placeholder={t('notificationAdmin.ruleModal.emailPlaceholder')}
                value={recipient.value ?? ''}
                disabled={busy}
                onChange={(e) => updateRecipient(index, { value: e.target.value })}
              />
            )}

            {recipient.type === 'InternalPermission' && (
              <SearchableSelect
                ariaLabel={t('notificationAdmin.ruleModal.permissionAria', { number: index + 1 })}
                value={recipient.value}
                onChange={(value) => updateRecipient(index, { value })}
                options={permissionOptions}
                disabled={busy}
                placeholder={t('notificationAdmin.ruleModal.permissionPlaceholder')}
              />
            )}

            {recipient.type === 'InternalRole' && (
              <span className="notification-admin-inline-field">
                <input
                  aria-label={t('notificationAdmin.ruleModal.roleAria', { number: index + 1 })}
                  placeholder={t('notificationAdmin.ruleModal.rolePlaceholder')}
                  title={t('notificationAdmin.ruleModal.roleTitle')}
                  value={recipient.value ?? ''}
                  disabled={busy}
                  onChange={(e) => updateRecipient(index, { value: e.target.value })}
                />
              </span>
            )}

            {recipient.type === 'CustomerCommunicationRule' && (
              <select
                aria-label={t('notificationAdmin.ruleModal.communicationAria', { number: index + 1 })}
                value={recipient.value ?? ''}
                disabled={busy}
                onChange={(e) => updateRecipient(index, { value: e.target.value })}
              >
                <option value="">{t('notificationAdmin.ruleModal.communicationPlaceholder')}</option>
                {COMMUNICATION_TYPES.map((type: CustomerCommunicationType) => (
                  <option key={type} value={type}>
                    {t(COMMUNICATION_TYPE_LABEL_KEYS[type])}
                  </option>
                ))}
              </select>
            )}

            {(recipient.type === 'CustomerPrimaryContact' || recipient.type === 'Driver') && (
              <span className="notification-admin-muted">{t('notificationAdmin.ruleModal.noExtraData')}</span>
            )}

            <Button variant="ghost" onClick={() => removeRecipient(index)} disabled={busy}>
              {t('ui.actions.delete')}
            </Button>
          </div>
        ))}
        <Button variant="secondary" onClick={addRecipient} disabled={busy}>
          {t('notificationAdmin.ruleModal.addEmail')}
        </Button>
      </fieldset>
    </Modal>
  )
}
