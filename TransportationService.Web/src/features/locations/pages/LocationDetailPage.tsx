import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { ApiError } from '../../../api/apiClient'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { deleteLocation, getLocation, updateLocation } from '../api/locationsApi'
import { LOCATION_TYPE_LABELS, LOCATION_TYPES, type LocationDetail, type LocationInput, type LocationType } from '../types'
import './location-form.css'

export function LocationDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [location, setLocation] = useState<LocationDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<LocationInput | null>(null)
  const [saving, setSaving] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  useEffect(() => {
    if (!id) return
    let mounted = true
    getLocation(id)
      .then((result) => {
        if (!mounted) return
        setLocation(result)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Locatie kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

  const loading = location === null && loadError === null
  const canEdit = hasPermission('locations.edit')

  function startEdit() {
    if (!location) return
    setForm({
      code: location.code,
      name: location.name,
      type: location.type,
      street: location.street,
      houseNumber: location.houseNumber,
      postalCode: location.postalCode,
      city: location.city,
      countryCode: location.countryCode,
      latitude: location.latitude,
      longitude: location.longitude,
      contactName: location.contactName,
      contactPhone: location.contactPhone,
      contactEmail: location.contactEmail,
      openingHours: location.openingHours,
      loadingInstructions: location.loadingInstructions,
      unloadingInstructions: location.unloadingInstructions,
      accessInstructions: location.accessInstructions,
      accessRestrictions: location.accessRestrictions,
      vehicleRestrictions: location.vehicleRestrictions,
      trailerRestrictions: location.trailerRestrictions,
      alfapassRequired: location.alfapassRequired,
      appointmentRequired: location.appointmentRequired,
      isActive: location.isActive,
      customerId: location.customerId,
      notes: location.notes,
    })
    setEditing(true)
  }

  function set<K extends keyof LocationInput>(key: K, value: LocationInput[K]) {
    setForm((f) => (f ? { ...f, [key]: value } : f))
  }

  async function saveEdit() {
    if (!id || !form) return
    setSaving(true)
    try {
      const updated = await updateLocation(id, form)
      setLocation(updated)
      setEditing(false)
      showSuccess('Locatie bijgewerkt.')
    } catch (err) {
      showError(err instanceof ApiError && err.status === 409 ? 'Er bestaat al een locatie met deze code.' : 'Wijzigingen konden niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!id) return
    try {
      await deleteLocation(id)
      showSuccess('Locatie verwijderd.')
      navigate('/locations')
    } catch {
      showError('Locatie kon niet worden verwijderd.')
      setConfirmDelete(false)
    }
  }

  if (loading) return <p className="placeholder-text">Locatie laden…</p>
  if (loadError || !location) return <p className="placeholder-text">{loadError ?? 'Niet gevonden.'}</p>

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Locaties', to: '/locations' }, { label: location.code }]} />
      <PageHeader
        title={location.name}
        subtitle={`${location.code} · ${LOCATION_TYPE_LABELS[location.type]}`}
        action={
          <div className="location-detail-actions">
            {canEdit && !editing && <Button variant="secondary" onClick={startEdit}>Bewerken</Button>}
            {hasPermission('locations.delete') && <Button variant="danger" onClick={() => setConfirmDelete(true)}>Verwijderen</Button>}
          </div>
        }
      />

      <div className="location-detail-badges">
        {location.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>}
        {location.alfapassRequired && <Badge tone="warning">Alfapass vereist</Badge>}
        {location.appointmentRequired && <Badge tone="info">Afspraak vereist</Badge>}
        {location.customerName && <Badge tone="neutral">Klant: {location.customerName}</Badge>}
      </div>

      {!editing ? (
        <div className="location-detail-grid">
          <FormField label="Adres">
            <span>
              {[location.street, location.houseNumber].filter(Boolean).join(' ') || '—'}
              {location.postalCode || location.city ? `, ${[location.postalCode, location.city].filter(Boolean).join(' ')}` : ''}
              {location.countryCode ? ` (${location.countryCode})` : ''}
            </span>
          </FormField>
          <FormField label="Coördinaten">
            <span>{location.latitude != null && location.longitude != null ? `${location.latitude}, ${location.longitude}` : '—'}</span>
          </FormField>
          <FormField label="Contact">
            <span>{[location.contactName, location.contactPhone, location.contactEmail].filter(Boolean).join(' · ') || '—'}</span>
          </FormField>
          <FormField label="Openingstijden">
            <span>{location.openingHours ?? '—'}</span>
          </FormField>
          <FormField label="Laadinstructies" className="location-detail-full">
            <span>{location.loadingInstructions ?? '—'}</span>
          </FormField>
          <FormField label="Losinstructies" className="location-detail-full">
            <span>{location.unloadingInstructions ?? '—'}</span>
          </FormField>
          <FormField label="Toegangsinstructies" className="location-detail-full">
            <span>{location.accessInstructions ?? '—'}</span>
          </FormField>
          <FormField label="Restricties" className="location-detail-full">
            <span>
              {[location.accessRestrictions, location.vehicleRestrictions, location.trailerRestrictions].filter(Boolean).join(' · ') || '—'}
            </span>
          </FormField>
          <FormField label="Notities" className="location-detail-full">
            <span>{location.notes ?? '—'}</span>
          </FormField>
        </div>
      ) : (
        form && (
          <form
            className="location-form"
            onSubmit={(e) => {
              e.preventDefault()
              void saveEdit()
            }}
          >
            <section className="location-form-card">
              <h2>Basisgegevens</h2>
              <div className="location-form-grid">
                <FormField label="Code" htmlFor="e-code" required>
                  <input id="e-code" value={form.code} onChange={(e) => set('code', e.target.value)} disabled={saving} />
                </FormField>
                <FormField label="Naam" htmlFor="e-name" required>
                  <input id="e-name" value={form.name} onChange={(e) => set('name', e.target.value)} disabled={saving} />
                </FormField>
                <FormField label="Type" htmlFor="e-type">
                  <select id="e-type" value={form.type} onChange={(e) => set('type', e.target.value as LocationType)} disabled={saving}>
                    {LOCATION_TYPES.map((t) => (
                      <option key={t} value={t}>
                        {LOCATION_TYPE_LABELS[t]}
                      </option>
                    ))}
                  </select>
                </FormField>
                <FormField label="Actief" htmlFor="e-active">
                  <label className="location-checkbox">
                    <input id="e-active" type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} disabled={saving} />
                    <span>Locatie is actief</span>
                  </label>
                </FormField>
              </div>
            </section>

            <section className="location-form-card">
              <h2>Adres &amp; coördinaten</h2>
              <div className="location-form-grid">
                <FormField label="Straat" htmlFor="e-street">
                  <input id="e-street" value={form.street ?? ''} onChange={(e) => set('street', e.target.value || null)} disabled={saving} />
                </FormField>
                <FormField label="Nummer" htmlFor="e-house">
                  <input id="e-house" value={form.houseNumber ?? ''} onChange={(e) => set('houseNumber', e.target.value || null)} disabled={saving} />
                </FormField>
                <FormField label="Postcode" htmlFor="e-postal">
                  <input id="e-postal" value={form.postalCode ?? ''} onChange={(e) => set('postalCode', e.target.value || null)} disabled={saving} />
                </FormField>
                <FormField label="Plaats" htmlFor="e-city">
                  <input id="e-city" value={form.city ?? ''} onChange={(e) => set('city', e.target.value || null)} disabled={saving} />
                </FormField>
                <FormField label="Land" htmlFor="e-country">
                  <CountryCombobox id="e-country" value={form.countryCode} onChange={(code) => set('countryCode', code)} disabled={saving} />
                </FormField>
              </div>
            </section>

            <section className="location-form-card">
              <h2>Restricties</h2>
              <div className="location-form-checkboxes">
                <label className="location-checkbox">
                  <input type="checkbox" checked={form.alfapassRequired} onChange={(e) => set('alfapassRequired', e.target.checked)} disabled={saving} />
                  <span>Alfapass vereist</span>
                </label>
                <label className="location-checkbox">
                  <input type="checkbox" checked={form.appointmentRequired} onChange={(e) => set('appointmentRequired', e.target.checked)} disabled={saving} />
                  <span>Afspraak vereist</span>
                </label>
              </div>
            </section>

            <div className="location-form-actions">
              <Button type="button" variant="secondary" onClick={() => setEditing(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </div>
          </form>
        )
      )}

      {confirmDelete && (
        <ConfirmDialog
          title="Locatie verwijderen"
          message={`Weet je zeker dat je locatie ${location.code} wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}
