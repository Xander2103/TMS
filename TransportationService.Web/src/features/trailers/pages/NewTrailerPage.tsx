import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { FormField } from '../../../components/ui/FormField'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { computeVolumeM3 } from '../../../utils/volume'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { createTrailer } from '../api/trailersApi'
import {
  TRAILER_OWNERSHIP_LABELS,
  type TrailerInput,
  type TrailerOwnershipType,
} from '../types'
import './trailer-form.css'

const EMPTY: TrailerInput = {
  licensePlate: '',
  vin: null,
  categoryId: null,
  brand: null,
  model: null,
  year: null,
  firstRegistrationDate: null,
  capacityKg: null,
  lengthMeters: null,
  widthMeters: null,
  heightMeters: null,
  volumeM3: null,
  volumeIsManual: false,
  axleCount: 0,
  loadingMeters: 0,
  hasRefrigeration: false,
  adrSuitable: false,
  ownershipType: 'Owned',
  operationalStatus: 'Available',
  statusReason: null,
  isActive: true,
  notes: null,
}

export function NewTrailerPage() {
  const navigate = useNavigate()
  const { showSuccess } = useToast()
  const { options: categories } = useLookupOptions('/api/trailer-categories')
  const [form, setForm] = useState<TrailerInput>(EMPTY)
  const [error, setError] = useState<string | null>(null)
  const [yearError, setYearError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const currentYear = new Date().getFullYear()
  const autoVolume = computeVolumeM3(form.lengthMeters, form.widthMeters, form.heightMeters)

  function set<K extends keyof TrailerInput>(key: K, value: TrailerInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    if (!form.licensePlate.trim()) {
      setError('Kenteken is verplicht.')
      return
    }
    if (form.year !== null && form.year > currentYear) {
      setYearError(`Bouwjaar mag niet in de toekomst liggen (maximaal ${currentYear}).`)
      return
    }
    setYearError(null)
    setSubmitting(true)
    try {
      const trailer = await createTrailer(form)
      showSuccess(`Oplegger ${trailer.internalNumber} aangemaakt.`)
      navigate(`/trailers/${trailer.id}`)
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 409
          ? 'Er bestaat al een oplegger met dit kenteken.'
          : describeApiError(err, 'Oplegger kon niet worden aangemaakt.').message,
      )
      setSubmitting(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Opleggers', to: '/trailers' }, { label: 'Nieuw' }]} />
      <PageHeader title="Nieuwe oplegger" />
      <form className="trailer-form" onSubmit={handleSubmit} noValidate>
        {error && (
          <div className="trailer-form-error" role="alert">
            {error}
          </div>
        )}

        <section className="trailer-form-card">
          <h2>Basisgegevens</h2>
          <div className="trailer-form-grid">
            <FormField label="Kenteken" htmlFor="t-plate" required>
              <input id="t-plate" value={form.licensePlate} onChange={(e) => set('licensePlate', e.target.value)} disabled={submitting} />
            </FormField>
            <FormField label="Chassisnummer (VIN)" htmlFor="t-vin">
              <input id="t-vin" value={form.vin ?? ''} onChange={(e) => set('vin', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Categorie" htmlFor="t-category">
              <select id="t-category" value={form.categoryId ?? ''} onChange={(e) => set('categoryId', e.target.value || null)} disabled={submitting}>
                <option value="">— Geen —</option>
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Eigendomsvorm" htmlFor="t-ownership">
              <select id="t-ownership" value={form.ownershipType} onChange={(e) => set('ownershipType', e.target.value as TrailerOwnershipType)} disabled={submitting}>
                {Object.entries(TRAILER_OWNERSHIP_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Merk" htmlFor="t-brand">
              <input id="t-brand" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Model" htmlFor="t-model">
              <input id="t-model" value={form.model ?? ''} onChange={(e) => set('model', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Bouwjaar" htmlFor="t-year" error={yearError ?? undefined}>
              <input
                id="t-year"
                type="number"
                min={1900}
                max={currentYear}
                value={form.year ?? ''}
                onChange={(e) => set('year', e.target.value === '' ? null : Number(e.target.value))}
                aria-invalid={yearError ? 'true' : undefined}
                disabled={submitting}
              />
            </FormField>
            <FormField label="Eerste registratie" htmlFor="t-first-reg">
              <input id="t-first-reg" type="date" value={form.firstRegistrationDate ?? ''} onChange={(e) => set('firstRegistrationDate', e.target.value || null)} disabled={submitting} />
            </FormField>
          </div>
        </section>

        <section className="trailer-form-card">
          <h2>Capaciteit &amp; afmetingen</h2>
          <div className="trailer-form-grid">
            <FormField label="Aantal assen" htmlFor="t-axles" hint="Voor Maut/tolberekening.">
              <input id="t-axles" type="number" min={0} max={12} value={form.axleCount} onChange={(e) => set('axleCount', Number(e.target.value) || 0)} disabled={submitting} />
            </FormField>
            <FormField label="Laadmeters" htmlFor="t-ldm">
              <input id="t-ldm" type="number" min={0} step="0.01" value={form.loadingMeters} onChange={(e) => set('loadingMeters', Number(e.target.value) || 0)} disabled={submitting} />
            </FormField>
            <FormField label="Laadvermogen (kg)" htmlFor="t-capacity">
              <input id="t-capacity" type="number" step="0.01" value={form.capacityKg ?? ''} onChange={(e) => set('capacityKg', e.target.value === '' ? null : Number(e.target.value))} disabled={submitting} />
            </FormField>
            <FormField label="Lengte (m)" htmlFor="t-length">
              <input id="t-length" type="number" step="0.01" value={form.lengthMeters ?? ''} onChange={(e) => set('lengthMeters', e.target.value === '' ? null : Number(e.target.value))} disabled={submitting} />
            </FormField>
            <FormField label="Breedte (m)" htmlFor="t-width">
              <input id="t-width" type="number" step="0.01" value={form.widthMeters ?? ''} onChange={(e) => set('widthMeters', e.target.value === '' ? null : Number(e.target.value))} disabled={submitting} />
            </FormField>
            <FormField label="Hoogte (m)" htmlFor="t-height">
              <input id="t-height" type="number" step="0.01" value={form.heightMeters ?? ''} onChange={(e) => set('heightMeters', e.target.value === '' ? null : Number(e.target.value))} disabled={submitting} />
            </FormField>
            <FormField
              label="Volume (m³)"
              htmlFor="t-volume"
              hint={form.volumeIsManual ? 'Handmatige waarde; vink uit om opnieuw te berekenen.' : 'Automatisch berekend uit lengte × breedte × hoogte.'}
            >
              <input
                id="t-volume"
                type="number"
                min={0}
                step="0.001"
                value={form.volumeIsManual ? (form.volumeM3 ?? '') : (autoVolume ?? '')}
                onChange={(e) => set('volumeM3', e.target.value === '' ? null : Number(e.target.value))}
                disabled={submitting || !form.volumeIsManual}
              />
              <label className="trailer-checkbox">
                <input
                  type="checkbox"
                  checked={form.volumeIsManual}
                  onChange={(e) => {
                    set('volumeIsManual', e.target.checked)
                    if (e.target.checked) set('volumeM3', form.volumeM3 ?? autoVolume)
                  }}
                  disabled={submitting}
                />
                <span>Handmatig invullen</span>
              </label>
            </FormField>
          </div>
          <div className="trailer-form-checkboxes">
            <label className="trailer-checkbox">
              <input type="checkbox" checked={form.hasRefrigeration} onChange={(e) => set('hasRefrigeration', e.target.checked)} disabled={submitting} />
              <span>Koeling</span>
            </label>
            <label className="trailer-checkbox">
              <input type="checkbox" checked={form.adrSuitable} onChange={(e) => set('adrSuitable', e.target.checked)} disabled={submitting} />
              <span>ADR-geschikt</span>
            </label>
          </div>
        </section>

        <section className="trailer-form-card">
          <h2>Notities</h2>
          <textarea rows={3} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={submitting} />
        </section>

        <div className="trailer-form-actions">
          <Button type="button" variant="secondary" onClick={() => navigate('/trailers')} disabled={submitting}>
            Annuleren
          </Button>
          <Button type="submit" disabled={submitting}>
            {submitting ? 'Bezig…' : 'Oplegger aanmaken'}
          </Button>
        </div>
      </form>
    </div>
  )
}
