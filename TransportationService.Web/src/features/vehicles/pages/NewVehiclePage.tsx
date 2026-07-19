import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { FormField } from '../../../components/ui/FormField'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { searchDrivers } from '../../drivers/api/driversApi'
import type { DriverListItem } from '../../drivers/types'
import { createVehicle } from '../api/vehiclesApi'
import {
  EMISSION_CLASS_LABELS,
  FUEL_TYPE_LABELS,
  OWNERSHIP_TYPE_LABELS,
  type CreateVehicleInput,
  type EmissionClass,
  type FuelType,
  type VehicleOwnershipType,
} from '../types'
import './vehicle-form.css'

const EMPTY: CreateVehicleInput = {
  licensePlate: '',
  vin: null,
  categoryId: null,
  brand: null,
  model: null,
  year: null,
  firstRegistrationDate: null,
  fuelType: 'Diesel',
  emissionClass: null,
  grossVehicleWeightKg: null,
  payloadKg: null,
  lengthMeters: null,
  widthMeters: null,
  heightMeters: null,
  volumeM3: null,
  odometerKm: 0,
  consumptionLPer100Km: null,
  hasCrane: false,
  hasRefrigeration: false,
  hasTailLift: false,
  adrSuitable: false,
  ownershipType: 'Owned',
  operationalStatus: 'Available',
  statusReason: null,
  isActive: true,
  fixedDriverId: null,
  currentDriverId: null,
  notes: null,
}

export function NewVehiclePage() {
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { options: categories } = useLookupOptions('/api/vehicle-categories')
  const [drivers, setDrivers] = useState<DriverListItem[]>([])
  const [form, setForm] = useState<CreateVehicleInput>(EMPTY)
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    let mounted = true
    searchDrivers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => {
        if (mounted) setDrivers(result.items)
      })
      .catch(() => {
        if (mounted) showError('Chauffeurs konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [showError])

  function set<K extends keyof CreateVehicleInput>(key: K, value: CreateVehicleInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    if (!form.licensePlate.trim()) {
      setError('Kenteken is verplicht.')
      return
    }
    setSubmitting(true)
    try {
      const vehicle = await createVehicle(form)
      showSuccess(`Voertuig ${vehicle.internalNumber} aangemaakt.`)
      navigate(`/vehicles/${vehicle.id}`)
    } catch (err) {
      setError(err instanceof ApiError && err.status === 409 ? 'Er bestaat al een voertuig met dit kenteken.' : 'Voertuig kon niet worden aangemaakt.')
      setSubmitting(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Voertuigen', to: '/vehicles' }, { label: 'Nieuw' }]} />
      <PageHeader title="Nieuw voertuig" />
      <form className="vehicle-form" onSubmit={handleSubmit} noValidate>
        {error && (
          <div className="vehicle-form-error" role="alert">
            {error}
          </div>
        )}

        <section className="vehicle-form-card">
          <h2>Basisgegevens</h2>
          <div className="vehicle-form-grid">
            <FormField label="Kenteken" htmlFor="v-plate" required>
              <input id="v-plate" value={form.licensePlate} onChange={(e) => set('licensePlate', e.target.value)} disabled={submitting} />
            </FormField>
            <FormField label="Chassisnummer (VIN)" htmlFor="v-vin">
              <input id="v-vin" value={form.vin ?? ''} onChange={(e) => set('vin', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Categorie" htmlFor="v-category">
              <select id="v-category" value={form.categoryId ?? ''} onChange={(e) => set('categoryId', e.target.value || null)} disabled={submitting}>
                <option value="">— Geen —</option>
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Merk" htmlFor="v-brand">
              <input id="v-brand" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Model" htmlFor="v-model">
              <input id="v-model" value={form.model ?? ''} onChange={(e) => set('model', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Bouwjaar" htmlFor="v-year">
              <input id="v-year" type="number" value={form.year ?? ''} onChange={(e) => set('year', e.target.value === '' ? null : Number(e.target.value))} disabled={submitting} />
            </FormField>
          </div>
        </section>

        <section className="vehicle-form-card">
          <h2>Techniek</h2>
          <div className="vehicle-form-grid">
            <FormField label="Brandstof" htmlFor="v-fuel">
              <select id="v-fuel" value={form.fuelType} onChange={(e) => set('fuelType', e.target.value as FuelType)} disabled={submitting}>
                {Object.entries(FUEL_TYPE_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Emissieklasse" htmlFor="v-emission">
              <select id="v-emission" value={form.emissionClass ?? ''} onChange={(e) => set('emissionClass', (e.target.value || null) as EmissionClass | null)} disabled={submitting}>
                <option value="">— Onbekend —</option>
                {Object.entries(EMISSION_CLASS_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Eigendomsvorm" htmlFor="v-ownership">
              <select id="v-ownership" value={form.ownershipType} onChange={(e) => set('ownershipType', e.target.value as VehicleOwnershipType)} disabled={submitting}>
                {Object.entries(OWNERSHIP_TYPE_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Kilometerstand" htmlFor="v-odometer">
              <input id="v-odometer" type="number" min={0} value={form.odometerKm} onChange={(e) => set('odometerKm', Number(e.target.value) || 0)} disabled={submitting} />
            </FormField>
            <FormField label="Normverbruik (l/100km)" htmlFor="v-consumption" hint="Voor kostenramingen; leeg = standaard uit de tarievenset.">
              <input
                id="v-consumption"
                type="number"
                min={0}
                step="0.1"
                value={form.consumptionLPer100Km ?? ''}
                onChange={(e) => set('consumptionLPer100Km', e.target.value === '' ? null : Number(e.target.value) || 0)}
                disabled={submitting}
              />
            </FormField>
          </div>
          <div className="vehicle-form-checkboxes">
            <label className="vehicle-checkbox">
              <input type="checkbox" checked={form.hasCrane} onChange={(e) => set('hasCrane', e.target.checked)} disabled={submitting} />
              <span>Kraan</span>
            </label>
            <label className="vehicle-checkbox">
              <input type="checkbox" checked={form.hasRefrigeration} onChange={(e) => set('hasRefrigeration', e.target.checked)} disabled={submitting} />
              <span>Koeling</span>
            </label>
            <label className="vehicle-checkbox">
              <input type="checkbox" checked={form.hasTailLift} onChange={(e) => set('hasTailLift', e.target.checked)} disabled={submitting} />
              <span>Laadklep</span>
            </label>
            <label className="vehicle-checkbox">
              <input type="checkbox" checked={form.adrSuitable} onChange={(e) => set('adrSuitable', e.target.checked)} disabled={submitting} />
              <span>ADR-geschikt</span>
            </label>
          </div>
        </section>

        <section className="vehicle-form-card">
          <h2>Chauffeur</h2>
          <div className="vehicle-form-grid">
            <FormField label="Vaste chauffeur" htmlFor="v-fixed-driver">
              <select id="v-fixed-driver" value={form.fixedDriverId ?? ''} onChange={(e) => set('fixedDriverId', e.target.value || null)} disabled={submitting}>
                <option value="">— Geen —</option>
                {drivers.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.fullName} ({d.driverNumber})
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Huidige chauffeur" htmlFor="v-current-driver">
              <select id="v-current-driver" value={form.currentDriverId ?? ''} onChange={(e) => set('currentDriverId', e.target.value || null)} disabled={submitting}>
                <option value="">— Geen —</option>
                {drivers.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.fullName} ({d.driverNumber})
                  </option>
                ))}
              </select>
            </FormField>
          </div>
        </section>

        <section className="vehicle-form-card">
          <h2>Notities</h2>
          <textarea rows={3} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={submitting} />
        </section>

        <div className="vehicle-form-actions">
          <Button type="button" variant="secondary" onClick={() => navigate('/vehicles')} disabled={submitting}>
            Annuleren
          </Button>
          <Button type="submit" disabled={submitting}>
            {submitting ? 'Bezig…' : 'Voertuig aanmaken'}
          </Button>
        </div>
      </form>
    </div>
  )
}
