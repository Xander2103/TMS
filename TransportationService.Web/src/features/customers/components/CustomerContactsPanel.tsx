import { useState, type FormEvent } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Badge } from '../../../components/ui/Badge'
import { EmptyState } from '../../../components/ui/EmptyState'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import type { CustomerContact, CustomerContactInput } from '../types'

interface CustomerContactsPanelProps {
  contacts: CustomerContact[]
  isSubmitting: boolean
  onAdd: (input: CustomerContactInput) => Promise<boolean>
  onUpdate: (contactId: string, input: CustomerContactInput) => Promise<boolean>
  onRemove: (contactId: string) => Promise<boolean>
}

type DialogState = { mode: 'create' } | { mode: 'edit'; contact: CustomerContact } | null

export function CustomerContactsPanel({ contacts, isSubmitting, onAdd, onUpdate, onRemove }: CustomerContactsPanelProps) {
  const [dialog, setDialog] = useState<DialogState>(null)
  const [removeTarget, setRemoveTarget] = useState<CustomerContact | null>(null)

  return (
    <div className="customer-contacts">
      <div className="page-header">
        <h3 style={{ margin: 0 }}>Contactpersonen</h3>
        <Button variant="secondary" onClick={() => setDialog({ mode: 'create' })}>
          Toevoegen
        </Button>
      </div>

      {contacts.length === 0 ? (
        <EmptyState message="Nog geen contactpersonen." />
      ) : (
        contacts.map((contact) => (
          <div key={contact.id} className="customer-contact-card">
            <div>
              <h4>
                {contact.firstName} {contact.lastName} {contact.isPrimary && <Badge tone="info">Primair</Badge>}
              </h4>
              <div className="customer-contact-meta">
                {[contact.role, contact.email, contact.phoneNumber].filter(Boolean).join(' · ') || '—'}
              </div>
            </div>
            <div className="customer-contact-actions">
              <Button variant="ghost" onClick={() => setDialog({ mode: 'edit', contact })}>
                Bewerken
              </Button>
              <Button variant="ghost" onClick={() => setRemoveTarget(contact)}>
                Verwijderen
              </Button>
            </div>
          </div>
        ))
      )}

      {dialog && (
        <ContactDialog
          contact={dialog.mode === 'edit' ? dialog.contact : undefined}
          isSubmitting={isSubmitting}
          onClose={() => setDialog(null)}
          onSubmit={async (input) => {
            const ok = dialog.mode === 'edit' ? await onUpdate(dialog.contact.id, input) : await onAdd(input)
            if (ok) setDialog(null)
          }}
        />
      )}

      {removeTarget && (
        <ConfirmDialog
          title="Contactpersoon verwijderen"
          message={`Weet u zeker dat u '${removeTarget.firstName} ${removeTarget.lastName}' wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          busy={isSubmitting}
          onConfirm={async () => {
            const ok = await onRemove(removeTarget.id)
            if (ok) setRemoveTarget(null)
          }}
          onCancel={() => setRemoveTarget(null)}
        />
      )}
    </div>
  )
}

function ContactDialog({
  contact,
  isSubmitting,
  onSubmit,
  onClose,
}: {
  contact?: CustomerContact
  isSubmitting: boolean
  onSubmit: (input: CustomerContactInput) => void
  onClose: () => void
}) {
  const [firstName, setFirstName] = useState(contact?.firstName ?? '')
  const [lastName, setLastName] = useState(contact?.lastName ?? '')
  const [role, setRole] = useState(contact?.role ?? '')
  const [email, setEmail] = useState(contact?.email ?? '')
  const [phoneNumber, setPhoneNumber] = useState(contact?.phoneNumber ?? '')
  const [isPrimary, setIsPrimary] = useState(contact?.isPrimary ?? false)
  const [notes, setNotes] = useState(contact?.notes ?? '')
  const [errors, setErrors] = useState<{ firstName?: string; lastName?: string }>({})

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const next: { firstName?: string; lastName?: string } = {}
    if (!firstName.trim()) next.firstName = 'Voornaam is verplicht.'
    if (!lastName.trim()) next.lastName = 'Achternaam is verplicht.'
    if (Object.keys(next).length > 0) {
      setErrors(next)
      return
    }
    onSubmit({
      firstName: firstName.trim(),
      lastName: lastName.trim(),
      role: role.trim() || null,
      email: email.trim() || null,
      phoneNumber: phoneNumber.trim() || null,
      isPrimary,
      notes: notes.trim() || null,
    })
  }

  return (
    <Modal
      title={contact ? 'Contactpersoon bewerken' : 'Nieuwe contactpersoon'}
      onClose={onClose}
      busy={isSubmitting}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>
            Annuleren
          </Button>
          <Button type="submit" form="contact-form" disabled={isSubmitting}>
            {isSubmitting ? 'Opslaan...' : 'Opslaan'}
          </Button>
        </>
      }
    >
      <form id="contact-form" onSubmit={handleSubmit} className="customer-form">
        <FormField label="Voornaam" htmlFor="ct-first" error={errors.firstName} required>
          <input id="ct-first" value={firstName} onChange={(e) => setFirstName(e.target.value)} aria-invalid={errors.firstName ? 'true' : undefined} maxLength={100} autoFocus />
        </FormField>
        <FormField label="Achternaam" htmlFor="ct-last" error={errors.lastName} required>
          <input id="ct-last" value={lastName} onChange={(e) => setLastName(e.target.value)} aria-invalid={errors.lastName ? 'true' : undefined} maxLength={100} />
        </FormField>
        <FormField label="Functie" htmlFor="ct-role">
          <input id="ct-role" value={role} onChange={(e) => setRole(e.target.value)} maxLength={100} />
        </FormField>
        <FormField label="E-mail" htmlFor="ct-email">
          <input id="ct-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} maxLength={250} />
        </FormField>
        <FormField label="Telefoon" htmlFor="ct-phone">
          <input id="ct-phone" value={phoneNumber} onChange={(e) => setPhoneNumber(e.target.value)} maxLength={30} />
        </FormField>
        <FormField label="Notities" htmlFor="ct-notes">
          <textarea id="ct-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} maxLength={1000} />
        </FormField>
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={isPrimary} onChange={(e) => setIsPrimary(e.target.checked)} />
          Primaire contactpersoon
        </label>
      </form>
    </Modal>
  )
}
