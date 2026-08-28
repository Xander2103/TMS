import { useState, type FormEvent } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { createLocation } from '../api/locationsApi'
import { extractAddressDuplicateConflict } from '../api/addressDuplicates'
import { checkAddressDuplicates, type AddressDuplicateCandidate } from '../api/customerAddressesApi'
import { LOCATION_TYPE_LABEL_KEYS, LOCATION_TYPES, type LocationOption, type LocationType } from '../types'

/**
 * How the dialog resolved: `created` = a NEW address was posted (with `customerId`, so the
 * server already linked it to the customer); `existing` = the user picked an existing address
 * from the duplicate warning (nothing was created or linked).
 */
export type LocationQuickCreateSource = 'created' | 'existing'

interface LocationQuickCreateDialogProps {
  customerId: string
  initialName?: string
  /** Resolves the inline-create flow: the location (new or existing) and how it came about, or null on cancel. */
  onClose: (created: LocationOption | null, source?: LocationQuickCreateSource) => void
}

/**
 * Compact inline-create dialog for a customer location (used from order entry and the
 * customer's Adressen tab). Posts to the same POST /api/locations use case as the full form —
 * no separate simplified entity. The same-front-door rule is enforced server-side; the
 * pre-flight check here only makes the candidates visible before the round trip.
 */
export function LocationQuickCreateDialog({ customerId, initialName, onClose }: LocationQuickCreateDialogProps) {
  const { t } = useLocale()
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
  // Duplicate detection (sprint 2C): an EXACT match blocks the first submit; the user either
  // reuses the existing address or deliberately overrides.
  const [duplicates, setDuplicates] = useState<AddressDuplicateCandidate[] | null>(null)
  const [overrideDuplicate, setOverrideDuplicate] = useState(false)

  /** An override only applies to the address it was given for: any address change resets it. */
  function setAddressField(setter: (value: string) => void) {
    return (value: string) => {
      setter(value)
      setOverrideDuplicate(false)
      setDuplicates(null)
    }
  }

  function selectCountry(value: string | null) {
    setCountryCode(value)
    setOverrideDuplicate(false)
    setDuplicates(null)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    // Code is optional since the master-data wave: blank codes are generated server-side.
    const nextClientErrors: { code?: string; name?: string } = {}
    if (!name.trim()) nextClientErrors.name = t('locations.form.nameRequired')
    setClientErrors(nextClientErrors)
    if (nextClientErrors.name) return

    setSaving(true)
    setError(null)
    setFieldErrors({})
    try {
      if (!overrideDuplicate) {
        const check = await checkAddressDuplicates({
          street: street.trim() || null,
          houseNumber: houseNumber.trim() || null,
          postalCode: postalCode.trim() || null,
          city: city.trim() || null,
          countryCode,
        })
        if (check.hasExactMatch) {
          // Same front door: never silently create a second record for it.
          setDuplicates(check.candidates)
          setSaving(false)
          return
        }
      }

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
        // The same field the full form uses; the server enforces the rule either way.
        overrideDuplicate,
      })
      onClose(
        {
          id: created.id,
          code: created.code,
          name: created.name,
          type: created.type,
          city: created.city,
          isDefaultLoadingLocation: created.isDefaultLoadingLocation,
          isDefaultUnloadingLocation: created.isDefaultUnloadingLocation,
          isDefaultBillingLocation: created.isDefaultBillingLocation,
        },
        'created',
      )
    } catch (err) {
      // The server found the duplicate (race, or the pre-flight was skipped): same warning UI.
      const conflict = extractAddressDuplicateConflict(err)
      if (conflict) {
        setDuplicates(conflict.candidates)
        setSaving(false)
        return
      }
      const described = describeApiError(err, t('locations.new.createFailed'))
      setError(described.message)
      setFieldErrors(described.fieldErrors)
      setSaving(false)
    }
  }

  return (
    <Modal
      title={t('locations.quickCreate.title')}
      onClose={() => onClose(null)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(null)} disabled={saving}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="location-quick-create" disabled={saving}>
            {saving ? t('locations.quickCreate.creating') : t('locations.quickCreate.create')}
          </Button>
        </>
      }
    >
      <form id="location-quick-create" onSubmit={handleSubmit} noValidate>
        {duplicates && duplicates.length > 0 && (
          <AddressDuplicateWarning
            candidates={duplicates}
            disabled={saving}
            onUseExisting={(candidate) =>
              onClose(
                {
                  id: candidate.locationId,
                  code: candidate.code,
                  name: candidate.name,
                  type: candidate.type,
                  city: candidate.city,
                  isDefaultLoadingLocation: false,
                  isDefaultUnloadingLocation: false,
                  isDefaultBillingLocation: false,
                },
                'existing',
              )
            }
            onCreateAnyway={() => {
              setOverrideDuplicate(true)
              setDuplicates(null)
            }}
          />
        )}
        <ValidationSummary
          message={error}
          fieldErrors={fieldErrors}
          fieldLabels={{ countryCode: t('locations.form.fields.country'), code: t('locations.form.fields.code') }}
        />
        <FormField
          label={t('locations.form.fields.code')}
          htmlFor="qc-code"
          hint={t('locations.form.hints.codeAuto')}
          error={clientErrors.code ?? getFieldError(fieldErrors, 'code')}
        >
          <input id="qc-code" value={code} onChange={(e) => setCode(e.target.value)} maxLength={40} disabled={saving} />
        </FormField>
        <FormField label={t('locations.form.fields.name')} htmlFor="qc-name" required error={clientErrors.name}>
          <input id="qc-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} disabled={saving} />
        </FormField>
        <FormField label={t('locations.form.fields.type')} htmlFor="qc-type">
          <select id="qc-type" value={type} onChange={(e) => setType(e.target.value as LocationType)} disabled={saving}>
            {LOCATION_TYPES.map((locationType) => (
              <option key={locationType} value={locationType}>
                {t(LOCATION_TYPE_LABEL_KEYS[locationType])}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('locations.form.fields.street')} htmlFor="qc-street">
          <input id="qc-street" value={street} onChange={(e) => setAddressField(setStreet)(e.target.value)} maxLength={150} disabled={saving} />
        </FormField>
        <FormField label={t('locations.form.fields.houseNumber')} htmlFor="qc-house">
          <input id="qc-house" value={houseNumber} onChange={(e) => setAddressField(setHouseNumber)(e.target.value)} maxLength={20} disabled={saving} />
        </FormField>
        <FormField label={t('locations.form.fields.postalCode')} htmlFor="qc-postal">
          <input id="qc-postal" value={postalCode} onChange={(e) => setAddressField(setPostalCode)(e.target.value)} maxLength={20} disabled={saving} />
        </FormField>
        <FormField label={t('locations.quickCreate.cityLabel')} htmlFor="qc-city">
          <input id="qc-city" value={city} onChange={(e) => setAddressField(setCity)(e.target.value)} maxLength={100} disabled={saving} />
        </FormField>
        <FormField label={t('locations.form.fields.country')} htmlFor="qc-country" error={getFieldError(fieldErrors, 'countryCode')}>
          <CountryCombobox id="qc-country" value={countryCode} onChange={selectCountry} disabled={saving} />
        </FormField>
      </form>
    </Modal>
  )
}

interface AddressDuplicateWarningProps {
  candidates: AddressDuplicateCandidate[]
  disabled?: boolean
  /** Omit to hide the "use existing" action (e.g. a form that cannot swap to another record). */
  onUseExisting?: (candidate: AddressDuplicateCandidate) => void
  onCreateAnyway: () => void
}

/**
 * "This address may already exist" — the candidate list with "use existing" / "create anyway".
 * Shared by the quick-create dialog and the full address form so both speak the same words.
 * Inactive candidates are shown for context but never offered as the address to reuse.
 */
export function AddressDuplicateWarning({ candidates, disabled, onUseExisting, onCreateAnyway }: AddressDuplicateWarningProps) {
  const { t } = useLocale()
  return (
    <div className="location-duplicate-warning" role="status">
      <strong>{t('locations.duplicate.title')}</strong>
      <ul>
        {candidates.map((candidate) => (
          <li key={candidate.locationId}>
            {onUseExisting && candidate.isActive && (
              <>
                <button type="button" className="link-button" disabled={disabled} onClick={() => onUseExisting(candidate)}>
                  {t('locations.duplicate.useExisting')}
                </button>{' '}
              </>
            )}
            <span>
              {candidate.name} — {[candidate.street, candidate.houseNumber].filter(Boolean).join(' ')}
              {candidate.postalCode || candidate.city ? `, ${[candidate.postalCode, candidate.city].filter(Boolean).join(' ')}` : ''}
            </span>
            {!candidate.isActive && <span className="customer-form-muted"> ({t('ui.statusBadges.inactive')})</span>}
            {candidate.linkedCustomers.length > 0 && (
              <span className="customer-form-muted">
                {' '}
                ({t('locations.duplicate.usedBy', { customers: candidate.linkedCustomers.join(', ') })})
              </span>
            )}
          </li>
        ))}
      </ul>
      <Button variant="secondary" disabled={disabled} onClick={onCreateAnyway}>
        {t('locations.duplicate.createAnyway')}
      </Button>
    </div>
  )
}
