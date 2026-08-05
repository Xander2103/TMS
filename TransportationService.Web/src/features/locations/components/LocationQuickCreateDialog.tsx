import { useState, type FormEvent } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { createLocation } from '../api/locationsApi'
import { LOCATION_TYPE_LABELS, LOCATION_TYPES, type LocationOption, type LocationType } from '../types'

interface LocationQuickCreateDialogProps {
  customerId: string
  initialName?: string
  /** Resolves the inline-create flow: the created location, or null on cancel. */
  onClose: (created: LocationOption | null) => void
}

/**
 * Compact inline-create dialog for a customer location (used from order entry). Posts to the
 * same POST /api/locations use case as the full form — no separate simplified entity.
 */
export function LocationQuickCreateDialog({ customerId, initialName, onClose }: LocationQuickCreateDialogProps) {
  const [code, setCode] = useState('')
  const [name, setName] = useState(initialName ?? '')
  const [type, setType] = useState<LocationType>('CustomerLocation')
  const [street, setStreet] = useState('')
  const [houseNumber, setHouseNumber] = useState('')
  const [postalCode, setPostalCode] = useState('')
  const [city, setCity] = useState('')
  const [countryCode, setCountryCode] = useState<string | null>('BE')
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [clientErrors, setClientErrors] = useState<{ code?: string; name?: string }>({})
  const [saving, setSaving] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    // Code is optional since the master-data wave: blank codes are generated server-side.
    const nextClientErrors: { code?: string; name?: string } = {}
    if (!name.trim()) nextClientErrors.name = 'Naam is verplicht.'
    setClientErrors(nextClientErrors)
    if (nextClientErrors.name) return

    setSaving(true)
    setError(null)
    setFieldErrors({})
    try {
      const created = await createLocation({
        code: code.trim(),
        name: name.trim(),
        type,
        street: street.trim() || null,
        houseNumber: houseNumber.trim() || null,
        postalCode: postalCode.trim() || null,
        city: city.trim() || null,
        countryCode,
        latitude: null,
        longitude: null,
        contactName: null,
        contactPhone: null,
        contactMobile: null,
        contactEmail: null,
        customerContactId: null,
        externalReference: null,
        openingHours: null,
        openingIntervals: [],
        loadingInstructions: null,
        unloadingInstructions: null,
        accessInstructions: null,
        accessRestrictions: null,
        vehicleRestrictions: null,
        trailerRestrictions: null,
        alfapassRequired: false,
        appointmentRequired: false,
        gate: null,
        receptionPoint: null,
        dock: null,
        routeDescription: null,
        deliveryByAppointmentOnly: false,
        heightRestrictionMeters: null,
        weightRestrictionTons: null,
        adrAllowed: null,
        craneRequired: false,
        forkliftAvailable: false,
        driverInstructions: null,
        internalMemo: null,
        defaultLoadingMinutes: null,
        defaultUnloadingMinutes: null,
        preferredArrivalFrom: null,
        preferredArrivalTo: null,
        earliestArrival: null,
        latestArrival: null,
        isActive: true,
        customerId,
        notes: null,
        isDefaultLoadingLocation: false,
        isDefaultUnloadingLocation: false,
        isDefaultBillingLocation: false,
      })
      onClose({
        id: created.id,
        code: created.code,
        name: created.name,
        type: created.type,
        city: created.city,
        isDefaultLoadingLocation: created.isDefaultLoadingLocation,
        isDefaultUnloadingLocation: created.isDefaultUnloadingLocation,
        isDefaultBillingLocation: created.isDefaultBillingLocation,
      })
    } catch (err) {
      const described = describeApiError(err, 'Locatie kon niet worden aangemaakt.')
      setError(described.message)
      setFieldErrors(described.fieldErrors)
      setSaving(false)
    }
  }

  return (
    <Modal
      title="Nieuwe klantlocatie"
      onClose={() => onClose(null)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(null)} disabled={saving}>
            Annuleren
          </Button>
          <Button type="submit" form="location-quick-create" disabled={saving}>
            {saving ? 'Aanmaken…' : 'Locatie aanmaken'}
          </Button>
        </>
      }
    >
      <form id="location-quick-create" onSubmit={handleSubmit} noValidate>
        <ValidationSummary message={error} fieldErrors={fieldErrors} fieldLabels={{ countryCode: 'Land', code: 'Code' }} />
        <FormField
          label="Code"
          htmlFor="qc-code"
          hint="Leeg laten voor automatische code."
          error={clientErrors.code ?? getFieldError(fieldErrors, 'code')}
        >
          <input id="qc-code" value={code} onChange={(e) => setCode(e.target.value)} maxLength={40} disabled={saving} />
        </FormField>
        <FormField label="Naam" htmlFor="qc-name" required error={clientErrors.name}>
          <input id="qc-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} disabled={saving} />
        </FormField>
        <FormField label="Type" htmlFor="qc-type">
          <select id="qc-type" value={type} onChange={(e) => setType(e.target.value as LocationType)} disabled={saving}>
            {LOCATION_TYPES.map((t) => (
              <option key={t} value={t}>
                {LOCATION_TYPE_LABELS[t]}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Straat" htmlFor="qc-street">
          <input id="qc-street" value={street} onChange={(e) => setStreet(e.target.value)} maxLength={150} disabled={saving} />
        </FormField>
        <FormField label="Nummer" htmlFor="qc-house">
          <input id="qc-house" value={houseNumber} onChange={(e) => setHouseNumber(e.target.value)} maxLength={20} disabled={saving} />
        </FormField>
        <FormField label="Postcode" htmlFor="qc-postal">
          <input id="qc-postal" value={postalCode} onChange={(e) => setPostalCode(e.target.value)} maxLength={20} disabled={saving} />
        </FormField>
        <FormField label="Plaats" htmlFor="qc-city">
          <input id="qc-city" value={city} onChange={(e) => setCity(e.target.value)} maxLength={100} disabled={saving} />
        </FormField>
        <FormField label="Land" htmlFor="qc-country" error={getFieldError(fieldErrors, 'countryCode')}>
          <CountryCombobox id="qc-country" value={countryCode} onChange={setCountryCode} disabled={saving} />
        </FormField>
      </form>
    </Modal>
  )
}
