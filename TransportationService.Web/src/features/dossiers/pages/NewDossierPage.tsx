import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { BackButton } from '../../../components/ui/BackButton'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { searchCustomers } from '../../customers/api/customersApi'
import { listActivityTypes, type ActivityType } from '../api/activityTypesApi'
import { activityTypeIcon, DEFAULT_ACTIVITY_TYPE_ICON } from '../activityTypeIcons'
import { createDossierFast } from '../api/dossiersApi'
import './new-dossier.css'

/**
 * §8 fast create — the ONLY asked fields are klant (required), referentie, datum and a
 * template tile. Everything else (route, goederen, prijs, entiteit) komt daarna op het
 * dossier zelf; readiness begeleidt de vervollediging.
 */
export function NewDossierPage() {
  const navigate = useNavigate()
  const toast = useToast()

  const [customerId, setCustomerId] = useState<string | null>(null)
  const [customerReference, setCustomerReference] = useState('')
  const [dossierDate, setDossierDate] = useState(() => new Date().toISOString().slice(0, 10))
  /** null = "Leeg dossier" (default). */
  const [activityTypeId, setActivityTypeId] = useState<string | null>(null)
  const [customerOptions, setCustomerOptions] = useState<SearchableSelectOption[]>([])
  const [quickStartTypes, setQuickStartTypes] = useState<ActivityType[]>([])
  const [busy, setBusy] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})

  useEffect(() => {
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => setCustomerOptions(result.items.map((c) => ({ value: c.id, label: c.name }))))
      .catch(() => setCustomerOptions([]))
    listActivityTypes()
      .then((types) =>
        setQuickStartTypes(
          types
            .filter((t) => t.isActive && t.isQuickStart)
            .sort((a, b) => a.quickStartOrder - b.quickStartOrder || a.sortOrder - b.sortOrder),
        ),
      )
      .catch(() => setQuickStartTypes([]))
    // Autofocus the customer combobox: picking the klant is the whole job here.
    document.getElementById('nd-klant')?.focus()
  }, [])

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!customerId) {
      setFormError('Selecteer een klant om het dossier aan te maken.')
      return
    }
    setBusy(true)
    setFormError(null)
    setFieldErrors({})
    try {
      const created = await createDossierFast({
        customerId,
        dossierDate: dossierDate || null,
        customerReference: customerReference.trim() || null,
        activityTypeId,
      })
      toast.showSuccess(`Dossier ${created.dossierNumber} aangemaakt.`)
      navigate(`/dossiers/${created.id}`)
    } catch (err) {
      const described = describeApiError(err, 'Het dossier kon niet worden aangemaakt.')
      setFormError(described.message)
      setFieldErrors(described.fieldErrors)
    } finally {
      setBusy(false)
    }
  }

  const EmptyIcon = DEFAULT_ACTIVITY_TYPE_ICON

  return (
    <div className="new-dossier">
      <BackButton to="/dossiers" label="Terug naar dossiers" />
      <PageHeader title="Nieuw dossier" subtitle="Alleen de klant is verplicht; de rest vul je aan op het dossier." />

      <form onSubmit={(event) => void submit(event)} noValidate>
        <ValidationSummary message={formError} fieldErrors={fieldErrors} />

        <FormField label="Klant" htmlFor="nd-klant" required error={getFieldError(fieldErrors, 'customerId')}>
          <SearchableSelect
            id="nd-klant"
            value={customerId}
            onChange={setCustomerId}
            options={customerOptions}
            placeholder="Zoek een klant…"
            disabled={busy}
          />
        </FormField>

        <div className="new-dossier-row">
          <FormField label="Klantreferentie" htmlFor="nd-ref" error={getFieldError(fieldErrors, 'customerReference')}>
            <input
              id="nd-ref"
              value={customerReference}
              onChange={(event) => setCustomerReference(event.target.value)}
              maxLength={100}
              disabled={busy}
            />
          </FormField>
          <FormField label="Datum" htmlFor="nd-datum" error={getFieldError(fieldErrors, 'dossierDate')}>
            <input
              id="nd-datum"
              type="date"
              value={dossierDate}
              onChange={(event) => setDossierDate(event.target.value)}
              disabled={busy}
            />
          </FormField>
        </div>

        <fieldset className="new-dossier-templates">
          <legend>Sjabloon</legend>
          <div className="new-dossier-tiles" role="radiogroup" aria-label="Sjabloon">
            <button
              type="button"
              role="radio"
              aria-checked={activityTypeId === null}
              className={`new-dossier-tile${activityTypeId === null ? ' new-dossier-tile-selected' : ''}`}
              onClick={() => setActivityTypeId(null)}
              disabled={busy}
            >
              <EmptyIcon size={20} aria-hidden="true" />
              <span>Leeg dossier</span>
            </button>
            {quickStartTypes.map((type) => {
              const Icon = activityTypeIcon(type.icon)
              const selected = activityTypeId === type.id
              return (
                <button
                  key={type.id}
                  type="button"
                  role="radio"
                  aria-checked={selected}
                  className={`new-dossier-tile${selected ? ' new-dossier-tile-selected' : ''}`}
                  onClick={() => setActivityTypeId(type.id)}
                  disabled={busy}
                >
                  <Icon size={20} aria-hidden="true" />
                  <span>{type.name}</span>
                </button>
              )
            })}
          </div>
        </fieldset>

        <div className="new-dossier-actions">
          <Button type="submit" disabled={busy || !customerId}>
            {busy ? 'Aanmaken…' : 'Dossier aanmaken'}
          </Button>
        </div>
      </form>
    </div>
  )
}
