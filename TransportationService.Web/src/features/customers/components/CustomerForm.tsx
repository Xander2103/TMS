import { useState, type FormEvent } from 'react'
import { FormField } from '../../../components/ui/FormField'
import { Button } from '../../../components/ui/Button'
import { FormSection } from '../../../components/ui/FormSection'
import { FormActions } from '../../../components/ui/FormActions'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { validateVatNumber } from '../utils/vatNumber'
import { VAT_TREATMENT_LABELS, type CustomerDetail, type CustomerInput, type UpdateCustomerInput, type VatTreatment } from '../types'
import './customers.css'

interface CustomerFormProps {
  mode: 'create' | 'edit'
  initial?: CustomerDetail
  isSubmitting: boolean
  submitError: string | null
  /** Per-field backend validation messages, shown next to the fields + in the summary. */
  serverFieldErrors?: FieldErrors
  onSubmit: (values: UpdateCustomerInput) => void
  onCancel: () => void
}

/** User-facing labels for backend field paths, for the validation summary. */
const FIELD_LABELS: Record<string, string> = {
  name: 'Naam',
  vatNumber: 'BTW-nummer',
  countryCode: 'Land',
  vatCountryCode: 'BTW-land',
  defaultVatRatePercent: 'Standaard BTW-tarief',
  'initialContact.firstName': 'Contactpersoon — voornaam',
  'initialContact.lastName': 'Contactpersoon — achternaam',
}

const STANDARD_VAT_RATES = ['0', '6', '12', '21'] as const

function nullable(value: string): string | null {
  const trimmed = value.trim()
  return trimmed ? trimmed : null
}

function initialRateChoice(rate: number | null | undefined): { choice: string; custom: string } {
  if (rate === null || rate === undefined) return { choice: '', custom: '' }
  const asString = String(rate)
  return (STANDARD_VAT_RATES as readonly string[]).includes(asString)
    ? { choice: asString, custom: '' }
    : { choice: 'custom', custom: asString }
}

export function CustomerForm({ mode, initial, isSubmitting, submitError, serverFieldErrors, onSubmit, onCancel }: CustomerFormProps) {
  const languages = useLookupOptions('/api/languages')

  const [name, setName] = useState(initial?.name ?? '')
  const [legalName, setLegalName] = useState(initial?.legalName ?? '')
  const [categoryId, setCategoryId] = useState<string | null>(initial?.categoryId ?? null)
  const [email, setEmail] = useState(initial?.email ?? '')
  const [phoneNumber, setPhoneNumber] = useState(initial?.phoneNumber ?? '')
  const [website, setWebsite] = useState(initial?.website ?? '')
  const [street, setStreet] = useState(initial?.street ?? '')
  const [houseNumber, setHouseNumber] = useState(initial?.houseNumber ?? '')
  const [postalCode, setPostalCode] = useState(initial?.postalCode ?? '')
  const [city, setCity] = useState(initial?.city ?? '')
  const [countryCode, setCountryCode] = useState<string | null>(initial?.countryCode ?? null)

  const [vatNumber, setVatNumber] = useState(initial?.vatNumber ?? '')
  const [vatTreatment, setVatTreatment] = useState<VatTreatment>(initial?.vatTreatment ?? 'DomesticVat')
  const initialRate = initialRateChoice(initial?.defaultVatRatePercent)
  const [vatRateChoice, setVatRateChoice] = useState(initialRate.choice)
  const [customVatRate, setCustomVatRate] = useState(initialRate.custom)
  const [vatCountryCode, setVatCountryCode] = useState<string | null>(initial?.vatCountryCode ?? null)
  const [vatNotes, setVatNotes] = useState(initial?.vatNotes ?? '')
  const [peppolId, setPeppolId] = useState(initial?.peppolId ?? '')
  const [peppolScheme, setPeppolScheme] = useState(initial?.peppolScheme ?? '')

  const [invoiceEmail, setInvoiceEmail] = useState(initial?.invoiceEmail ?? '')
  const [invoiceLanguageCode, setInvoiceLanguageCode] = useState(initial?.invoiceLanguageCode ?? '')
  const [paymentTermDays, setPaymentTermDays] = useState(String(initial?.paymentTermDays ?? 30))
  const [defaultLanguageCode, setDefaultLanguageCode] = useState(initial?.defaultLanguageCode ?? '')
  const [purchaseOrderRequired, setPurchaseOrderRequired] = useState(initial?.purchaseOrderRequired ?? false)
  const [signedDeliveryNoteRequired, setSignedDeliveryNoteRequired] = useState(initial?.signedDeliveryNoteRequired ?? false)
  const [customerReferenceRequired, setCustomerReferenceRequired] = useState(initial?.customerReferenceRequired ?? false)

  const [notes, setNotes] = useState(initial?.notes ?? '')

  // Optional initial contact (create flow only) — same model as the contacts panel.
  const [contactFirstName, setContactFirstName] = useState('')
  const [contactLastName, setContactLastName] = useState('')
  const [contactRole, setContactRole] = useState('')
  const [contactEmail, setContactEmail] = useState('')
  const [contactPhone, setContactPhone] = useState('')
  const [contactIsPrimary, setContactIsPrimary] = useState(true)

  const [nameError, setNameError] = useState<string | undefined>(undefined)
  const [vatError, setVatError] = useState<string | undefined>(undefined)
  const [vatRateError, setVatRateError] = useState<string | undefined>(undefined)
  const [contactErrors, setContactErrors] = useState<{ firstName?: string; lastName?: string }>({})
  const [dirty, setDirty] = useState(false)

  function touch() {
    if (!dirty) setDirty(true)
  }

  function resolveVatRate(): { ok: boolean; value: number | null } {
    if (vatRateChoice === '') return { ok: true, value: null }
    if (vatRateChoice !== 'custom') return { ok: true, value: Number(vatRateChoice) }
    const parsed = Number(customVatRate.replace(',', '.'))
    if (!Number.isFinite(parsed) || parsed < 0 || parsed > 100) return { ok: false, value: null }
    return { ok: true, value: parsed }
  }

  const contactHasInput =
    mode === 'create' &&
    [contactFirstName, contactLastName, contactRole, contactEmail, contactPhone].some((value) => value.trim() !== '')

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    let valid = true
    if (!name.trim()) {
      setNameError('Naam is verplicht.')
      valid = false
    } else {
      setNameError(undefined)
    }

    const vatMessage = validateVatNumber(vatNumber) ?? undefined
    setVatError(vatMessage)
    if (vatMessage) valid = false

    const rate = resolveVatRate()
    if (!rate.ok) {
      setVatRateError('Geef een tarief tussen 0 en 100 op.')
      valid = false
    } else {
      setVatRateError(undefined)
    }

    // The contact is optional, but once any contact field is filled the name is required.
    const nextContactErrors: { firstName?: string; lastName?: string } = {}
    if (contactHasInput) {
      if (!contactFirstName.trim()) nextContactErrors.firstName = 'Voornaam van de contactpersoon is verplicht.'
      if (!contactLastName.trim()) nextContactErrors.lastName = 'Achternaam van de contactpersoon is verplicht.'
    }
    setContactErrors(nextContactErrors)
    if (nextContactErrors.firstName || nextContactErrors.lastName) valid = false

    if (!valid) return

    const base: CustomerInput = {
      name: name.trim(),
      legalName: nullable(legalName),
      vatNumber: nullable(vatNumber),
      categoryId: categoryId || null,
      email: nullable(email),
      phoneNumber: nullable(phoneNumber),
      website: nullable(website),
      street: nullable(street),
      houseNumber: nullable(houseNumber),
      postalCode: nullable(postalCode),
      city: nullable(city),
      countryCode: countryCode || null,
      invoiceEmail: nullable(invoiceEmail),
      paymentTermDays: Number.isFinite(Number(paymentTermDays)) ? Number(paymentTermDays) : 0,
      defaultLanguageCode: defaultLanguageCode || null,
      notes: nullable(notes),
      vatTreatment,
      defaultVatRatePercent: rate.value,
      vatCountryCode: vatCountryCode || null,
      vatNotes: nullable(vatNotes),
      peppolId: nullable(peppolId),
      peppolScheme: nullable(peppolScheme),
      invoiceLanguageCode: invoiceLanguageCode || null,
      purchaseOrderRequired,
      signedDeliveryNoteRequired,
      customerReferenceRequired,
      initialContact: contactHasInput
        ? {
            firstName: contactFirstName.trim(),
            lastName: contactLastName.trim(),
            role: nullable(contactRole),
            email: nullable(contactEmail),
            phoneNumber: nullable(contactPhone),
            isPrimary: contactIsPrimary,
            notes: null,
          }
        : null,
    }
    // Clear the guard before the parent saves + navigates away. Activation is a dedicated
    // detail-page action; the form only preserves the current state.
    setDirty(false)
    onSubmit({ ...base, isActive: initial?.isActive ?? true })
  }

  return (
    <form onSubmit={handleSubmit} className="customer-form" onChange={touch}>
      <UnsavedChangesGuard when={dirty && !isSubmitting} />
      <ValidationSummary message={submitError} fieldErrors={serverFieldErrors} fieldLabels={FIELD_LABELS} />

      <FormActions position="top" dirty={dirty}>
        <Button variant="secondary" onClick={onCancel} disabled={isSubmitting}>
          Annuleren
        </Button>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Opslaan...' : 'Opslaan'}
        </Button>
      </FormActions>

      <FormSection title="Algemeen" columns={2}>
        <FormField label="Naam" htmlFor="c-name" error={nameError ?? getFieldError(serverFieldErrors, 'name')} required>
          <input id="c-name" value={name} onChange={(e) => setName(e.target.value)} aria-invalid={nameError ? 'true' : undefined} maxLength={200} />
        </FormField>
        <FormField label="Juridische naam" htmlFor="c-legal">
          <input id="c-legal" value={legalName} onChange={(e) => setLegalName(e.target.value)} maxLength={200} />
        </FormField>
        <FormField label="Categorie" htmlFor="c-category" hint="Commerciële classificatie van deze klant.">
          <LookupSelect
            id="c-category"
            basePath="/api/customer-categories"
            managePermission="customer_categories.manage"
            singular="klantcategorie"
            value={categoryId}
            onChange={(v) => {
              setCategoryId(v)
              touch()
            }}
            placeholder="— Geen categorie —"
          />
        </FormField>
      </FormSection>

      <FormSection title="Contact" columns={3}>
        <FormField label="E-mail" htmlFor="c-email">
          <input id="c-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} maxLength={250} />
        </FormField>
        <FormField label="Telefoon" htmlFor="c-phone">
          <input id="c-phone" value={phoneNumber} onChange={(e) => setPhoneNumber(e.target.value)} maxLength={30} />
        </FormField>
        <FormField label="Website" htmlFor="c-website">
          <input id="c-website" value={website} onChange={(e) => setWebsite(e.target.value)} maxLength={200} />
        </FormField>
        <FormField label="Voorkeurstaal" htmlFor="c-lang" hint="Taal voor algemene communicatie.">
          <select id="c-lang" value={defaultLanguageCode} onChange={(e) => setDefaultLanguageCode(e.target.value)}>
            <option value="">— Geen —</option>
            {languages.options.map((option) => (
              <option key={option.id} value={option.code}>
                {option.name}
              </option>
            ))}
          </select>
        </FormField>
      </FormSection>

      <FormSection title="Adres" columns={3}>
        <FormField label="Straat" htmlFor="c-street">
          <input id="c-street" value={street} onChange={(e) => setStreet(e.target.value)} maxLength={150} />
        </FormField>
        <FormField label="Nummer" htmlFor="c-houseno">
          <input id="c-houseno" value={houseNumber} onChange={(e) => setHouseNumber(e.target.value)} maxLength={20} />
        </FormField>
        <FormField label="Postcode" htmlFor="c-postal">
          <input id="c-postal" value={postalCode} onChange={(e) => setPostalCode(e.target.value)} maxLength={20} />
        </FormField>
        <FormField label="Plaats" htmlFor="c-city">
          <input id="c-city" value={city} onChange={(e) => setCity(e.target.value)} maxLength={100} />
        </FormField>
        <FormField label="Land" htmlFor="c-country" error={getFieldError(serverFieldErrors, 'countryCode')}>
          <CountryCombobox
            id="c-country"
            value={countryCode}
            onChange={(code) => {
              setCountryCode(code)
              touch()
            }}
          />
        </FormField>
      </FormSection>

      <FormSection
        title="BTW & Peppol"
        columns={3}
        description="BTW-regime en e-facturatiegegevens. De BTW-behandeling bepaalt hóe gefactureerd wordt; het tarief bepaalt het percentage."
      >
        <FormField
          label="BTW-nummer"
          htmlFor="c-vat"
          hint="Belgische nummers worden gecontroleerd (BE + 10 cijfers)."
          error={vatError ?? getFieldError(serverFieldErrors, 'vatNumber')}
        >
          <input
            id="c-vat"
            value={vatNumber}
            onChange={(e) => setVatNumber(e.target.value)}
            onBlur={() => setVatError(validateVatNumber(vatNumber) ?? undefined)}
            aria-invalid={vatError || getFieldError(serverFieldErrors, 'vatNumber') ? 'true' : undefined}
            maxLength={30}
          />
        </FormField>
        <FormField label="BTW-behandeling" htmlFor="c-vat-treatment">
          <select id="c-vat-treatment" value={vatTreatment} onChange={(e) => setVatTreatment(e.target.value as VatTreatment)}>
            {Object.entries(VAT_TREATMENT_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Standaard BTW-tarief" htmlFor="c-vat-rate" error={vatRateError} hint="Leeg = bedrijfsstandaard.">
          <div className="customer-form-rate">
            <select id="c-vat-rate" value={vatRateChoice} onChange={(e) => setVatRateChoice(e.target.value)}>
              <option value="">Bedrijfsstandaard</option>
              {STANDARD_VAT_RATES.map((rate) => (
                <option key={rate} value={rate}>
                  {rate}%
                </option>
              ))}
              <option value="custom">Aangepast…</option>
            </select>
            {vatRateChoice === 'custom' && (
              <input
                aria-label="Aangepast BTW-tarief"
                value={customVatRate}
                onChange={(e) => setCustomVatRate(e.target.value)}
                placeholder="bv. 9,5"
                inputMode="decimal"
              />
            )}
          </div>
        </FormField>
        <FormField
          label="BTW-land"
          htmlFor="c-vat-country"
          hint="Alleen invullen als dit afwijkt van het adresland."
          error={getFieldError(serverFieldErrors, 'vatCountryCode')}
        >
          <CountryCombobox
            id="c-vat-country"
            value={vatCountryCode}
            onChange={(code) => {
              setVatCountryCode(code)
              touch()
            }}
            placeholder="— Zelfde als adresland —"
          />
        </FormField>
        <FormField label="Peppol-ID" htmlFor="c-peppol-id" hint="Zonder schema, bv. 0123456789.">
          <input id="c-peppol-id" value={peppolId} onChange={(e) => setPeppolId(e.target.value)} maxLength={64} />
        </FormField>
        <FormField label="Peppol-schema" htmlFor="c-peppol-scheme" hint="4 cijfers, bv. 0208 (ondernemingsnummer).">
          <input id="c-peppol-scheme" value={peppolScheme} onChange={(e) => setPeppolScheme(e.target.value)} maxLength={10} list="peppol-schemes" />
          <datalist id="peppol-schemes">
            <option value="0208">Belgisch ondernemingsnummer</option>
            <option value="9925">Belgisch BTW-nummer</option>
            <option value="0106">Nederlands KvK-nummer</option>
            <option value="9944">Nederlands BTW-nummer</option>
            <option value="0088">GLN</option>
          </datalist>
        </FormField>
        <FormField label="BTW-notities" htmlFor="c-vat-notes" className="form-span-all">
          <textarea id="c-vat-notes" value={vatNotes} onChange={(e) => setVatNotes(e.target.value)} rows={2} maxLength={1000} />
        </FormField>
      </FormSection>

      <FormSection title="Facturatie & vereisten" columns={3}>
        <FormField label="Facturatie-e-mail" htmlFor="c-invoice-email">
          <input id="c-invoice-email" type="email" value={invoiceEmail} onChange={(e) => setInvoiceEmail(e.target.value)} maxLength={250} />
        </FormField>
        <FormField label="Factuurtaal" htmlFor="c-invoice-lang" hint="Leeg = voorkeurstaal.">
          <select id="c-invoice-lang" value={invoiceLanguageCode} onChange={(e) => setInvoiceLanguageCode(e.target.value)}>
            <option value="">— Zelfde als voorkeurstaal —</option>
            {languages.options.map((option) => (
              <option key={option.id} value={option.code}>
                {option.name}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Betaaltermijn (dagen)" htmlFor="c-payterm">
          <input id="c-payterm" type="number" min={0} value={paymentTermDays} onChange={(e) => setPaymentTermDays(e.target.value)} />
        </FormField>
        <div className="customer-form-requirements form-span-all">
          <label className="customer-form-checkbox">
            <input type="checkbox" checked={customerReferenceRequired} onChange={(e) => setCustomerReferenceRequired(e.target.checked)} />
            Klantreferentie verplicht bij elke opdracht
          </label>
          <label className="customer-form-checkbox">
            <input type="checkbox" checked={purchaseOrderRequired} onChange={(e) => setPurchaseOrderRequired(e.target.checked)} />
            Bestelbon (PO) vereist
          </label>
          <label className="customer-form-checkbox">
            <input type="checkbox" checked={signedDeliveryNoteRequired} onChange={(e) => setSignedDeliveryNoteRequired(e.target.checked)} />
            Getekende leverbon (CMR) vereist
          </label>
        </div>
      </FormSection>

      {mode === 'create' && (
        <FormSection
          title="Eerste contactpersoon (optioneel)"
          columns={3}
          collapsible
          defaultOpen={contactHasInput}
          description="Voeg meteen een contactpersoon toe; dit kan ook later op de klantpagina."
        >
          <FormField
            label="Voornaam"
            htmlFor="c-contact-first"
            error={contactErrors.firstName ?? getFieldError(serverFieldErrors, 'initialContact.firstName')}
          >
            <input
              id="c-contact-first"
              value={contactFirstName}
              onChange={(e) => setContactFirstName(e.target.value)}
              aria-invalid={contactErrors.firstName ? 'true' : undefined}
              maxLength={100}
            />
          </FormField>
          <FormField
            label="Achternaam"
            htmlFor="c-contact-last"
            error={contactErrors.lastName ?? getFieldError(serverFieldErrors, 'initialContact.lastName')}
          >
            <input
              id="c-contact-last"
              value={contactLastName}
              onChange={(e) => setContactLastName(e.target.value)}
              aria-invalid={contactErrors.lastName ? 'true' : undefined}
              maxLength={100}
            />
          </FormField>
          <FormField label="Functie" htmlFor="c-contact-role">
            <input id="c-contact-role" value={contactRole} onChange={(e) => setContactRole(e.target.value)} maxLength={100} />
          </FormField>
          <FormField label="E-mail" htmlFor="c-contact-email">
            <input id="c-contact-email" type="email" value={contactEmail} onChange={(e) => setContactEmail(e.target.value)} maxLength={250} />
          </FormField>
          <FormField label="Telefoon" htmlFor="c-contact-phone">
            <input id="c-contact-phone" value={contactPhone} onChange={(e) => setContactPhone(e.target.value)} maxLength={30} />
          </FormField>
          <div className="customer-form-requirements">
            <label className="customer-form-checkbox">
              <input type="checkbox" checked={contactIsPrimary} onChange={(e) => setContactIsPrimary(e.target.checked)} />
              Primaire contactpersoon
            </label>
          </div>
        </FormSection>
      )}

      <FormSection title="Notities" columns={1}>
        <FormField label="Interne notities" htmlFor="c-notes" className="form-span-all">
          <textarea id="c-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} maxLength={2000} />
        </FormField>
      </FormSection>

      <FormActions dirty={dirty}>
        <Button variant="secondary" onClick={onCancel} disabled={isSubmitting}>
          Annuleren
        </Button>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Opslaan...' : 'Opslaan'}
        </Button>
      </FormActions>
    </form>
  )
}
