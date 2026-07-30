import { useEffect, useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { describeApiError } from '../../../api/problemDetails'
import { searchCustomers } from '../../customers/api/customersApi'
import { createPartner, updatePartner, type EdiPartner } from '../api/ediApi'

interface PartnerModalProps {
  /** Null creates a new partner; otherwise edits the given one (code is immutable after creation). */
  partner: EdiPartner | null
  onClose: () => void
  onSaved: () => void
}

/** Create/edit modal for a trading partner — code+naam+klant on creation; naam/klant/externe
 * code/profiel/notities/actief afterwards. Never inline-embedded in the partner list. */
export function PartnerModal({ partner, onClose, onSaved }: PartnerModalProps) {
  const [code, setCode] = useState(partner?.code ?? '')
  const [name, setName] = useState(partner?.name ?? '')
  const [customerId, setCustomerId] = useState<string | null>(partner?.customerId ?? null)
  const [externalCustomerIdentifier, setExternalCustomerIdentifier] = useState(partner?.externalCustomerIdentifier ?? '')
  const [mappingProfile] = useState(partner?.mappingProfile ?? 'generic-json-v1')
  const [notes, setNotes] = useState(partner?.notes ?? '')
  const [isActive, setIsActive] = useState(partner?.isActive ?? true)
  const [customers, setCustomers] = useState<SearchableSelectOption[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((data) => {
        if (mounted) setCustomers(data.items.map((c) => ({ value: c.id, label: `${c.name} (${c.customerNumber})` })))
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!name.trim() || (!partner && !code.trim())) {
      setError('Code en naam zijn verplicht.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      if (partner) {
        await updatePartner(partner.id, {
          name: name.trim(),
          customerId,
          externalCustomerIdentifier: externalCustomerIdentifier.trim() || null,
          mappingProfile,
          isActive,
          notes: notes.trim() || null,
        })
      } else {
        await createPartner({
          code: code.trim(),
          name: name.trim(),
          customerId,
          externalCustomerIdentifier: externalCustomerIdentifier.trim() || null,
          isActive,
        })
      }
      onSaved()
    } catch (err) {
      setError(describeApiError(err, 'De partner kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={partner ? `Handelspartner bewerken — ${partner.code}` : 'Nieuwe handelspartner'}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button type="submit" form="edi-partner-form" disabled={busy}>
            {busy ? 'Bezig...' : 'Opslaan'}
          </Button>
        </>
      }
    >
      <form id="edi-partner-form" onSubmit={submit} noValidate>
        {error && (
          <div role="alert" className="issued-items-form-error">
            {error}
          </div>
        )}
        {!partner && (
          <FormField label="Code" htmlFor="edi-partner-code" required hint="Technische partnercode, gebruikt in het inbound-endpoint.">
            <input id="edi-partner-code" value={code} maxLength={50} onChange={(e) => setCode(e.target.value)} disabled={busy} />
          </FormField>
        )}
        <FormField label="Naam" htmlFor="edi-partner-name" required>
          <input id="edi-partner-name" value={name} maxLength={200} onChange={(e) => setName(e.target.value)} disabled={busy} />
        </FormField>
        <FormField label="Klant" htmlFor="edi-partner-customer" hint="Onze klant waarvoor deze partner orders aanlevert.">
          <SearchableSelect
            id="edi-partner-customer"
            value={customerId}
            onChange={setCustomerId}
            options={customers}
            placeholder="— Geen klant gekoppeld —"
            disabled={busy}
            ariaLabel="Klant"
          />
        </FormField>
        <FormField label="Externe klantcode" htmlFor="edi-partner-external" hint="De identifier die deze partner zelf voor de klant gebruikt.">
          <input
            id="edi-partner-external"
            value={externalCustomerIdentifier}
            maxLength={100}
            onChange={(e) => setExternalCustomerIdentifier(e.target.value)}
            disabled={busy}
          />
        </FormField>
        {partner && (
          <FormField label="Notities" htmlFor="edi-partner-notes">
            <input id="edi-partner-notes" value={notes} maxLength={1000} onChange={(e) => setNotes(e.target.value)} disabled={busy} />
          </FormField>
        )}
        <label className="tof-checkbox">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} disabled={busy} />
          Actief
        </label>
        <p className="edi-profile-note">
          Actief profiel: <strong>Generiek JSON</strong> — partnerspecifieke formaten volgen zodra er een specificatie is.
        </p>
      </form>
    </Modal>
  )
}
