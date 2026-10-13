import { useEffect, useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
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
      setError(t('edi.partnerModal.validation'))
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
      setError(localizeApiError(t, err, t('edi.partnerModal.saveFailed')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={partner ? t('edi.partnerModal.editTitle', { code: partner.code }) : t('edi.partnerModal.newTitle')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('edi.partnerModal.cancel')}
          </Button>
          <Button type="submit" form="edi-partner-form" disabled={busy}>
            {busy ? t('edi.partnerModal.busy') : t('edi.partnerModal.save')}
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
          <FormField label={t('edi.partnerModal.codeLabel')} htmlFor="edi-partner-code" required hint={t('edi.partnerModal.codeHint')}>
            <input id="edi-partner-code" value={code} maxLength={50} onChange={(e) => setCode(e.target.value)} disabled={busy} />
          </FormField>
        )}
        <FormField label={t('edi.partnerModal.nameLabel')} htmlFor="edi-partner-name" required>
          <input id="edi-partner-name" value={name} maxLength={200} onChange={(e) => setName(e.target.value)} disabled={busy} />
        </FormField>
        <FormField label={t('edi.partnerModal.customerLabel')} htmlFor="edi-partner-customer" hint={t('edi.partnerModal.customerHint')}>
          <SearchableSelect
            id="edi-partner-customer"
            value={customerId}
            onChange={setCustomerId}
            options={customers}
            placeholder={t('edi.partnerModal.customerPlaceholder')}
            disabled={busy}
            ariaLabel={t('edi.partnerModal.customerAria')}
          />
        </FormField>
        <FormField label={t('edi.partnerModal.externalLabel')} htmlFor="edi-partner-external" hint={t('edi.partnerModal.externalHint')}>
          <input
            id="edi-partner-external"
            value={externalCustomerIdentifier}
            maxLength={100}
            onChange={(e) => setExternalCustomerIdentifier(e.target.value)}
            disabled={busy}
          />
        </FormField>
        {partner && (
          <FormField label={t('edi.partnerModal.notesLabel')} htmlFor="edi-partner-notes">
            <input id="edi-partner-notes" value={notes} maxLength={1000} onChange={(e) => setNotes(e.target.value)} disabled={busy} />
          </FormField>
        )}
        <label className="tof-checkbox">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} disabled={busy} />
          {t('edi.partnerModal.active')}
        </label>
        <p className="edi-profile-note">
          {t('edi.partnerModal.profileActive')} <strong>{t('edi.partnerModal.profileName')}</strong>{' '}
          {t('edi.partnerModal.profileFollowUp')}
        </p>
      </form>
    </Modal>
  )
}
