import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { FormField } from '../../../components/ui/FormField'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { searchCustomers } from '../../customers/api/customersApi'
import type { CustomerListItem } from '../../customers/types'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { createLocation } from '../api/locationsApi'
import { LOCATION_TYPE_LABELS, LOCATION_TYPES, type LocationInput, type LocationType } from '../types'
import './location-form.css'

const EMPTY: LocationInput = {
  code: '',
  name: '',
  type: 'Depot',
  street: null,
  houseNumber: null,
  postalCode: null,
  city: null,
  countryCode: null,
  latitude: null,
  longitude: null,
  contactName: null,
  contactPhone: null,
  contactEmail: null,
  openingHours: null,
  loadingInstructions: null,
  unloadingInstructions: null,
  accessInstructions: null,
  accessRestrictions: null,
  vehicleRestrictions: null,
  trailerRestrictions: null,
  alfapassRequired: false,
  appointmentRequired: false,
  isActive: true,
  customerId: null,
  notes: null,
  isDefaultLoadingLocation: false,
  isDefaultUnloadingLocation: false,
  isDefaultBillingLocation: false,
}

export function NewLocationPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const { showSuccess, showError } = useToast()
  // Arriving from a customer's Locations tab prefills the link and returns there afterwards.
  const prefilledCustomerId = searchParams.get('customerId')
  const [form, setForm] = useState<LocationInput>(
    prefilledCustomerId ? { ...EMPTY, customerId: prefilledCustomerId, type: 'CustomerLocation' } : EMPTY,
  )
  const [customers, setCustomers] = useState<CustomerListItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    let mounted = true
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => {
        if (mounted) setCustomers(result.items)
      })
      .catch(() => {
        if (mounted) showError('Klanten konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [showError])

  function set<K extends keyof LocationInput>(key: K, value: LocationInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    if (!form.code.trim() || !form.name.trim()) {
      setError('Code en naam zijn verplicht.')
      return
    }
    setSubmitting(true)
    try {
      const location = await createLocation(form)
      showSuccess(`Locatie ${location.code} aangemaakt.`)
      navigate(prefilledCustomerId ? `/customers/${prefilledCustomerId}` : `/locations/${location.id}`)
    } catch (err) {
      const message =
        err instanceof ApiError && err.status === 409
          ? 'Er bestaat al een locatie met deze code.'
          : 'Locatie kon niet worden aangemaakt.'
      setError(message)
      setSubmitting(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Locaties', to: '/locations' }, { label: 'Nieuw' }]} />
      <PageHeader title="Nieuwe locatie" />
      <form className="location-form" onSubmit={handleSubmit} noValidate>
        {error && (
          <div className="location-form-error" role="alert">
            {error}
          </div>
        )}

        <section className="location-form-card">
          <h2>Basisgegevens</h2>
          <div className="location-form-grid">
            <FormField label="Code" htmlFor="loc-code" required>
              <input id="loc-code" value={form.code} onChange={(e) => set('code', e.target.value)} disabled={submitting} maxLength={40} />
            </FormField>
            <FormField label="Naam" htmlFor="loc-name" required>
              <input id="loc-name" value={form.name} onChange={(e) => set('name', e.target.value)} disabled={submitting} maxLength={200} />
            </FormField>
            <FormField label="Type" htmlFor="loc-type" required>
              <select id="loc-type" value={form.type} onChange={(e) => set('type', e.target.value as LocationType)} disabled={submitting}>
                {LOCATION_TYPES.map((t) => (
                  <option key={t} value={t}>
                    {LOCATION_TYPE_LABELS[t]}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Klant" htmlFor="loc-customer">
              <select id="loc-customer" value={form.customerId ?? ''} onChange={(e) => set('customerId', e.target.value || null)} disabled={submitting}>
                <option value="">— Geen —</option>
                {customers.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </FormField>
          </div>
        </section>

        <section className="location-form-card">
          <h2>Adres</h2>
          <div className="location-form-grid">
            <FormField label="Straat" htmlFor="loc-street">
              <input id="loc-street" value={form.street ?? ''} onChange={(e) => set('street', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Nummer" htmlFor="loc-house">
              <input id="loc-house" value={form.houseNumber ?? ''} onChange={(e) => set('houseNumber', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Postcode" htmlFor="loc-postal">
              <input id="loc-postal" value={form.postalCode ?? ''} onChange={(e) => set('postalCode', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Plaats" htmlFor="loc-city">
              <input id="loc-city" value={form.city ?? ''} onChange={(e) => set('city', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Land" htmlFor="loc-country">
              <CountryCombobox id="loc-country" value={form.countryCode} onChange={(code) => set('countryCode', code)} disabled={submitting} />
            </FormField>
            <FormField label="Breedtegraad" htmlFor="loc-lat">
              <input
                id="loc-lat"
                type="number"
                step="0.000001"
                value={form.latitude ?? ''}
                onChange={(e) => set('latitude', e.target.value === '' ? null : Number(e.target.value))}
                disabled={submitting}
              />
            </FormField>
            <FormField label="Lengtegraad" htmlFor="loc-lng">
              <input
                id="loc-lng"
                type="number"
                step="0.000001"
                value={form.longitude ?? ''}
                onChange={(e) => set('longitude', e.target.value === '' ? null : Number(e.target.value))}
                disabled={submitting}
              />
            </FormField>
          </div>
        </section>

        <section className="location-form-card">
          <h2>Contact &amp; openingstijden</h2>
          <div className="location-form-grid">
            <FormField label="Contactpersoon" htmlFor="loc-contact-name">
              <input id="loc-contact-name" value={form.contactName ?? ''} onChange={(e) => set('contactName', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Telefoon" htmlFor="loc-contact-phone">
              <input id="loc-contact-phone" value={form.contactPhone ?? ''} onChange={(e) => set('contactPhone', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="E-mail" htmlFor="loc-contact-email">
              <input id="loc-contact-email" value={form.contactEmail ?? ''} onChange={(e) => set('contactEmail', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Openingstijden" htmlFor="loc-hours">
              <input id="loc-hours" value={form.openingHours ?? ''} onChange={(e) => set('openingHours', e.target.value || null)} disabled={submitting} placeholder="08:00-18:00" />
            </FormField>
          </div>
        </section>

        <section className="location-form-card">
          <h2>Instructies &amp; restricties</h2>
          <div className="location-form-grid location-form-grid-full">
            <FormField label="Laadinstructies" htmlFor="loc-loading">
              <textarea id="loc-loading" rows={2} value={form.loadingInstructions ?? ''} onChange={(e) => set('loadingInstructions', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Losinstructies" htmlFor="loc-unloading">
              <textarea id="loc-unloading" rows={2} value={form.unloadingInstructions ?? ''} onChange={(e) => set('unloadingInstructions', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Toegangsinstructies" htmlFor="loc-access">
              <textarea id="loc-access" rows={2} value={form.accessInstructions ?? ''} onChange={(e) => set('accessInstructions', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Toegangsrestricties" htmlFor="loc-access-restr">
              <input id="loc-access-restr" value={form.accessRestrictions ?? ''} onChange={(e) => set('accessRestrictions', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Voertuigrestricties" htmlFor="loc-vehicle-restr">
              <input id="loc-vehicle-restr" value={form.vehicleRestrictions ?? ''} onChange={(e) => set('vehicleRestrictions', e.target.value || null)} disabled={submitting} />
            </FormField>
            <FormField label="Opleggerrestricties" htmlFor="loc-trailer-restr">
              <input id="loc-trailer-restr" value={form.trailerRestrictions ?? ''} onChange={(e) => set('trailerRestrictions', e.target.value || null)} disabled={submitting} />
            </FormField>
          </div>
          <div className="location-form-checkboxes">
            <label className="location-checkbox">
              <input type="checkbox" checked={form.alfapassRequired} onChange={(e) => set('alfapassRequired', e.target.checked)} disabled={submitting} />
              <span>Alfapass vereist</span>
            </label>
            <label className="location-checkbox">
              <input type="checkbox" checked={form.appointmentRequired} onChange={(e) => set('appointmentRequired', e.target.checked)} disabled={submitting} />
              <span>Afspraak vereist</span>
            </label>
          </div>
        </section>

        <section className="location-form-card">
          <h2>Notities</h2>
          <textarea rows={3} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={submitting} />
        </section>

        <div className="location-form-actions">
          <Button type="button" variant="secondary" onClick={() => navigate('/locations')} disabled={submitting}>
            Annuleren
          </Button>
          <Button type="submit" disabled={submitting}>
            {submitting ? 'Bezig…' : 'Locatie aanmaken'}
          </Button>
        </div>
      </form>
    </div>
  )
}
