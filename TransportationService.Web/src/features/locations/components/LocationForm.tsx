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
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers, getCustomer } from '../../customers/api/customersApi'
import type { CustomerContact, CustomerListItem } from '../../customers/types'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { OpeningHoursEditor } from './OpeningHoursEditor'
import { openingIntervalsValid } from '../openingHours'
import {
  LOCATION_FIELD_IDS,
  LOCATION_SECTIONS,
  LOCATION_SECTION_FIELD_KEYS,
  computeLocationSectionStatus,
} from './locationSections'
import { LOCATION_TYPE_LABELS, LOCATION_TYPES, type LocationInput, type LocationType } from '../types'
import '../pages/location-form.css'

interface LocationFormProps {
  mode: 'create' | 'edit'
  /** Read once at mount (pages early-return while loading, so this is stable). */
  initial: LocationInput
  submitting: boolean
  error?: string | null
  onSubmit: (value: LocationInput) => void
  onCancel: () => void
  submitLabel?: string
}

function contactDisplayName(contact: CustomerContact): string {
  return contact.displayName || [contact.firstName, contact.lastName].filter(Boolean).join(' ')
}

/** User-facing labels for the validation summary. */
const FIELD_LABELS: Record<string, string> = {
  name: 'Naam',
  code: 'Code',
  contactEmail: 'E-mail contactpersoon',
  latitude: 'Breedtegraad',
  longitude: 'Lengtegraad',
  openingIntervals: 'Openingsuren',
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
export function LocationForm({ mode, initial, submitting, error, onSubmit, onCancel, submitLabel }: LocationFormProps) {
  const { hasPermission } = useAuth()
  const canViewSensitive = hasPermission('locations.view_sensitive')

  const [form, setForm] = useState<LocationInput>(initial)
  const [openingValid, setOpeningValid] = useState(() => openingIntervalsValid(initial.openingIntervals))
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [dirty, setDirty] = useState(false)

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
      options.push({ value: customerId, label: known || 'Gekoppelde klant' })
    }
    return options
  }, [customers, customerId, contactsFor])

  function set<K extends keyof LocationInput>(key: K, value: LocationInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
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
    if (!form.name.trim()) errors.name = 'Naam is verplicht.'
    if (mode === 'edit' && !form.code.trim()) {
      // On update the backend requires the (already assigned) code; only create may leave it blank.
      errors.code = 'Code is verplicht.'
    }
    if (form.contactEmail && !form.contactEmail.includes('@')) {
      errors.contactEmail = 'Geef een geldig e-mailadres op.'
    }
    if (form.latitude != null && (form.latitude < -90 || form.latitude > 90)) {
      errors.latitude = 'Breedtegraad moet tussen -90 en 90 liggen.'
    }
    if (form.longitude != null && (form.longitude < -180 || form.longitude > 180)) {
      errors.longitude = 'Lengtegraad moet tussen -180 en 180 liggen.'
    }
    if (!openingValid) errors.openingIntervals = 'Corrigeer eerst de openingsuren.'
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
    setDirty(false)
    onSubmit(payload)
  }

  const summaryErrors: FieldErrors = Object.fromEntries(
    Object.entries(fieldErrors).map(([key, message]) => [key, [message]]),
  )

  const sectionHasError = (id: string) =>
    (LOCATION_SECTION_FIELD_KEYS[id] ?? []).some((key) => fieldErrors[key])
  const status = computeLocationSectionStatus(form, mode)

  // ---- Section bodies ---------------------------------------------------------------------

  const renderAlgemeen = () => (
    <FormSection title="Algemeen" columns={2}>
      <FormField label="Naam" htmlFor="loc-name" required error={fieldErrors.name}>
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
        label="Code"
        htmlFor="loc-code"
        hint={mode === 'create' ? 'Leeg laten voor automatische code.' : undefined}
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
      <FormField label="Type" htmlFor="loc-type">
        <select id="loc-type" value={form.type} onChange={(e) => set('type', e.target.value as LocationType)} disabled={submitting}>
          {LOCATION_TYPES.map((t) => (
            <option key={t} value={t}>
              {LOCATION_TYPE_LABELS[t]}
            </option>
          ))}
        </select>
      </FormField>
      <FormField label="Externe referentie" htmlFor="loc-ext-ref" hint="Referentie van de klant of partner voor deze locatie.">
        <input
          id="loc-ext-ref"
          value={form.externalReference ?? ''}
          onChange={(e) => set('externalReference', e.target.value || null)}
          disabled={submitting}
          maxLength={100}
        />
      </FormField>
      <FormField label="Klant" htmlFor="loc-customer" hint="Typ om te zoeken; leeg = geen klant gekoppeld.">
        <SearchableSelect
          id="loc-customer"
          value={form.customerId}
          onChange={selectCustomer}
          options={customerOptions}
          placeholder="Geen klant gekoppeld"
          disabled={submitting}
          ariaLabel="Klant"
        />
      </FormField>
      {mode === 'edit' && (
        <div className="location-form-checkboxes">
          <label className="location-checkbox">
            <input type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} disabled={submitting} />
            <span>Actief</span>
          </label>
        </div>
      )}
    </FormSection>
  )

  const renderAdres = () => (
    <FormSection title="Adres" columns={2}>
      <FormField label="Straat" htmlFor="loc-street">
        <input id="loc-street" value={form.street ?? ''} onChange={(e) => set('street', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="Nummer" htmlFor="loc-house">
        <input id="loc-house" value={form.houseNumber ?? ''} onChange={(e) => set('houseNumber', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="Postcode" htmlFor="loc-postal">
        <input id="loc-postal" value={form.postalCode ?? ''} onChange={(e) => set('postalCode', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="Gemeente" htmlFor="loc-city">
        <input id="loc-city" value={form.city ?? ''} onChange={(e) => set('city', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="Land" htmlFor="loc-country">
        <CountryCombobox id="loc-country" value={form.countryCode} onChange={(code) => { set('countryCode', code); touch() }} disabled={submitting} />
      </FormField>
      <details className="location-form-advanced form-span-all">
        <summary>Geavanceerd — coördinaten</summary>
        <div className="location-form-advanced-grid">
          <FormField label="Breedtegraad" htmlFor="loc-lat" error={fieldErrors.latitude}>
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
          <FormField label="Lengtegraad" htmlFor="loc-lng" error={fieldErrors.longitude}>
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
    <FormSection title="Contactpersoon ter plaatse" columns={2}>
      {form.customerId && (
        <FormField
          label="Contactpersoon van klant"
          htmlFor="loc-customer-contact"
          hint="Kies om onderstaande velden in te vullen; ze blijven aanpasbaar."
          className="form-span-all"
        >
          <select id="loc-customer-contact" value={form.customerContactId ?? ''} onChange={(e) => selectCustomerContact(e.target.value)} disabled={submitting}>
            <option value="">— Geen koppeling —</option>
            {contacts.map((c) => (
              <option key={c.id} value={c.id}>
                {contactDisplayName(c)}
              </option>
            ))}
          </select>
        </FormField>
      )}
      <FormField label="Naam contactpersoon" htmlFor="loc-contact-name">
        <input id="loc-contact-name" value={form.contactName ?? ''} onChange={(e) => set('contactName', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="Telefoon" htmlFor="loc-contact-phone">
        <input id="loc-contact-phone" value={form.contactPhone ?? ''} onChange={(e) => set('contactPhone', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="Gsm" htmlFor="loc-contact-mobile">
        <input id="loc-contact-mobile" value={form.contactMobile ?? ''} onChange={(e) => set('contactMobile', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="E-mail" htmlFor="loc-contact-email" error={fieldErrors.contactEmail}>
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
    <FormSection title="Openingstijden" columns={1}>
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
      <FormField label="Openingsuren (vrije tekst, fallback)" htmlFor="loc-hours" hint="Wordt getoond wanneer er geen tijdvakken zijn ingesteld.">
        <input
          id="loc-hours"
          value={form.openingHours ?? ''}
          onChange={(e) => set('openingHours', e.target.value || null)}
          disabled={submitting}
          placeholder="Bv. ma-vr 08:00-18:00"
          maxLength={500}
        />
      </FormField>
    </FormSection>
  )

  const renderOperationeel = () => (
    <>
      <FormSection title="Terrein" columns={2}>
        <FormField label="Poort" htmlFor="loc-gate">
          <input id="loc-gate" value={form.gate ?? ''} onChange={(e) => set('gate', e.target.value || null)} disabled={submitting} maxLength={50} />
        </FormField>
        <FormField label="Aanmeldpunt" htmlFor="loc-reception">
          <input id="loc-reception" value={form.receptionPoint ?? ''} onChange={(e) => set('receptionPoint', e.target.value || null)} disabled={submitting} maxLength={200} />
        </FormField>
        <FormField label="Kade/dok" htmlFor="loc-dock">
          <input id="loc-dock" value={form.dock ?? ''} onChange={(e) => set('dock', e.target.value || null)} disabled={submitting} maxLength={50} />
        </FormField>
        <FormField label="Routebeschrijving" htmlFor="loc-route" className="form-span-all">
          <textarea id="loc-route" rows={2} value={form.routeDescription ?? ''} onChange={(e) => set('routeDescription', e.target.value || null)} disabled={submitting} />
        </FormField>
        <div className="location-form-checkboxes form-span-all">
          <label className="location-checkbox">
            <input type="checkbox" checked={form.craneRequired} onChange={(e) => set('craneRequired', e.target.checked)} disabled={submitting} />
            <span>Kraan vereist</span>
          </label>
          <label className="location-checkbox">
            <input type="checkbox" checked={form.forkliftAvailable} onChange={(e) => set('forkliftAvailable', e.target.checked)} disabled={submitting} />
            <span>Heftruck beschikbaar</span>
          </label>
        </div>
      </FormSection>

      <FormSection title="Toegang" columns={2}>
        {canViewSensitive && (
          <FormField label="Toegangscode" htmlFor="loc-access-code" hint="Vertrouwelijk — alleen zichtbaar met de juiste rechten.">
            <input id="loc-access-code" value={form.accessCode ?? ''} onChange={(e) => set('accessCode', e.target.value || null)} disabled={submitting} maxLength={100} />
          </FormField>
        )}
        <FormField label="Toegangsrestricties" htmlFor="loc-access-restr">
          <input id="loc-access-restr" value={form.accessRestrictions ?? ''} onChange={(e) => set('accessRestrictions', e.target.value || null)} disabled={submitting} />
        </FormField>
        <div className="location-form-checkboxes form-span-all">
          <label className="location-checkbox">
            <input type="checkbox" checked={form.appointmentRequired} onChange={(e) => set('appointmentRequired', e.target.checked)} disabled={submitting} />
            <span>Afspraak verplicht</span>
          </label>
          <label className="location-checkbox">
            <input type="checkbox" checked={form.deliveryByAppointmentOnly} onChange={(e) => set('deliveryByAppointmentOnly', e.target.checked)} disabled={submitting} />
            <span>Leveren enkel op afspraak</span>
          </label>
          <label className="location-checkbox">
            <input type="checkbox" checked={form.alfapassRequired} onChange={(e) => set('alfapassRequired', e.target.checked)} disabled={submitting} />
            <span>Alfapass vereist</span>
          </label>
        </div>
      </FormSection>

      <FormSection title="Beperkingen" columns={2}>
        <FormField label="Hoogtebeperking (m)" htmlFor="loc-height">
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
        <FormField label="Gewichtsbeperking (t)" htmlFor="loc-weight">
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
        <FormField label="ADR toegelaten" htmlFor="loc-adr" hint="Onbekend = niet ingevuld.">
          <select
            id="loc-adr"
            value={form.adrAllowed === null ? '' : String(form.adrAllowed)}
            onChange={(e) => set('adrAllowed', e.target.value === '' ? null : e.target.value === 'true')}
            disabled={submitting}
          >
            <option value="">Onbekend</option>
            <option value="true">Ja</option>
            <option value="false">Nee</option>
          </select>
        </FormField>
        <FormField label="Voertuigbeperkingen" htmlFor="loc-vehicle-restr">
          <input id="loc-vehicle-restr" value={form.vehicleRestrictions ?? ''} onChange={(e) => set('vehicleRestrictions', e.target.value || null)} disabled={submitting} />
        </FormField>
        <FormField label="Opleggerrestricties" htmlFor="loc-trailer-restr">
          <input id="loc-trailer-restr" value={form.trailerRestrictions ?? ''} onChange={(e) => set('trailerRestrictions', e.target.value || null)} disabled={submitting} />
        </FormField>
      </FormSection>
    </>
  )

  const renderInstructies = () => (
    <FormSection title="Instructies" columns={1}>
      <FormField label="Laadinstructies" htmlFor="loc-loading" className="form-span-all">
        <textarea id="loc-loading" rows={2} value={form.loadingInstructions ?? ''} onChange={(e) => set('loadingInstructions', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="Losinstructies" htmlFor="loc-unloading" className="form-span-all">
        <textarea id="loc-unloading" rows={2} value={form.unloadingInstructions ?? ''} onChange={(e) => set('unloadingInstructions', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="Toegangsinstructies" htmlFor="loc-access" className="form-span-all">
        <textarea id="loc-access" rows={2} value={form.accessInstructions ?? ''} onChange={(e) => set('accessInstructions', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="Chauffeursinstructies" htmlFor="loc-driver" className="form-span-all">
        <textarea id="loc-driver" rows={2} value={form.driverInstructions ?? ''} onChange={(e) => set('driverInstructions', e.target.value || null)} disabled={submitting} />
      </FormField>
      <div className="location-form-internal form-span-all">
        <p className="location-form-internal-label">Alleen interne gebruikers</p>
        <FormField label="Interne memo" htmlFor="loc-memo" hint="Nooit zichtbaar voor chauffeurs of klanten.">
          <textarea id="loc-memo" rows={2} value={form.internalMemo ?? ''} onChange={(e) => set('internalMemo', e.target.value || null)} disabled={submitting} />
        </FormField>
        <FormField label="Notities" htmlFor="loc-notes">
          <textarea id="loc-notes" rows={3} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={submitting} />
        </FormField>
      </div>
    </FormSection>
  )

  const renderPlanning = () => (
    <FormSection title="Planningsstandaarden" columns={2}>
      <FormField label="Standaard laadtijd (min)" htmlFor="loc-load-min">
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
      <FormField label="Standaard lostijd (min)" htmlFor="loc-unload-min">
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
      <FormField label="Voorkeursvenster van" htmlFor="loc-pref-from">
        <input id="loc-pref-from" type="time" value={form.preferredArrivalFrom ?? ''} onChange={(e) => set('preferredArrivalFrom', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="Voorkeursvenster tot" htmlFor="loc-pref-to">
        <input id="loc-pref-to" type="time" value={form.preferredArrivalTo ?? ''} onChange={(e) => set('preferredArrivalTo', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="Vroegste aankomst" htmlFor="loc-earliest">
        <input id="loc-earliest" type="time" value={form.earliestArrival ?? ''} onChange={(e) => set('earliestArrival', e.target.value || null)} disabled={submitting} />
      </FormField>
      <FormField label="Laatste aankomst" htmlFor="loc-latest">
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
    hasError: sectionHasError(meta.id),
    complete: status[meta.id]?.complete,
    filled: status[meta.id]?.filled,
    render: renderers[meta.id],
  }))

  const actionBar = (position: 'top' | 'bottom') => (
    <FormActions dirty={dirty} position={position}>
      <Button type="button" variant="secondary" onClick={onCancel} disabled={submitting}>
        Annuleren
      </Button>
      <Button type="submit" disabled={submitting}>
        {submitting ? 'Bezig…' : submitLabel ?? (mode === 'create' ? 'Locatie aanmaken' : 'Opslaan')}
      </Button>
    </FormActions>
  )

  return (
    <form ref={formRef} className="location-form" onSubmit={handleSubmit} onChange={touch} noValidate>
      <UnsavedChangesGuard when={dirty && !submitting} />
      <ValidationSummary message={error} fieldErrors={summaryErrors} fieldLabels={FIELD_LABELS} onSelect={jumpToField} />

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
