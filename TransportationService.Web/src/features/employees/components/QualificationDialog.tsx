import { useState, type ChangeEvent, type FormEvent } from 'react'
import { useQualificationTypes } from '../hooks/useQualificationTypes'
import { useQualificationMutations } from '../hooks/useQualificationMutations'
import type { EmployeeQualification } from '../types/qualification'
import './QualificationDialog.css'

interface QualificationDialogProps {
  employeeId: string
  onSaved: (qualification: EmployeeQualification) => void
  onCancel: () => void
}

export function QualificationDialog({ employeeId, onSaved, onCancel }: QualificationDialogProps) {
  const { qualificationTypes, isLoading: typesLoading } = useQualificationTypes()
  const { isSubmitting, error, create } = useQualificationMutations()

  const [qualificationTypeId, setQualificationTypeId] = useState('')
  const [documentNumber, setDocumentNumber] = useState('')
  const [obtainedDate, setObtainedDate] = useState('')
  const [expiryDate, setExpiryDate] = useState('')
  const [notes, setNotes] = useState('')
  const [validationError, setValidationError] = useState<string | null>(null)

  const selectedType = qualificationTypes.find((type) => type.id === qualificationTypeId)

  function handleTypeChange(event: ChangeEvent<HTMLSelectElement>) {
    setQualificationTypeId(event.target.value)
    setValidationError(null)
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!qualificationTypeId) {
      setValidationError('Kies een kwalificatietype.')
      return
    }
    if (!obtainedDate) {
      setValidationError('Behaaldatum is verplicht.')
      return
    }
    if (selectedType?.requiresExpiryDate && !expiryDate) {
      setValidationError('Vervaldatum is verplicht voor dit type.')
      return
    }

    const saved = await create(employeeId, {
      qualificationTypeId,
      documentNumber: documentNumber.trim() || null,
      obtainedDate,
      expiryDate: expiryDate || null,
      notes: notes.trim() || null,
    })

    if (saved) {
      onSaved(saved)
    }
  }

  return (
    <div className="qualification-dialog-overlay">
      <div className="qualification-dialog" role="dialog" aria-modal="true" aria-label="Kwalificatie toevoegen">
        <form onSubmit={handleSubmit} noValidate>
          <div className="form-field">
            <label htmlFor="qualificationType">Type</label>
            <select id="qualificationType" value={qualificationTypeId} onChange={handleTypeChange} disabled={typesLoading} required>
              <option value="">Kies een type...</option>
              {qualificationTypes.map((type) => (
                <option key={type.id} value={type.id}>
                  {type.name}
                </option>
              ))}
            </select>
          </div>

          <div className="form-field">
            <label htmlFor="documentNumber">Documentnummer</label>
            <input id="documentNumber" type="text" value={documentNumber} onChange={(e) => setDocumentNumber(e.target.value)} />
          </div>

          <div className="form-field">
            <label htmlFor="obtainedDate">Behaaldatum</label>
            <input id="obtainedDate" type="date" required value={obtainedDate} onChange={(e) => setObtainedDate(e.target.value)} />
          </div>

          {selectedType?.requiresExpiryDate && (
            <div className="form-field">
              <label htmlFor="expiryDate">Vervaldatum</label>
              <input id="expiryDate" type="date" required value={expiryDate} onChange={(e) => setExpiryDate(e.target.value)} />
            </div>
          )}

          <div className="form-field">
            <label htmlFor="qualificationNotes">Notities</label>
            <textarea id="qualificationNotes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
          </div>

          {(validationError || error) && (
            <p className="form-error" role="alert">
              {validationError ?? error}
            </p>
          )}

          <div className="form-actions">
            <button type="submit" className="primary-button" disabled={isSubmitting}>
              {isSubmitting ? 'Opslaan...' : 'Toevoegen'}
            </button>
            <button type="button" className="secondary-link" onClick={onCancel}>
              Annuleren
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
