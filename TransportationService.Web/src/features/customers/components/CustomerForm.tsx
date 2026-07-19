import { useState, type FormEvent } from 'react'
import { FormField } from '../../../components/ui/FormField'
import { Button } from '../../../components/ui/Button'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import type { CustomerDetail, CustomerInput, UpdateCustomerInput } from '../types'
import './customers.css'

interface CustomerFormProps {
  mode: 'create' | 'edit'
  initial?: CustomerDetail
  isSubmitting: boolean
  submitError: string | null
  onSubmit: (values: UpdateCustomerInput) => void
  onCancel: () => void
}

function nullable(value: string): string | null {
  const trimmed = value.trim()
  return trimmed ? trimmed : null
}

export function CustomerForm({ mode, initial, isSubmitting, submitError, onSubmit, onCancel }: CustomerFormProps) {
  const categories = useLookupOptions('/api/customer-categories')
  const languages = useLookupOptions('/api/languages')

  const [name, setName] = useState(initial?.name ?? '')
  const [legalName, setLegalName] = useState(initial?.legalName ?? '')
  const [vatNumber, setVatNumber] = useState(initial?.vatNumber ?? '')
  const [categoryId, setCategoryId] = useState(initial?.categoryId ?? '')
  const [email, setEmail] = useState(initial?.email ?? '')
  const [phoneNumber, setPhoneNumber] = useState(initial?.phoneNumber ?? '')
  const [website, setWebsite] = useState(initial?.website ?? '')
  const [street, setStreet] = useState(initial?.street ?? '')
  const [houseNumber, setHouseNumber] = useState(initial?.houseNumber ?? '')
  const [postalCode, setPostalCode] = useState(initial?.postalCode ?? '')
  const [city, setCity] = useState(initial?.city ?? '')
  const [countryCode, setCountryCode] = useState(initial?.countryCode ?? '')
  const [invoiceEmail, setInvoiceEmail] = useState(initial?.invoiceEmail ?? '')
  const [paymentTermDays, setPaymentTermDays] = useState(String(initial?.paymentTermDays ?? 30))
  const [defaultLanguageCode, setDefaultLanguageCode] = useState(initial?.defaultLanguageCode ?? '')
  const [notes, setNotes] = useState(initial?.notes ?? '')
  const [isActive, setIsActive] = useState(initial?.isActive ?? true)
  const [nameError, setNameError] = useState<string | undefined>(undefined)

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!name.trim()) {
      setNameError('Naam is verplicht.')
      return
    }
    setNameError(undefined)

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
    }
    onSubmit({ ...base, isActive })
  }

  return (
    <form onSubmit={handleSubmit} className="customer-form">
      {submitError && (
        <p className="ui-form-field-error" role="alert">
          {submitError}
        </p>
      )}

      <fieldset className="customer-form-section">
        <legend>Algemeen</legend>
        <div className="customer-form-grid">
          <FormField label="Naam" htmlFor="c-name" error={nameError} required>
            <input id="c-name" value={name} onChange={(e) => setName(e.target.value)} aria-invalid={nameError ? 'true' : undefined} maxLength={200} />
          </FormField>
          <FormField label="Juridische naam" htmlFor="c-legal">
            <input id="c-legal" value={legalName} onChange={(e) => setLegalName(e.target.value)} maxLength={200} />
          </FormField>
          <FormField label="BTW-nummer" htmlFor="c-vat">
            <input id="c-vat" value={vatNumber} onChange={(e) => setVatNumber(e.target.value)} maxLength={30} />
          </FormField>
          <FormField label="Categorie" htmlFor="c-category">
            <select id="c-category" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
              <option value="">— Geen —</option>
              {categories.options.map((option) => (
                <option key={option.id} value={option.id}>
                  {option.name}
                </option>
              ))}
            </select>
          </FormField>
        </div>
      </fieldset>

      <fieldset className="customer-form-section">
        <legend>Contact</legend>
        <div className="customer-form-grid">
          <FormField label="E-mail" htmlFor="c-email">
            <input id="c-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} maxLength={250} />
          </FormField>
          <FormField label="Telefoon" htmlFor="c-phone">
            <input id="c-phone" value={phoneNumber} onChange={(e) => setPhoneNumber(e.target.value)} maxLength={30} />
          </FormField>
          <FormField label="Website" htmlFor="c-website">
            <input id="c-website" value={website} onChange={(e) => setWebsite(e.target.value)} maxLength={200} />
          </FormField>
        </div>
      </fieldset>

      <fieldset className="customer-form-section">
        <legend>Adres</legend>
        <div className="customer-form-grid">
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
          <FormField label="Land" htmlFor="c-country">
            <CountryCombobox id="c-country" value={countryCode || null} onChange={(code) => setCountryCode(code ?? '')} />
          </FormField>
        </div>
      </fieldset>

      <fieldset className="customer-form-section">
        <legend>Facturatie</legend>
        <div className="customer-form-grid">
          <FormField label="Facturatie-e-mail" htmlFor="c-invoice-email">
            <input id="c-invoice-email" type="email" value={invoiceEmail} onChange={(e) => setInvoiceEmail(e.target.value)} maxLength={250} />
          </FormField>
          <FormField label="Betaaltermijn (dagen)" htmlFor="c-payterm">
            <input id="c-payterm" type="number" min={0} value={paymentTermDays} onChange={(e) => setPaymentTermDays(e.target.value)} />
          </FormField>
          <FormField label="Voorkeurstaal" htmlFor="c-lang">
            <select id="c-lang" value={defaultLanguageCode} onChange={(e) => setDefaultLanguageCode(e.target.value)}>
              <option value="">— Geen —</option>
              {languages.options.map((option) => (
                <option key={option.id} value={option.code}>
                  {option.name}
                </option>
              ))}
            </select>
          </FormField>
        </div>
      </fieldset>

      <FormField label="Notities" htmlFor="c-notes">
        <textarea id="c-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} maxLength={2000} />
      </FormField>

      {mode === 'edit' && (
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          Actief
        </label>
      )}

      <div className="customer-form-actions">
        <Button variant="secondary" onClick={onCancel} disabled={isSubmitting}>
          Annuleren
        </Button>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Opslaan...' : 'Opslaan'}
        </Button>
      </div>
    </form>
  )
}
