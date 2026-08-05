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
import { deleteLocation, duplicateLocation, getLocation, updateLocation } from '../api/locationsApi'
import { LocationForm } from '../components/LocationForm'
import { formatOpeningIntervals } from '../openingHours'
import { LOCATION_TYPE_LABELS, type LocationDetail, type LocationInput } from '../types'
import './location-form.css'

/** Full detail → form payload; every field round-trips so saving never clears untouched data. */
function toInput(location: LocationDetail): LocationInput {
  return {
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
    contactMobile: location.contactMobile,
    contactEmail: location.contactEmail,
    customerContactId: location.customerContactId,
    externalReference: location.externalReference,
    openingHours: location.openingHours,
    // null would clear the intervals server-side, so the form always carries the full list.
    openingIntervals: location.openingIntervals ?? [],
    loadingInstructions: location.loadingInstructions,
    unloadingInstructions: location.unloadingInstructions,
    accessInstructions: location.accessInstructions,
    accessRestrictions: location.accessRestrictions,
    vehicleRestrictions: location.vehicleRestrictions,
    trailerRestrictions: location.trailerRestrictions,
    alfapassRequired: location.alfapassRequired,
    appointmentRequired: location.appointmentRequired,
    gate: location.gate,
    accessCode: location.accessCode ?? null,
    receptionPoint: location.receptionPoint,
    dock: location.dock,
    routeDescription: location.routeDescription,
    deliveryByAppointmentOnly: location.deliveryByAppointmentOnly,
    heightRestrictionMeters: location.heightRestrictionMeters,
    weightRestrictionTons: location.weightRestrictionTons,
    adrAllowed: location.adrAllowed,
    craneRequired: location.craneRequired,
    forkliftAvailable: location.forkliftAvailable,
    driverInstructions: location.driverInstructions,
    internalMemo: location.internalMemo,
    defaultLoadingMinutes: location.defaultLoadingMinutes,
    defaultUnloadingMinutes: location.defaultUnloadingMinutes,
    preferredArrivalFrom: location.preferredArrivalFrom,
    preferredArrivalTo: location.preferredArrivalTo,
    earliestArrival: location.earliestArrival,
    latestArrival: location.latestArrival,
    isActive: location.isActive,
    customerId: location.customerId,
    notes: location.notes,
    // Carried through so saving an edit never silently clears the customer defaults.
    isDefaultLoadingLocation: location.isDefaultLoadingLocation,
    isDefaultUnloadingLocation: location.isDefaultUnloadingLocation,
    isDefaultBillingLocation: location.isDefaultBillingLocation,
  }
}

export function LocationDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [location, setLocation] = useState<LocationDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [duplicating, setDuplicating] = useState(false)

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

  async function saveEdit(value: LocationInput) {
    if (!id) return
    setSaving(true)
    setSaveError(null)
    try {
      const updated = await updateLocation(id, value)
      setLocation(updated)
      setEditing(false)
      showSuccess('Locatie bijgewerkt.')
    } catch (err) {
      setSaveError(
        err instanceof ApiError && err.status === 409
          ? 'Er bestaat al een locatie met deze code.'
          : 'Wijzigingen konden niet worden opgeslagen.',
      )
    } finally {
      setSaving(false)
    }
  }

  async function handleDuplicate() {
    if (!id) return
    setDuplicating(true)
    try {
      const copy = await duplicateLocation(id)
      showSuccess('Locatie gedupliceerd.')
      navigate(`/locations/${copy.id}`)
      // The route param changes, so the effect reloads the copy; reset local view state.
      setLocation(null)
      setEditing(false)
    } catch {
      showError('Locatie kon niet worden gedupliceerd.')
    } finally {
      setDuplicating(false)
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

  const openingLines = formatOpeningIntervals(location.openingIntervals ?? [])
  const restrictionParts = [
    location.heightRestrictionMeters != null ? `max. hoogte ${location.heightRestrictionMeters} m` : null,
    location.weightRestrictionTons != null ? `max. gewicht ${location.weightRestrictionTons} t` : null,
    location.adrAllowed === true ? 'ADR toegelaten' : location.adrAllowed === false ? 'ADR niet toegelaten' : null,
    location.craneRequired ? 'kraan vereist' : null,
    location.forkliftAvailable ? 'heftruck beschikbaar' : null,
    location.accessRestrictions,
    location.vehicleRestrictions,
    location.trailerRestrictions,
  ].filter(Boolean)

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Locaties', to: '/locations' }, { label: location.code }]} />
      <PageHeader
        title={location.name}
        subtitle={`${location.code} · ${LOCATION_TYPE_LABELS[location.type]}`}
        action={
          <div className="location-detail-actions">
            {canEdit && !editing && (
              <Button variant="secondary" onClick={() => { setSaveError(null); setEditing(true) }}>
                Bewerken
              </Button>
            )}
            {hasPermission('locations.create') && !editing && (
              <Button variant="secondary" onClick={() => void handleDuplicate()} disabled={duplicating}>
                {duplicating ? 'Bezig…' : 'Dupliceren'}
              </Button>
            )}
            {hasPermission('locations.delete') && <Button variant="danger" onClick={() => setConfirmDelete(true)}>Verwijderen</Button>}
          </div>
        }
      />

      <div className="location-detail-badges">
        {location.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>}
        {location.alfapassRequired && <Badge tone="warning">Alfapass vereist</Badge>}
        {location.appointmentRequired && <Badge tone="info">Afspraak vereist</Badge>}
        {location.deliveryByAppointmentOnly && <Badge tone="info">Leveren enkel op afspraak</Badge>}
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
          <FormField label="Externe referentie">
            <span>{location.externalReference ?? '—'}</span>
          </FormField>
          <FormField label="Contact ter plaatse">
            <span>
              {[location.contactName, location.contactPhone, location.contactMobile, location.contactEmail].filter(Boolean).join(' · ') || '—'}
            </span>
          </FormField>
          <FormField label="Openingsuren" className="location-detail-full">
            {openingLines.length > 0 ? (
              <span>{openingLines.join(' · ')}</span>
            ) : (
              <span>{location.openingHours ?? '—'}</span>
            )}
          </FormField>
          <FormField label="Toegang">
            <span>
              {[
                location.gate ? `poort ${location.gate}` : null,
                location.dock ? `kade/dok ${location.dock}` : null,
                location.receptionPoint ? `aanmelden: ${location.receptionPoint}` : null,
                location.accessCode ? `code ${location.accessCode}` : null,
              ]
                .filter(Boolean)
                .join(' · ') || '—'}
            </span>
          </FormField>
          <FormField label="Planning">
            <span>
              {[
                location.defaultLoadingMinutes != null ? `laadtijd ${location.defaultLoadingMinutes} min` : null,
                location.defaultUnloadingMinutes != null ? `lostijd ${location.defaultUnloadingMinutes} min` : null,
                location.preferredArrivalFrom || location.preferredArrivalTo
                  ? `voorkeur ${location.preferredArrivalFrom ?? '…'}–${location.preferredArrivalTo ?? '…'}`
                  : null,
                location.earliestArrival ? `vroegste ${location.earliestArrival}` : null,
                location.latestArrival ? `laatste ${location.latestArrival}` : null,
              ]
                .filter(Boolean)
                .join(' · ') || '—'}
            </span>
          </FormField>
          <FormField label="Routebeschrijving" className="location-detail-full">
            <span>{location.routeDescription ?? '—'}</span>
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
          <FormField label="Chauffeursinstructies" className="location-detail-full">
            <span>{location.driverInstructions ?? '—'}</span>
          </FormField>
          <FormField label="Restricties" className="location-detail-full">
            <span>{restrictionParts.join(' · ') || '—'}</span>
          </FormField>
          <FormField label="Interne memo" className="location-detail-full">
            <span>{location.internalMemo ?? '—'}</span>
          </FormField>
          <FormField label="Notities" className="location-detail-full">
            <span>{location.notes ?? '—'}</span>
          </FormField>
        </div>
      ) : (
        <LocationForm
          mode="edit"
          initial={toInput(location)}
          submitting={saving}
          error={saveError}
          onSubmit={(value) => void saveEdit(value)}
          onCancel={() => setEditing(false)}
        />
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
