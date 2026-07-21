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
import {
  createCommunicationRule,
  deleteCommunicationRule,
  listCommunicationRules,
  updateCommunicationRule,
} from '../api/customerCommunicationApi'
import {
  COMMUNICATION_TYPES,
  COMMUNICATION_TYPE_LABELS,
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
  const { hasPermission } = useAuth()
  const canManage = hasPermission('customers.manage_communication')

  const [rules, setRules] = useState<CustomerCommunicationRule[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [dialog, setDialog] = useState<DialogState>(null)
  const [removeTarget, setRemoveTarget] = useState<CustomerCommunicationRule | null>(null)
  const [busy, setBusy] = useState(false)

  const contactsById = useMemo(() => new Map(contacts.map((contact) => [contact.id, contact])), [contacts])

  const reload = useCallback(() => {
    listCommunicationRules(customerId)
      .then((data) => {
        setRules(data)
        setLoadError(null)
      })
      .catch(() => setLoadError('De communicatieregels konden niet worden geladen.'))
  }, [customerId])

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
      header: 'Type',
      render: (rule) => communicationTypeLabel(rule.type, rule.customTypeLabel),
    },
    { key: 'contacts', header: 'Contactpersonen', render: (rule) => describeContacts(rule) },
    { key: 'cc', header: 'CC', render: (rule) => rule.ccEmail ?? '—' },
    { key: 'language', header: 'Taal', render: (rule) => rule.languageCode ?? '—' },
    {
      key: 'active',
      header: 'Status',
      render: (rule) =>
        rule.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>,
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: 'Acties',
            render: (rule: CustomerCommunicationRule) => (
              <span className="customer-contact-actions">
                <Button variant="ghost" onClick={() => setDialog({ mode: 'edit', rule })}>
                  Bewerken
                </Button>
                <Button variant="ghost" onClick={() => setRemoveTarget(rule)}>
                  Verwijderen
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
        toast.showSuccess('Communicatieregel bijgewerkt.')
      } else {
        await createCommunicationRule(customerId, input)
        toast.showSuccess('Communicatieregel toegevoegd.')
      }
      setDialog(null)
      reload()
      return { ok: true }
    } catch (err) {
      const described = describeApiError(err, 'De communicatieregel kon niet worden opgeslagen.')
      return { ok: false, error: described.message, fieldErrors: described.fieldErrors }
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="customer-contacts">
      <div className="page-header">
        <h3 style={{ margin: 0 }}>Communicatieregels</h3>
        {canManage && (
          <Button variant="secondary" onClick={() => setDialog({ mode: 'create' })}>
            + Regel toevoegen
          </Button>
        )}
      </div>

      <DataTable
        columns={columns}
        rows={rules ?? []}
        rowKey={(rule) => rule.id}
        isLoading={rules === null && loadError === null}
        error={loadError}
        emptyMessage="Nog geen communicatieregels. Zonder regels worden er geen meldingen naar deze klant gestuurd."
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
          title="Communicatieregel verwijderen"
          message={`Regel '${communicationTypeLabel(removeTarget.type, removeTarget.customTypeLabel)}' verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          busy={busy}
          onConfirm={async () => {
            setBusy(true)
            try {
              await deleteCommunicationRule(customerId, removeTarget.id)
              toast.showSuccess('Communicatieregel verwijderd.')
              setRemoveTarget(null)
              reload()
            } catch (err) {
              toast.showError(describeApiError(err, 'De communicatieregel kon niet worden verwijderd.').message)
            } finally {
              setBusy(false)
            }
          }}
          onCancel={() => setRemoveTarget(null)}
        />
      )}
    </div>
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
    if (contactIds.length === 0) next.contactIds = 'Koppel minstens één contactpersoon aan deze regel.'
    if (type === 'Other' && !customTypeLabel.trim()) next.customTypeLabel = "Geef een omschrijving voor het type 'Andere'."
    if (ccEmail.trim() && !ccEmail.includes('@')) next.ccEmail = 'Het CC-adres is geen geldig e-mailadres.'
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
      title={rule ? 'Communicatieregel bewerken' : 'Nieuwe communicatieregel'}
      onClose={onClose}
      busy={isSubmitting}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>
            Annuleren
          </Button>
          <Button type="submit" form="communication-rule-form" disabled={isSubmitting}>
            {isSubmitting ? 'Opslaan...' : 'Opslaan'}
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
        <FormField label="Type" htmlFor="comm-type" required>
          <select id="comm-type" value={type} onChange={(e) => setType(e.target.value as CustomerCommunicationType)}>
            {COMMUNICATION_TYPES.map((value) => (
              <option key={value} value={value}>
                {COMMUNICATION_TYPE_LABELS[value]}
              </option>
            ))}
          </select>
        </FormField>
        {type === 'Other' && (
          <FormField
            label="Omschrijving type"
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
          label="Contactpersonen"
          htmlFor="comm-contacts"
          required
          hint="De actieve gekoppelde contactpersonen ontvangen dit type melding."
          error={localErrors.contactIds ?? getFieldError(serverFieldErrors, 'contactIds')}
        >
          <div id="comm-contacts" className="customer-comm-contact-list" role="group" aria-label="Contactpersonen">
            {selectable.length === 0 && (
              <p className="customer-form-muted">Deze klant heeft nog geen contactpersonen.</p>
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
                {!contact.isActive ? ' — inactief' : ''}
              </label>
            ))}
          </div>
        </FormField>
        <FormField
          label="CC-e-mail"
          htmlFor="comm-cc"
          hint="Optioneel extra adres dat elke melding in CC krijgt."
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
        <FormField label="Taal" htmlFor="comm-language" hint="Taalcode, bv. nl, fr of en. Leeg = voorkeurstaal van de contactpersoon.">
          <input id="comm-language" value={languageCode} onChange={(e) => setLanguageCode(e.target.value)} maxLength={10} />
        </FormField>
        <FormField
          label="Fallback-contactpersoon"
          htmlFor="comm-fallback"
          hint="Ontvangt de melding wanneer geen enkele gekoppelde contactpersoon bruikbaar is."
        >
          <select id="comm-fallback" value={fallbackContactId} onChange={(e) => setFallbackContactId(e.target.value)}>
            <option value="">— Geen —</option>
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
          Actief
        </label>
      </form>
    </Modal>
  )
}
