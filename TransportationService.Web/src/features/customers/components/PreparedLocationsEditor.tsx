import { useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { LOCATION_TYPE_LABELS, LOCATION_TYPES, type LocationType } from '../../locations/types'
import { createPreparedLocation, type PreparedLocation } from '../utils/preparedLocations'

interface PreparedLocationsEditorProps {
  value: PreparedLocation[]
  onChange: (rows: PreparedLocation[]) => void
  disabled?: boolean
}

/**
 * Staged locations for the customer CREATE flow. Nothing is posted here: rows are held in
 * client-side state and created via POST /api/locations only AFTER the customer exists (the
 * quick-create dialog itself posts immediately, so create mode uses this staged variant with
 * the same compact field set instead).
 */
export function PreparedLocationsEditor({ value, onChange, disabled }: PreparedLocationsEditorProps) {
  const [showDialog, setShowDialog] = useState(false)

  return (
    <div className="customer-staged-locations">
      {value.length === 0 && (
        <p className="customer-form-muted">Nog geen locaties toegevoegd. Ze worden aangemaakt zodra je de klant opslaat.</p>
      )}
      {value.map((row) => (
        <div key={row.key} className="customer-staged-location-card">
          <div className="customer-staged-location-info">
            <strong>{row.name}</strong>
            <span className="customer-form-muted">
              {LOCATION_TYPE_LABELS[row.type]}
              {row.city.trim() ? ` · ${row.city.trim()}` : ''}
            </span>
          </div>
          <Button
            variant="ghost"
            disabled={disabled}
            onClick={() => onChange(value.filter((r) => r.key !== row.key))}
            aria-label={`Locatie ${row.name} verwijderen`}
          >
            Verwijderen
          </Button>
        </div>
      ))}
      <div>
        <Button variant="secondary" disabled={disabled} onClick={() => setShowDialog(true)}>
          + Locatie toevoegen
        </Button>
      </div>

      {showDialog && (
        <StagedLocationDialog
          onClose={() => setShowDialog(false)}
          onAdd={(row) => {
            onChange([...value, row])
            setShowDialog(false)
          }}
        />
      )}
    </div>
  )
}

function StagedLocationDialog({ onAdd, onClose }: { onAdd: (row: PreparedLocation) => void; onClose: () => void }) {
  const [name, setName] = useState('')
  const [type, setType] = useState<LocationType>('CustomerLocation')
  const [street, setStreet] = useState('')
  const [houseNumber, setHouseNumber] = useState('')
  const [postalCode, setPostalCode] = useState('')
  const [city, setCity] = useState('')
  const [countryCode, setCountryCode] = useState<string | null>('BE')
  const [contactPhone, setContactPhone] = useState('')
  const [nameError, setNameError] = useState<string | undefined>(undefined)

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    // The staged dialog lives inside the customer <form>; never bubble the submit up.
    event.stopPropagation()
    if (!name.trim()) {
      setNameError('Naam is verplicht.')
      return
    }
    onAdd({
      ...createPreparedLocation(),
      name: name.trim(),
      type,
      street,
      houseNumber,
      postalCode,
      city,
      countryCode,
      contactPhone,
    })
  }

  return (
    <Modal
      title="Locatie toevoegen"
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Annuleren
          </Button>
          <Button type="submit" form="staged-location-form">
            Toevoegen
          </Button>
        </>
      }
    >
      <p className="customer-form-muted">
        Deze locatie wordt aangemaakt zodra je de klant opslaat.
      </p>
      <form id="staged-location-form" onSubmit={handleSubmit} noValidate>
        <FormField label="Naam" htmlFor="sl-name" required error={nameError}>
          <input
            id="sl-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            aria-invalid={nameError ? 'true' : undefined}
            maxLength={200}
            autoFocus
          />
        </FormField>
        <FormField label="Type" htmlFor="sl-type">
          <select id="sl-type" value={type} onChange={(e) => setType(e.target.value as LocationType)}>
            {LOCATION_TYPES.map((t) => (
              <option key={t} value={t}>
                {LOCATION_TYPE_LABELS[t]}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Straat" htmlFor="sl-street">
          <input id="sl-street" value={street} onChange={(e) => setStreet(e.target.value)} maxLength={150} />
        </FormField>
        <FormField label="Nummer" htmlFor="sl-house">
          <input id="sl-house" value={houseNumber} onChange={(e) => setHouseNumber(e.target.value)} maxLength={20} />
        </FormField>
        <FormField label="Postcode" htmlFor="sl-postal">
          <input id="sl-postal" value={postalCode} onChange={(e) => setPostalCode(e.target.value)} maxLength={20} />
        </FormField>
        <FormField label="Gemeente" htmlFor="sl-city">
          <input id="sl-city" value={city} onChange={(e) => setCity(e.target.value)} maxLength={100} />
        </FormField>
        <FormField label="Land" htmlFor="sl-country">
          <CountryCombobox id="sl-country" value={countryCode} onChange={setCountryCode} />
        </FormField>
        <FormField label="Telefoon" htmlFor="sl-phone">
          <input id="sl-phone" value={contactPhone} onChange={(e) => setContactPhone(e.target.value)} maxLength={30} />
        </FormField>
      </form>
    </Modal>
  )
}
