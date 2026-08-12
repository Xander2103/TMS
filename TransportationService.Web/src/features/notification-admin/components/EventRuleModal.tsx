import { useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { usePermissions } from '../../roles/hooks/usePermissions'
import { COMMUNICATION_TYPES, COMMUNICATION_TYPE_LABELS, type CustomerCommunicationType } from '../../customers/types'
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
        return 'Vul voor elke expliciete ontvanger een geldig e-mailadres in.'
      }
      if (RECIPIENT_TYPES_WITH_VALUE.includes(recipient.type) && recipient.type !== 'ExplicitEmail' && !value) {
        return `Kies een waarde voor elke ontvanger van het type "${RECIPIENT_TYPE_LABELS[recipient.type]}".`
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
      showSuccess('Meldingsregel opgeslagen.')
      onSaved()
    } catch (err) {
      setError(describeApiError(err, 'De meldingsregel kon niet worden opgeslagen.').message)
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
      title={`Meldingsregel — ${rule.label}`}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button onClick={() => void handleSave()} disabled={busy}>
            Opslaan
          </Button>
        </>
      }
    >
      {rule.peppolPending && (
        <p className="notification-admin-peppol-note">
          <Badge tone="warning">Nog niet actief</Badge> Peppol-koppeling is nog niet aangesloten; deze gebeurtenis
          wordt pas verstuurd zodra dat gekoppeld is. Ontvangers kun je alvast instellen.
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
          Actief
        </label>
        <label className="notification-admin-checkbox">
          <input type="checkbox" checked={inAppEnabled} onChange={(e) => setInAppEnabled(e.target.checked)} disabled={busy} />
          In-app
        </label>
        <label className="notification-admin-checkbox">
          <input type="checkbox" checked={emailEnabled} onChange={(e) => setEmailEnabled(e.target.checked)} disabled={busy} />
          E-mail
        </label>
        <label className="notification-admin-checkbox notification-admin-checkbox-disabled">
          <input type="checkbox" checked={false} disabled title="SMS is nog niet beschikbaar" />
          SMS (binnenkort)
        </label>
      </div>

      <label className="notification-admin-checkbox">
        <input
          type="checkbox"
          checked={allowCustomerOverride}
          onChange={(e) => setAllowCustomerOverride(e.target.checked)}
          disabled={busy}
        />
        Klanten mogen deze melding voor zichzelf uitschakelen (Klantafwijkingen)
      </label>

      <fieldset className="notification-admin-recipients">
        <legend>Ontvangers</legend>
        {recipients.length === 0 && <p className="placeholder-text">Geen ontvangers ingesteld — er wordt niets verstuurd.</p>}
        {recipients.map((recipient, index) => (
          <div key={index} className="notification-admin-recipient-row">
            <select
              aria-label={`Type ontvanger ${index + 1}`}
              value={recipient.type}
              disabled={busy}
              onChange={(e) => updateRecipient(index, { type: e.target.value as NotificationRecipientType, value: null })}
            >
              {RECIPIENT_TYPES.map((type) => (
                <option key={type} value={type}>
                  {RECIPIENT_TYPE_LABELS[type]}
                </option>
              ))}
            </select>

            {recipient.type === 'ExplicitEmail' && (
              <input
                aria-label={`E-mailadres ontvanger ${index + 1}`}
                type="email"
                placeholder="naam@bedrijf.be"
                value={recipient.value ?? ''}
                disabled={busy}
                onChange={(e) => updateRecipient(index, { value: e.target.value })}
              />
            )}

            {recipient.type === 'InternalPermission' && (
              <SearchableSelect
                ariaLabel={`Recht voor ontvanger ${index + 1}`}
                value={recipient.value}
                onChange={(value) => updateRecipient(index, { value })}
                options={permissionOptions}
                disabled={busy}
                placeholder="Kies een recht..."
              />
            )}

            {recipient.type === 'InternalRole' && (
              <span className="notification-admin-inline-field">
                <input
                  aria-label={`Rolcode ontvanger ${index + 1}`}
                  placeholder="bv. management, hr, planner"
                  title="Sjabloon-rolcode — zie Rollen & rechten voor de exacte code."
                  value={recipient.value ?? ''}
                  disabled={busy}
                  onChange={(e) => updateRecipient(index, { value: e.target.value })}
                />
              </span>
            )}

            {recipient.type === 'CustomerCommunicationRule' && (
              <select
                aria-label={`Communicatietype ontvanger ${index + 1}`}
                value={recipient.value ?? ''}
                disabled={busy}
                onChange={(e) => updateRecipient(index, { value: e.target.value })}
              >
                <option value="">— Kies een communicatietype —</option>
                {COMMUNICATION_TYPES.map((type: CustomerCommunicationType) => (
                  <option key={type} value={type}>
                    {COMMUNICATION_TYPE_LABELS[type]}
                  </option>
                ))}
              </select>
            )}

            {(recipient.type === 'CustomerPrimaryContact' || recipient.type === 'Driver') && (
              <span className="notification-admin-muted">Geen extra gegevens nodig.</span>
            )}

            <Button variant="ghost" onClick={() => removeRecipient(index)} disabled={busy}>
              Verwijderen
            </Button>
          </div>
        ))}
        <Button variant="secondary" onClick={addRecipient} disabled={busy}>
          + E-mailadres toevoegen
        </Button>
      </fieldset>
    </Modal>
  )
}
