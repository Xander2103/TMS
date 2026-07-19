import { useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { useQualificationTypes } from '../hooks/useQualificationTypes'
import { useQualificationMutations } from '../hooks/useQualificationMutations'
import type { EmployeeQualification } from '../types/qualification'

interface QualificationDialogProps {
  employeeId: string
  /** When set, the dialog edits this qualification instead of creating a new one. */
  existing?: EmployeeQualification
  onSaved: (qualification: EmployeeQualification) => void
  onCancel: () => void
}

export function QualificationDialog({ employeeId, existing, onSaved, onCancel }: QualificationDialogProps) {
  const { qualificationTypes, isLoading: typesLoading } = useQualificationTypes()
  const { isSubmitting, error, create, update } = useQualificationMutations()

  const [qualificationTypeId, setQualificationTypeId] = useState<string | null>(existing?.qualificationTypeId ?? null)
  const [documentNumber, setDocumentNumber] = useState(existing?.documentNumber ?? '')
  const [obtainedDate, setObtainedDate] = useState(existing?.obtainedDate ?? '')
  const [expiryDate, setExpiryDate] = useState(existing?.expiryDate ?? '')
  const [issuingCountryCode, setIssuingCountryCode] = useState<string | null>(existing?.issuingCountryCode ?? null)
  const [notes, setNotes] = useState(existing?.notes ?? '')
  const [validationError, setValidationError] = useState<string | null>(null)

  const selectedType = qualificationTypes.find((type) => type.id === qualificationTypeId)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!existing && !qualificationTypeId) {
      setValidationError('Kies een kwalificatietype.')
      return
    }
    if (!obtainedDate) {
      setValidationError('Behaal-/uitgiftedatum is verplicht.')
      return
    }
    if (selectedType?.requiresExpiryDate && !expiryDate) {
      setValidationError('Vervaldatum is verplicht voor dit type.')
      return
    }
    setValidationError(null)

    const payload = {
      documentNumber: documentNumber.trim() || null,
      obtainedDate,
      expiryDate: expiryDate || null,
      notes: notes.trim() || null,
      issuingCountryCode,
    }

    const saved = existing
      ? await update(employeeId, existing.id, payload)
      : await create(employeeId, { ...payload, qualificationTypeId: qualificationTypeId! })

    if (saved) {
      onSaved(saved)
    }
  }

  return (
    <Modal
      title={existing ? `${existing.qualificationTypeName} bewerken` : 'Kwalificatie toevoegen'}
      onClose={onCancel}
      busy={isSubmitting}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={isSubmitting}>
            Annuleren
          </Button>
          <Button type="submit" form="qualification-form" disabled={isSubmitting}>
            {isSubmitting ? 'Opslaan…' : existing ? 'Opslaan' : 'Toevoegen'}
          </Button>
        </>
      }
    >
      <form id="qualification-form" onSubmit={handleSubmit} noValidate>
        {(validationError || error) && (
          <p className="ui-form-field-error" role="alert">
            {validationError ?? error}
          </p>
        )}

        {!existing && (
          <FormField label="Type" htmlFor="qualificationType" required>
            <SearchableSelect
              id="qualificationType"
              value={qualificationTypeId}
              onChange={setQualificationTypeId}
              options={qualificationTypes.map((type) => ({
                value: type.id,
                label: type.name,
                description: type.category,
                keywords: type.code,
              }))}
              isLoading={typesLoading}
              placeholder="Kies een type…"
            />
          </FormField>
        )}

        <FormField label="Document-/licentienummer" htmlFor="documentNumber">
          <input id="documentNumber" type="text" value={documentNumber} onChange={(e) => setDocumentNumber(e.target.value)} maxLength={100} />
        </FormField>

        <FormField label="Behaal-/uitgiftedatum" htmlFor="obtainedDate" required>
          <input id="obtainedDate" type="date" value={obtainedDate} onChange={(e) => setObtainedDate(e.target.value)} />
        </FormField>

        <FormField
          label="Vervaldatum"
          htmlFor="expiryDate"
          required={selectedType?.requiresExpiryDate}
          hint={selectedType?.requiresExpiryDate ? 'Verplicht voor dit type.' : 'Leeg = geen vervaldatum.'}
        >
          <input id="expiryDate" type="date" value={expiryDate} onChange={(e) => setExpiryDate(e.target.value)} />
        </FormField>

        <FormField label="Uitgifteland" htmlFor="issuingCountry">
          <CountryCombobox id="issuingCountry" value={issuingCountryCode} onChange={setIssuingCountryCode} placeholder="— Onbekend —" />
        </FormField>

        <FormField label="Notities" htmlFor="qualificationNotes">
          <textarea id="qualificationNotes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} />
        </FormField>
      </form>
    </Modal>
  )
}
