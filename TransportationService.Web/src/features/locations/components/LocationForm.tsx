import { useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormActions } from '../../../components/ui/FormActions'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { SectionedForm, type SectionDef } from '../../../components/ui/SectionedForm'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useSectionNavigation, firstSectionWithError } from '../../../components/ui/useSectionNavigation'
import type { FieldErrors } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers, getCustomer } from '../../customers/api/customersApi'
import type { CustomerContact, CustomerListItem } from '../../customers/types'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { checkAddressDuplicates, type AddressDuplicateCandidate } from '../api/customerAddressesApi'
import { extractAddressDuplicateConflict } from '../api/addressDuplicates'
import { AddressDuplicateWarning } from './LocationQuickCreateDialog'
import { OpeningHoursEditor } from './OpeningHoursEditor'
import { openingIntervalsValid } from '../openingHours'
import {
  LOCATION_FIELD_IDS,
  LOCATION_SECTIONS,
  LOCATION_SECTION_FIELD_KEYS,
  computeLocationSectionStatus,
} from './locationSections'
import { LOCATION_TYPE_LABEL_KEYS, LOCATION_TYPES, type LocationInput, type LocationType } from '../types'
import '../pages/location-form.css'

interface LocationFormProps {
  mode: 'create' | 'edit'
  /** Read once at mount (pages early-return while loading, so this is stable). */
  initial: LocationInput
  submitting: boolean
  error?: string | null
  /**
   * The raw error of the last submit, when the page has one. A 409 `address_duplicate`
   * (same front door, see R1) is rendered as the candidate list with "Toch aanmaken".
   */
  submitError?: unknown
  onSubmit: (value: LocationInput) => void
  onCancel: () => void
  submitLabel?: string
}

/** The physical-address fields: changing any of them invalidates a duplicate override. */
const ADDRESS_KEYS: ReadonlySet<keyof LocationInput> = new Set(['street', 'houseNumber', 'postalCode', 'city', 'countryCode'])

function contactDisplayName(contact: CustomerContact): string {
  return contact.displayName || [contact.firstName, contact.lastName].filter(Boolean).join(' ')
}

/** Vertaalsleutels voor de validation summary — renderen als t(FIELD_LABEL_KEYS[key]). */
const FIELD_LABEL_KEYS: Record<string, string> = {
  name: 'locations.form.fields.name',
  code: 'locations.form.fields.code',
  contactEmail: 'locations.form.fields.contactEmail',
  latitude: 'locations.form.fields.latitude',
  longitude: 'locations.form.fields.longitude',
  openingIntervals: 'locations.form.fields.openingIntervals',
}

/**
 * Shared create/edit location form, rebuilt as a {@link SectionedForm} (left rail like the
 * employee form). Only "Naam" (and, on edit, the already-assigned "Code") blocks submit;
 * everything else is optional. Every section reports a semantic status in the rail:
 * ✓ vereiste velden geldig, ● optionele data aanwezig, ! validatiefout, ○ leeg.
 *
 * The access code field is rendered — and included in the payload — only for users holding
 * locations.view_sensitive: the backend preserves the stored code when the field is omitted.
 */
export function LocationForm({ mode, initial, submitting, error, submitError, onSubmit, onCancel, submitLabel }: LocationFormProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canViewSensitive = hasPermission('locations.view_sensitive')

  const [form, setForm] = useState<LocationInput>(initial)
  const [openingValid, setOpeningValid] = useState(() => openingIntervalsValid(initial.openingIntervals))
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [dirty, setDirty] = useState(false)

  // Same-front-door rule (R1): the server refuses a duplicate create unless overridden; the
  // form checks first so the candidates show before the round trip, and also understands the
  // server's 409 when the page hands it over.
  const [duplicates, setDuplicates] = useState<AddressDuplicateCandidate[] | null>(null)
  const [overrideDuplicate, setOverrideDuplicate] = useState(false)
  const [checkingDuplicates, setCheckingDuplicates] = useState(false)
  // Derived, not synced: the server's candidates show until the user overrides THAT error.
  const [dismissedSubmitError, setDismissedSubmitError] = useState<unknown>(undefined)
  const serverDuplicates = useMemo(() => extractAddressDuplicateConflict(submitError)?.candidates ?? null, [submitError])
  const shownDuplicates = duplicates ?? (submitError !== undefined && dismissedSubmitError !== submitError ? serverDuplicates : null)

  // A shared address (several customer relationships) has no single legacy owner to move:
  // the customer field is read-only and points at Klant › Adressen (D2).
  const linkedCustomerCount = initial.linkedCustomerCount ?? 0
  const sharedAddress = mode === 'edit' && linkedCustomerCount > 1

  const [customers, setCustomers] = useState<CustomerListItem[]>([])
  // Contacts (and the customer name, for when the linked customer falls outside the first
  // options page) are kept together with the customer they were fetched for, so switching
  // customers "clears" the list by derivation instead of a synchronous setState in an effect.
  const [contactsFor, setContactsFor] = useState<{ customerId: string; customerName: string; contacts: CustomerContact[] } | null>(null)

  useEffect(() => {
    let mounted = true
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => {
        if (mounted) setCustomers(result.items)
      })
      .catch(() => {
        /* customer link stays usable via the already-selected value */
      })
    return () => {
      mounted = false
    }
  }, [])

  // The on-site contact can be linked to one of the customer's contact persons; the list
  // follows the selected customer.
  const customerId = form.customerId
  useEffect(() => {
    if (!customerId) return
    let mounted = true
    getCustomer(customerId)
      .then((customer) => {
        if (mounted) {
          setContactsFor({ customerId, customerName: customer.name, contacts: customer.contacts.filter((c) => c.isActive) })
        }
      })
      .catch(() => {
        if (mounted) setContactsFor({ customerId, customerName: '', contacts: [] })
      })
    return () => {
      mounted = false
    }
  }, [customerId])

  const contacts = customerId && contactsFor?.customerId === customerId ? contactsFor.contacts : []

  const customerOptions = useMemo(() => {
    const options = customers.map((c) => ({ value: c.id, label: c.name }))
    // Keep an already-linked customer selectable/visible even when it is not in the first page.
    if (customerId && !options.some((o) => o.value === customerId)) {
      const known = contactsFor?.customerId === customerId ? contactsFor.customerName : ''
      options.push({ value: customerId, label: known || t('locations.form.linkedCustomer') })
    }
    return options
  }, [customers, customerId, contactsFor, t])

  function set<K extends keyof LocationInput>(key: K, value: LocationInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
    if (ADDRESS_KEYS.has(key)) {
      // An override only applies to the address it was given for.
      setOverrideDuplicate(false)
      setDuplicates(null)
    }
  }

  function touch() {
    if (!dirty) setDirty(true)
  }

  function selectCustomer(id: string | null) {
    // Switching customers invalidates a linked contact of the previous customer.
    setForm((f) => ({ ...f, customerId: id, customerContactId: id === f.customerId ? f.customerContactId : null }))
    touch()
  }

  function selectCustomerContact(contactId: string) {
    if (!contactId) {
      set('customerContactId', null)
      return
    }
    const contact = contacts.find((c) => c.id === contactId)
    if (!contact) return
    // Prefill the on-site snapshot fields from the linked contact; they stay editable.
    setForm((f) => ({
      ...f,
      customerContactId: contact.id,
      contactName: contactDisplayName(contact) || f.contactName,
      contactPhone: contact.phoneNumber,
      contactMobile: contact.mobilePhone,
      contactEmail: contact.email,
    }))
  }

  // ---- Section navigation, error routing & focus management -------------------------------
  const { activeId, setActive } = useSectionNavigation(
    LOCATION_SECTIONS.map((s) => s.id),
    LOCATION_SECTIONS[0].id,
  )
  const formRef = useRef<HTMLFormElement>(null)
  const pendingFocusKey = useRef<string | null>(null)
  const [focusToken, setFocusToken] = useState(0)

  // Runs after the section switch has rendered, so the target input exists in the DOM.
  useEffect(() => {
    const key = pendingFocusKey.current
    if (!key) return
    pendingFocusKey.current = null
    const id = LOCATION_FIELD_IDS[key]
    const target =
      (id ? document.getElementById(id) : null) ??
      formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]') ??
      null
    target?.focus()
  }, [focusToken])

  /** Activate the section owning `key` and focus its field (validation-summary click / failed submit). */
  function jumpToField(key: string) {
    const section = LOCATION_SECTIONS.find((s) => LOCATION_SECTION_FIELD_KEYS[s.id]?.includes(key))
    if (section) setActive(section.id)
    pendingFocusKey.current = key
    setFocusToken((token) => token + 1)
  }

  function validate(): Record<string, string> {
    const errors: Record<string, string> = {}
    if (!form.name.trim()) errors.name = t('locations.form.nameRequired')
    if (mode === 'edit' && !form.code.trim()) {
      // On update the backend requires the (already assigned) code; only create may leave it blank.
      errors.code = t('locations.form.codeRequired')
    }
    if (form.contactEmail && !form.contactEmail.includes('@')) {
      errors.contactEmail = t('locations.form.emailInvalid')
    }
    if (form.latitude != null && (form.latitude < -90 || form.latitude > 90)) {
      errors.latitude = t('locations.form.latitudeRange')
    }
    if (form.longitude != null && (form.longitude < -180 || form.longitude > 180)) {
      errors.longitude = t('locations.form.longitudeRange')
    }
    if (!openingValid) errors.openingIntervals = t('locations.form.openingInvalid')
    setFieldErrors(errors)
    return errors
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const errors = validate()
    const errorKeys = Object.keys(errors)
    if (errorKeys.length > 0) {
      // Route to the first section that owns a failing field and focus that field.
      const targetSection = firstSectionWithError(
        LOCATION_SECTIONS.map((s) => ({ id: s.id, fieldKeys: LOCATION_SECTION_FIELD_KEYS[s.id] })),
        errors,
      )
      const firstKey = targetSection
        ? (LOCATION_SECTION_FIELD_KEYS[targetSection] ?? []).find((key) => errorKeys.includes(key))
        : errorKeys[0]
      if (firstKey) jumpToField(firstKey)
      return
    }
    const payload: LocationInput = { ...form }
    if (!canViewSensitive) {
      // Never echo a value the user could not see: the backend keeps the stored access code.
      delete payload.accessCode
    }
    delete payload.linkedCustomerCount
    delete payload.linkedCustomerNames

    if (mode === 'create' && !overrideDuplicate && form.street?.trim()) {
      // Pre-flight: show the candidates before the round trip. The server enforces the rule
      // regardless, so a failed check simply lets the submit through.
      void submitAfterDuplicateCheck(payload)
      return
    }

    setDirty(false)
    onSubmit({ ...payload, overrideDuplicate })
  }

  async function submitAfterDuplicateCheck(payload: LocationInput) {
    setCheckingDuplicates(true)
    try {
      const check = await checkAddressDuplicates({
        street: payload.street,
        houseNumber: payload.houseNumber,
        postalCode: payload.postalCode,
        city: payload.city,
        countryCode: payload.countryCode,
      })
      if (check.hasExactMatch) {
        setDuplicates(check.candidates)
        return
      }
    } catch {
      /* the server still enforces the rule */
    } finally {
      setCheckingDuplicates(false)
    }
    setDirty(false)
    onSubmit({ ...payload, overrideDuplicate: false })
  }

  const summaryErrors: FieldErrors = Object.fromEntries(
    Object.entries(fieldErrors).map(([key, message]) => [key, [message]]),
  )

  const sectionHasError = (id: string) =>
    (LOCATION_SECTION_FIELD_KEYS[id] ?? []).some((key) => fieldErrors[key])
  const status = computeLocationSectionStatus(form, mode)

  // ---- Section bodies ---------------------------------------------------------------------

  const renderAlgemeen = () => (
    <FormSection title={t('locations.form.sectionTitles.general')} columns={2}>
      <FormField label={t('locations.form.fields.name')} htmlFor="loc-name" required error={fieldErrors.name}>
        <input
          id="loc-name"
          value={form.name}
          onChange={(e) => set('name', e.target.value)}
          disabled={submitting}
          maxLength={200}
          aria-invalid={fieldErrors.name ? 'true' : undefined}
        />
      </FormField>
      <FormField
        label={t('locations.form.fields.code')}
        htmlFor="loc-code"
        hint={mode === 'create' ? t('locations.form.hints.codeAuto') : undefined}
        required={mode === 'edit'}
        error={fieldErrors.code}
      >
        <input
          id="loc-code"
          value={form.code}
          onChange={(e) => set('code', e.target.value)}
          disabled={submitting}
          maxLength={40}
          aria-invalid={fieldErrors.code ? 'true' : undefined}
        />
      </FormField>
      <FormField label={t('locations.form.fields.type')} htmlFor="loc-type">
        <select id="loc-type" value={form.type} onChange={(e) => set('type', e.target.value as LocationType)} disabled={submitting}>
          {LOCATION_TYPES.map((type) => (
            <option key={type} value={type}>
              {t(LOCATION_TYPE_LABEL_KEYS[type])}
            </option>
          ))}
        </select>
      </FormField>
      <FormField label={t('locations.form.fields.externalReference')} htmlFor="loc-ext-ref" hint={t('locations.form.hints.externalReference')}>
        <input
          id="loc-ext-ref"
          value={form.externalReference ?? ''}
          onChange={(e) => set('externalReference', e.target.value || null)}
          disabled={submitting}
          maxLength={100}
        />
      </FormField>
      <FormField
        label={t('locations.form.fields.customer')}
        htmlFor="loc-customer"
        hint={
          sharedAddress
            ? t('locations.form.hints.sharedAddress', {
                count: linkedCustomerCount,
                customers: (initial.linkedCustomerNames ?? []).join(', '),
              })
            : t('locations.form.hints.customerSearch')
        }
      >
        <SearchableSelect
          id="loc-customer"
          value={form.customerId}
          onChange={selectCustomer}
          options={customerOptions}
          placeholder={t('locations.form.noCustomer')}
          disabled={submitting || sharedAddress}
          ariaLabel={t('locations.form.fields.customer')}
        />
      </FormField>
      {mode === 'edit' && (
        <div className="location-form-checkboxes">
          <label className="location-checkbox">
            <input type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} disabled={submitting} />
            <span>{t('locations.form.fields.active')}</span>
          </label>
        </div>
      )}
    </FormSection>
  )

  const renderAdres = () => (
    <FormSection title={t('locations.form.sectionTitles.address')} columns={2}>
      <FormField label={t('locations.form.fields.street')} htmlFor="loc-street">
        <input id="loc-street" value={form.street ?? ''} onChange={(e) => set('street', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.houseNumber')} htmlFor="loc-house">
        <input id="loc-house" value={form.houseNumber ?? ''} onChange={(e) => set('houseNumber', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.postalCode')} htmlFor="loc-postal">
        <input id="loc-postal" value={form.postalCode ?? ''} onChange={(e) => set('postalCode', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.city')} htmlFor="loc-city">
        <input id="loc-city" value={form.city ?? ''} onChange={(e) => set('city', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.country')} htmlFor="loc-country">
        <CountryCombobox id="loc-country" value={form.countryCode} onChange={(code) => { set('countryCode', code); touch() }} disabled={submitting} />
      </FormField>
      <details className="location-form-advanced form-span-all">
        <summary>{t('locations.form.advancedCoordinates')}</summary>
        <div className="location-form-advanced-grid">
          <FormField label={t('locations.form.fields.latitude')} htmlFor="loc-lat" error={fieldErrors.latitude}>
            <input
              id="loc-lat"
              type="number"
              step="0.000001"
              value={form.latitude ?? ''}
              onChange={(e) => set('latitude', e.target.value === '' ? null : Number(e.target.value))}
              disabled={submitting}
              aria-invalid={fieldErrors.latitude ? 'true' : undefined}
            />
          </FormField>
          <FormField label={t('locations.form.fields.longitude')} htmlFor="loc-lng" error={fieldErrors.longitude}>
            <input
              id="loc-lng"
              type="number"
              step="0.000001"
              value={form.longitude ?? ''}
              onChange={(e) => set('longitude', e.target.value === '' ? null : Number(e.target.value))}
              disabled={submitting}
              aria-invalid={fieldErrors.longitude ? 'true' : undefined}
            />
          </FormField>
        </div>
      </details>
    </FormSection>
  )

  const renderContact = () => (
    <FormSection title={t('locations.form.sectionTitles.contactOnSite')} columns={2}>
      {form.customerId && (
        <FormField
          label={t('locations.form.fields.customerContact')}
          htmlFor="loc-customer-contact"
          hint={t('locations.form.hints.customerContact')}
          className="form-span-all"
        >
          <select id="loc-customer-contact" value={form.customerContactId ?? ''} onChange={(e) => selectCustomerContact(e.target.value)} disabled={submitting}>
            <option value="">{t('locations.form.noContactLink')}</option>
            {contacts.map((c) => (
              <option key={c.id} value={c.id}>
                {contactDisplayName(c)}
              </option>
            ))}
          </select>
        </FormField>
      )}
      <FormField label={t('locations.form.fields.contactName')} htmlFor="loc-contact-name">
        <input id="loc-contact-name" value={form.contactName ?? ''} onChange={(e) => set('contactName', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.phone')} htmlFor="loc-contact-phone">
        <input id="loc-contact-phone" value={form.contactPhone ?? ''} onChange={(e) => set('contactPhone', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.mobile')} htmlFor="loc-contact-mobile">
        <input id="loc-contact-mobile" value={form.contactMobile ?? ''} onChange={(e) => set('contactMobile', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.email')} htmlFor="loc-contact-email" error={fieldErrors.contactEmail}>
        <input
          id="loc-contact-email"
          value={form.contactEmail ?? ''}
          onChange={(e) => set('contactEmail', e.target.value || null)}
          disabled={submitting}
          aria-invalid={fieldErrors.contactEmail ? 'true' : undefined}
        />
      </FormField>
    </FormSection>
  )

  const renderOpeningstijden = () => (
    <FormSection title={t('locations.form.sectionTitles.openingTimes')} columns={1}>
      <div className="form-span-all" id="loc-hours-editor" tabIndex={-1}>
        <OpeningHoursEditor
          value={form.openingIntervals}
          onChange={(intervals, isValid) => {
            set('openingIntervals', intervals)
            setOpeningValid(isValid)
            touch()
          }}
          disabled={submitting}
        />
        {fieldErrors.openingIntervals && (
          <p className="ui-form-field-error" role="alert">
            {fieldErrors.openingIntervals}
          </p>
        )}
      </div>
      <FormField label={t('locations.form.fields.openingHoursText')} htmlFor="loc-hours" hint={t('locations.form.hints.openingFallback')}>
        <input
          id="loc-hours"
          value={form.openingHours ?? ''}
          onChange={(e) => set('openingHours', e.target.value || null)}
          disabled={submitting}
          placeholder={t('locations.form.openingPlaceholder')}
          maxLength={500}
        />
      </FormField>
    </FormSection>
  )

  const renderOperationeel = () => (
    <>
      <FormSection title={t('locations.form.sectionTitles.terrain')} columns={2}>
        <FormField label={t('locations.form.fields.gate')} htmlFor="loc-gate">
          <input id="loc-gate" value={form.gate ?? ''} onChange={(e) => set('gate', e.target.value || null)} disabled={submitting} maxLength={50} />
        </FormField>
        <FormField label={t('locations.form.fields.receptionPoint')} htmlFor="loc-reception">
          <input id="loc-reception" value={form.receptionPoint ?? ''} onChange={(e) => set('receptionPoint', e.target.value || null)} disabled={submitting} maxLength={200} />
        </FormField>
        <FormField label={t('locations.form.fields.dock')} htmlFor="loc-dock">
          <input id="loc-dock" value={form.dock ?? ''} onChange={(e) => set('dock', e.target.value || null)} disabled={submitting} maxLength={50} />
        </FormField>
        <FormField label={t('locations.form.fields.routeDescription')} htmlFor="loc-route" className="form-span-all">
          <textarea id="loc-route" rows={2} value={form.routeDescription ?? ''} onChange={(e) => set('routeDescription', e.target.value || null)} disabled={submitting} />
        </FormField>
        <div className="location-form-checkboxes form-span-all">
          <label className="location-checkbox">
            <input type="checkbox" checked={form.craneRequired} onChange={(e) => set('craneRequired', e.target.checked)} disabled={submitting} />
            <span>{t('locations.flags.craneRequired')}</span>
          </label>
          <label className="location-checkbox">
            <input type="checkbox" checked={form.forkliftAvailable} onChange={(e) => set('forkliftAvailable', e.target.checked)} disabled={submitting} />
            <span>{t('locations.flags.forkliftAvailable')}</span>
          </label>
        </div>
      </FormSection>

      <FormSection title={t('locations.form.sectionTitles.access')} columns={2}>
        {canViewSensitive && (
          <FormField label={t('locations.form.fields.accessCode')} htmlFor="loc-access-code" hint={t('locations.form.hints.accessCode')}>
            <input id="loc-access-code" value={form.accessCode ?? ''} onChange={(e) => set('accessCode', e.target.value || null)} disabled={submitting} maxLength={100} />
          </FormField>
        )}
        <FormField label={t('locations.form.fields.accessRestrictions')} htmlFor="loc-access-restr">
          <input id="loc-access-restr" value={form.accessRestrictions ?? ''} onChange={(e) => set('accessRestrictions', e.target.value || null)} disabled={submitting} />
        </FormField>
        <div className="location-form-checkboxes form-span-all">
          <label className="location-checkbox">
            <input type="checkbox" checked={form.appointmentRequired} onChange={(e) => set('appointmentRequired', e.target.checked)} disabled={submitting} />
            <span>{t('locations.flags.appointmentRequired')}</span>
          </label>
          <label className="location-checkbox">
            <input type="checkbox" checked={form.deliveryByAppointmentOnly} onChange={(e) => set('deliveryByAppointmentOnly', e.target.checked)} disabled={submitting} />
            <span>{t('locations.flags.deliveryByAppointmentOnly')}</span>
          </label>
          <label className="location-checkbox">
            <input type="checkbox" checked={form.alfapassRequired} onChange={(e) => set('alfapassRequired', e.target.checked)} disabled={submitting} />
            <span>{t('locations.flags.alfapassRequired')}</span>
          </label>
        </div>
      </FormSection>

      <FormSection title={t('locations.form.sectionTitles.limits')} columns={2}>
        <FormField label={t('locations.form.fields.heightRestriction')} htmlFor="loc-height">
          <input
            id="loc-height"
            type="number"
            step="0.1"
            min="0"
            value={form.heightRestrictionMeters ?? ''}
            onChange={(e) => set('heightRestrictionMeters', e.target.value === '' ? null : Number(e.target.value))}
            disabled={submitting}
          />
        </FormField>
        <FormField label={t('locations.form.fields.weightRestriction')} htmlFor="loc-weight">
          <input
            id="loc-weight"
            type="number"
            step="0.1"
            min="0"
            value={form.weightRestrictionTons ?? ''}
            onChange={(e) => set('weightRestrictionTons', e.target.value === '' ? null : Number(e.target.value))}
            disabled={submitting}
          />
        </FormField>
        <FormField label={t('locations.form.fields.adrAllowed')} htmlFor="loc-adr" hint={t('locations.form.hints.adrUnknown')}>
          <select
            id="loc-adr"
            value={form.adrAllowed === null ? '' : String(form.adrAllowed)}
            onChange={(e) => set('adrAllowed', e.target.value === '' ? null : e.target.value === 'true')}
            disabled={submitting}
          >
            <option value="">{t('locations.form.adrUnknownOption')}</option>
            <option value="true">{t('locations.form.yes')}</option>
            <option value="false">{t('locations.form.no')}</option>
          </select>
        </FormField>
        <FormField label={t('locations.form.fields.vehicleRestrictions')} htmlFor="loc-vehicle-restr">
          <input id="loc-vehicle-restr" value={form.vehicleRestrictions ?? ''} onChange={(e) => set('vehicleRestrictions', e.target.value || null)} disabled={submitting} />
        </FormField>
        <FormField label={t('locations.form.fields.trailerRestrictions')} htmlFor="loc-trailer-restr">
          <input id="loc-trailer-restr" value={form.trailerRestrictions ?? ''} onChange={(e) => set('trailerRestrictions', e.target.value || null)} disabled={submitting} />
        </FormField>
      </FormSection>
    </>
  )

  const renderInstructies = () => (
    <FormSection title={t('locations.form.sectionTitles.instructions')} columns={1}>
      <FormField label={t('locations.form.fields.loadingInstructions')} htmlFor="loc-loading" className="form-span-all">
        <textarea id="loc-loading" rows={2} value={form.loadingInstructions ?? ''} onChange={(e) => set('loadingInstructions', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.unloadingInstructions')} htmlFor="loc-unloading" className="form-span-all">
        <textarea id="loc-unloading" rows={2} value={form.unloadingInstructions ?? ''} onChange={(e) => set('unloadingInstructions', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.accessInstructions')} htmlFor="loc-access" className="form-span-all">
        <textarea id="loc-access" rows={2} value={form.accessInstructions ?? ''} onChange={(e) => set('accessInstructions', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.driverInstructions')} htmlFor="loc-driver" className="form-span-all">
        <textarea id="loc-driver" rows={2} value={form.driverInstructions ?? ''} onChange={(e) => set('driverInstructions', e.target.value || null)} disabled={submitting} />
      </FormField>
      <div className="location-form-internal form-span-all">
        <p className="location-form-internal-label">{t('locations.internalOnly')}</p>
        <FormField label={t('locations.form.fields.internalMemo')} htmlFor="loc-memo" hint={t('locations.form.hints.internalMemo')}>
          <textarea id="loc-memo" rows={2} value={form.internalMemo ?? ''} onChange={(e) => set('internalMemo', e.target.value || null)} disabled={submitting} />
        </FormField>
        <FormField label={t('locations.form.fields.notes')} htmlFor="loc-notes">
          <textarea id="loc-notes" rows={3} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={submitting} />
        </FormField>
      </div>
    </FormSection>
  )

  const renderPlanning = () => (
    <FormSection title={t('locations.form.sectionTitles.planningDefaults')} columns={2}>
      <FormField label={t('locations.form.fields.defaultLoadingMinutes')} htmlFor="loc-load-min">
        <input
          id="loc-load-min"
          type="number"
          min="0"
          max="1440"
          step="1"
          value={form.defaultLoadingMinutes ?? ''}
          onChange={(e) => set('defaultLoadingMinutes', e.target.value === '' ? null : Number(e.target.value))}
          disabled={submitting}
        />
      </FormField>
      <FormField label={t('locations.form.fields.defaultUnloadingMinutes')} htmlFor="loc-unload-min">
        <input
          id="loc-unload-min"
          type="number"
          min="0"
          max="1440"
          step="1"
          value={form.defaultUnloadingMinutes ?? ''}
          onChange={(e) => set('defaultUnloadingMinutes', e.target.value === '' ? null : Number(e.target.value))}
          disabled={submitting}
        />
      </FormField>
      <FormField label={t('locations.form.fields.preferredFrom')} htmlFor="loc-pref-from">
        <input id="loc-pref-from" type="time" value={form.preferredArrivalFrom ?? ''} onChange={(e) => set('preferredArrivalFrom', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.preferredTo')} htmlFor="loc-pref-to">
        <input id="loc-pref-to" type="time" value={form.preferredArrivalTo ?? ''} onChange={(e) => set('preferredArrivalTo', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.earliestArrival')} htmlFor="loc-earliest">
        <input id="loc-earliest" type="time" value={form.earliestArrival ?? ''} onChange={(e) => set('earliestArrival', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label={t('locations.form.fields.latestArrival')} htmlFor="loc-latest">
        <input id="loc-latest" type="time" value={form.latestArrival ?? ''} onChange={(e) => set('latestArrival', e.target.value || null)} disabled={submitting} />
      </FormField>
    </FormSection>
  )

  const renderers: Record<string, () => ReactNode> = {
    algemeen: renderAlgemeen,
    adres: renderAdres,
    contact: renderContact,
    openingstijden: renderOpeningstijden,
    operationeel: renderOperationeel,
    instructies: renderInstructies,
    planning: renderPlanning,
  }

  const sections: SectionDef[] = LOCATION_SECTIONS.map((meta) => ({
    ...meta,
    label: t(meta.label),
    hasError: sectionHasError(meta.id),
    complete: status[meta.id]?.complete,
    filled: status[meta.id]?.filled,
    render: renderers[meta.id],
  }))

  const actionBar = (position: 'top' | 'bottom') => (
    <FormActions dirty={dirty} position={position}>
      <Button type="button" variant="secondary" onClick={onCancel} disabled={submitting}>
        {t('ui.actions.cancel')}
      </Button>
      <Button type="submit" disabled={submitting || checkingDuplicates}>
        {submitting || checkingDuplicates ? t('locations.form.busy') : submitLabel ?? (mode === 'create' ? t('locations.form.createSubmit') : t('ui.actions.save'))}
      </Button>
    </FormActions>
  )

  return (
    <form ref={formRef} className="location-form" onSubmit={handleSubmit} onChange={touch} noValidate>
      <UnsavedChangesGuard when={dirty && !submitting} />
      {shownDuplicates && shownDuplicates.length > 0 && (
        <AddressDuplicateWarning
          candidates={shownDuplicates}
          disabled={submitting || checkingDuplicates}
          onCreateAnyway={() => {
            setOverrideDuplicate(true)
            setDuplicates(null)
            setDismissedSubmitError(submitError)
          }}
        />
      )}
      <ValidationSummary
        message={error}
        fieldErrors={summaryErrors}
        fieldLabels={Object.fromEntries(Object.entries(FIELD_LABEL_KEYS).map(([key, labelKey]) => [key, t(labelKey)]))}
        onSelect={jumpToField}
      />

      {actionBar('top')}

      <SectionedForm
        sections={sections}
        activeId={activeId}
        onActiveChange={setActive}
        orientation="left"
        actions={actionBar('bottom')}
      />
    </form>
  )
}
