import { useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { useLocale } from '../../../i18n/localeContext'
import { LOCATION_TYPE_LABEL_KEYS, LOCATION_TYPES, type LocationType } from '../../locations/types'
import { createPreparedLocation, type PreparedLocation } from '../utils/preparedLocations'

interface PreparedLocationsEditorProps {
  value: PreparedLocation[]
  onChange: (rows: PreparedLocation[]) => void
  disabled?: boolean
}

/**
 * Staged locations for the customer CREATE flow. Nothing is posted here: rows are held in
 * client-side state and created via POST /api/locations only AFTER the customer create succeeds
 * (the quick-create dialog itself posts immediately, so create mode uses this staged variant
 * with the same compact field set instead).
 */
export function PreparedLocationsEditor({ value, onChange, disabled }: PreparedLocationsEditorProps) {
  const { t } = useLocale()
  const [showDialog, setShowDialog] = useState(false)

  return (
    <div className="customer-staged-locations">
      {value.length === 0 && (
        <p className="customer-form-muted">{t('customers.staged.empty')}</p>
      )}
      {value.map((row) => (
        <div key={row.key} className="customer-staged-location-card">
          <div className="customer-staged-location-info">
            <strong>{row.name}</strong>
            <span className="customer-form-muted">
              {t(LOCATION_TYPE_LABEL_KEYS[row.type])}
              {row.city.trim() ? ` · ${row.city.trim()}` : ''}
            </span>
          </div>
          <Button
            variant="ghost"
            disabled={disabled}
            onClick={() => onChange(value.filter((r) => r.key !== row.key))}
            aria-label={t('customers.staged.removeAria', { name: row.name })}
          >
            {t('ui.actions.delete')}
          </Button>
        </div>
      ))}
      <div>
        <Button variant="secondary" disabled={disabled} onClick={() => setShowDialog(true)}>
          {t('customers.staged.add')}
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
  const { t } = useLocale()
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
      setNameError(t('customers.form.nameRequired'))
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
      title={t('customers.staged.dialogTitle')}
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="staged-location-form">
            {t('ui.actions.add')}
          </Button>
        </>
      }
    >
      <p className="customer-form-muted">{t('customers.staged.dialogExplanation')}</p>
      <form id="staged-location-form" onSubmit={handleSubmit} noValidate>
        <FormField label={t('customers.fields.name')} htmlFor="sl-name" required error={nameError}>
          <input
            id="sl-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            aria-invalid={nameError ? 'true' : undefined}
            maxLength={200}
            autoFocus
          />
        </FormField>
        <FormField label={t('customers.staged.type')} htmlFor="sl-type">
          <select id="sl-type" value={type} onChange={(e) => setType(e.target.value as LocationType)}>
            {LOCATION_TYPES.map((locationType) => (
              <option key={locationType} value={locationType}>
                {t(LOCATION_TYPE_LABEL_KEYS[locationType])}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('customers.form.street')} htmlFor="sl-street">
          <input id="sl-street" value={street} onChange={(e) => setStreet(e.target.value)} maxLength={150} />
        </FormField>
        <FormField label={t('customers.form.houseNumber')} htmlFor="sl-house">
          <input id="sl-house" value={houseNumber} onChange={(e) => setHouseNumber(e.target.value)} maxLength={20} />
        </FormField>
        <FormField label={t('customers.form.postalCode')} htmlFor="sl-postal">
          <input id="sl-postal" value={postalCode} onChange={(e) => setPostalCode(e.target.value)} maxLength={20} />
        </FormField>
        <FormField label={t('customers.form.city')} htmlFor="sl-city">
          <input id="sl-city" value={city} onChange={(e) => setCity(e.target.value)} maxLength={100} />
        </FormField>
        <FormField label={t('customers.fields.countryCode')} htmlFor="sl-country">
          <CountryCombobox id="sl-country" value={countryCode} onChange={setCountryCode} />
        </FormField>
        <FormField label={t('customers.contacts.phone')} htmlFor="sl-phone">
          <input id="sl-phone" value={contactPhone} onChange={(e) => setContactPhone(e.target.value)} maxLength={30} />
        </FormField>
      </form>
    </Modal>
  )
}
