import { useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { FormField } from '../../../components/ui/FormField'
import { Button } from '../../../components/ui/Button'
import { FormSection } from '../../../components/ui/FormSection'
import { FormActions } from '../../../components/ui/FormActions'
import { SectionedForm, type SectionDef } from '../../../components/ui/SectionedForm'
import { useSectionNavigation, firstSectionWithError } from '../../../components/ui/useSectionNavigation'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { useAuth } from '../../auth/authContextValue'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { getVatTreatments, registryLookup, getPeppolSchemes, verifyCustomerPeppol } from '../api/customersApi'
import { getLegalEntityOptions } from '../../legal-entities/api/legalEntitiesApi'
import type { LegalEntityOption } from '../../legal-entities/types'
import { validateVatNumber } from '../utils/vatNumber'
import { resolveRateOptions } from '../utils/vatTreatment'
import { PeppolFieldGroup } from './PeppolFieldGroup'
import { combinePeppolValue, peppolFormatError } from '../utils/peppolValue'
import { CUSTOMER_SECTION_FIELD_KEYS, isKlantgegevensFieldKey } from './customerSections'
import {
  addContactRow,
  contactRowsToPayload,
  createContactRow,
  payloadIndexByKey,
  removeContactRow,
  updateContactRow,
  validateContactRows,
  type ContactRow,
} from '../utils/contactRows'
import {
  CUSTOMER_CONTACT_TYPE_LABELS,
  CUSTOMER_CONTACT_TYPES,
  CUSTOMER_DOCUMENT_STRATEGY_LABELS,
  PEPPOL_DELIVERY_PREFERENCE_LABELS,
  VAT_TREATMENT_LABELS,
  type CompanyRegistryResult,
  type CustomerContactType,
  type CustomerDetail,
  type CustomerDocumentStrategy,
  type CustomerInput,
  type CustomerPeppolVerifyResult,
  type PeppolDeliveryPreference,
  type PeppolScheme,
  type PeppolStatus,
  type UpdateCustomerInput,
  type VatTreatment,
  type VatTreatmentInfo,
} from '../types'
import './customers.css'

/**
 * Which save button triggered the submit: 'save' is the normal flow (navigate/reload by the
 * parent), 'saveAndNew' (create mode only) asks the parent to reset for a fresh entry.
 */
export type CustomerSubmitIntent = 'save' | 'saveAndNew'

interface CustomerFormProps {
  mode: 'create' | 'edit'
  initial?: CustomerDetail
  isSubmitting: boolean
  submitError: string | null
  /** Per-field backend validation messages, shown next to the fields + in the summary. */
  serverFieldErrors?: FieldErrors
  onSubmit: (values: UpdateCustomerInput, intent: CustomerSubmitIntent) => void
  onCancel: () => void
  /**
   * Edit-only self-saving panels embedded in their (sub)sections. The detail page owns the
   * mutations + reload for these, so it passes the wired panel nodes; create mode shows
   * inline blocks / "available after creation" placeholders instead.
   */
  editPanels?: {
    adressen?: ReactNode
    contactpersonen?: ReactNode
    communicatie?: ReactNode
    tarieven?: ReactNode
    historiek?: ReactNode
  }
  /**
   * Create-only slot under "Locaties & adressen": the staged-locations editor. The page owns
   * that state because the staged rows are created via POST /api/locations only after the
   * customer create succeeds.
   */
  stagedLocationsSlot?: ReactNode
}

/** User-facing labels for backend field paths, for the validation summary. */
const FIELD_LABELS: Record<string, string> = {
  name: 'Naam',
  nickname: 'Roepnaam',
  customerNumber: 'Klantnummer',
  companyNumber: 'Ondernemingsnummer',
  vatNumber: 'BTW-nummer',
  countryCode: 'Land',
  vatCountryCode: 'BTW-land',
  defaultVatRatePercent: 'Standaard BTW-tarief',
  currencyCode: 'Valuta',
  iban: 'IBAN',
  bic: 'BIC',
  bankAccountNumber: 'Rekeningnummer',
  notes: 'Interne klantmemo',
  peppolId: 'Peppol-ID',
  peppolScheme: 'Peppol-schema',
  peppolEnabled: 'Facturen via Peppol versturen',
  peppolDeliveryPreference: 'Bezorgvoorkeur',
  buyerReference: 'Kopersreferentie',
}

const FISCAL_PERMISSION_HINT = 'Vereist recht: fiscale gegevens beheren.'

function nullable(value: string): string | null {
  const trimmed = value.trim()
  return trimmed ? trimmed : null
}

function initialRateChoice(rate: number | null | undefined): { choice: string; custom: string } {
  if (rate === null || rate === undefined) return { choice: '', custom: '' }
  const asString = String(rate)
  return ['0', '6', '12', '21'].includes(asString) ? { choice: asString, custom: '' } : { choice: 'custom', custom: asString }
}

type LookupState =
  | { kind: 'idle' }
  | { kind: 'busy' }
  | { kind: 'not-configured' }
  | { kind: 'no-result' }
  | { kind: 'error'; message: string }
  | { kind: 'result'; result: CompanyRegistryResult }

type PeppolVerifyState =
  | { kind: 'idle' }
  | { kind: 'busy' }
  | { kind: 'error'; message: string }
  | { kind: 'result'; result: CustomerPeppolVerifyResult }

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('nl-BE', { dateStyle: 'short', timeStyle: 'short' })
}

export function CustomerForm({ mode, initial, isSubmitting, submitError, serverFieldErrors, onSubmit, onCancel, editPanels, stagedLocationsSlot }: CustomerFormProps) {
  const languages = useLookupOptions('/api/languages')
  const { hasPermission } = useAuth()
  const canManageFiscal = hasPermission('customers.manage_fiscal')
  const canValidatePeppol = hasPermission('peppol.validate')
  const fiscalHint = canManageFiscal ? undefined : FISCAL_PERMISSION_HINT

  // VAT-treatment catalog: the backend owns labels, rates and legal texts. On load failure
  // we fall back to the static labels so the form stays usable.
  const [treatments, setTreatments] = useState<VatTreatmentInfo[]>([])
  useEffect(() => {
    let mounted = true
    getVatTreatments()
      .then((data) => {
        if (mounted) setTreatments(data)
      })
      .catch(() => {
        /* fallback: static labels, default rate options */
      })
    return () => {
      mounted = false
    }
  }, [])

  // Authoritative Peppol scheme catalog (single source: GET /api/customers/peppol-schemes).
  const [peppolSchemes, setPeppolSchemes] = useState<PeppolScheme[]>([])
  useEffect(() => {
    let mounted = true
    getPeppolSchemes()
      .then((data) => {
        if (mounted) setPeppolSchemes(data)
      })
      .catch(() => {
        /* fallback: empty scheme list, manual entry still possible */
      })
    return () => {
      mounted = false
    }
  }, [])

  const [name, setName] = useState(initial?.name ?? '')
  const [nickname, setNickname] = useState(initial?.nickname ?? '')
  // Manual customer number (create flow only); the detail page owns audited number changes.
  const [customerNumber, setCustomerNumber] = useState('')
  const [legalName, setLegalName] = useState(initial?.legalName ?? '')
  const [categoryId, setCategoryId] = useState<string | null>(initial?.categoryId ?? null)
  const [isActive, setIsActive] = useState(initial?.isActive ?? true)
  const [email, setEmail] = useState(initial?.email ?? '')
  const [phoneNumber, setPhoneNumber] = useState(initial?.phoneNumber ?? '')
  const [website, setWebsite] = useState(initial?.website ?? '')
  const [street, setStreet] = useState(initial?.street ?? '')
  const [houseNumber, setHouseNumber] = useState(initial?.houseNumber ?? '')
  const [postalCode, setPostalCode] = useState(initial?.postalCode ?? '')
  const [city, setCity] = useState(initial?.city ?? '')
  const [countryCode, setCountryCode] = useState<string | null>(initial?.countryCode ?? null)

  const [vatNumber, setVatNumber] = useState(initial?.vatNumber ?? '')
  const [companyNumber, setCompanyNumber] = useState(initial?.companyNumber ?? '')
  const [vatTreatment, setVatTreatment] = useState<VatTreatment>(initial?.vatTreatment ?? 'DomesticVat')
  const initialRate = initialRateChoice(initial?.defaultVatRatePercent)
  const [vatRateChoice, setVatRateChoice] = useState(initialRate.choice)
  const [customVatRate, setCustomVatRate] = useState(initialRate.custom)
  const [vatCountryCode, setVatCountryCode] = useState<string | null>(initial?.vatCountryCode ?? null)
  const [vatNotes, setVatNotes] = useState(initial?.vatNotes ?? '')
  const [peppolId, setPeppolId] = useState(initial?.peppolId ?? '')
  const [peppolScheme, setPeppolScheme] = useState(initial?.peppolScheme ?? '')
  const [peppolStatus, setPeppolStatus] = useState<PeppolStatus>(
    initial?.peppolId || initial?.peppolScheme ? 'manual' : 'not-validated',
  )
  const [peppolEnabled, setPeppolEnabled] = useState(initial?.peppolEnabled ?? false)
  const [peppolDeliveryPreference, setPeppolDeliveryPreference] = useState<PeppolDeliveryPreference>(
    initial?.peppolDeliveryPreference ?? 'Peppol',
  )
  const [buyerReference, setBuyerReference] = useState(initial?.buyerReference ?? '')
  const [peppolVerify, setPeppolVerify] = useState<PeppolVerifyState>({ kind: 'idle' })

  const [iban, setIban] = useState(initial?.iban ?? '')
  const [bic, setBic] = useState(initial?.bic ?? '')
  const [bankName, setBankName] = useState(initial?.bankName ?? '')
  const [bankAccountNumber, setBankAccountNumber] = useState(initial?.bankAccountNumber ?? '')
  const [currencyCode, setCurrencyCode] = useState(initial?.currencyCode ?? 'EUR')

  // Legal-entity options for the default billing entity; empty on load failure or no permission.
  const [legalEntities, setLegalEntities] = useState<LegalEntityOption[]>([])
  const [defaultLegalEntityId, setDefaultLegalEntityId] = useState<string>(initial?.defaultLegalEntityId ?? '')
  const [allowedLegalEntityIds, setAllowedLegalEntityIds] = useState<string[]>(initial?.allowedLegalEntityIds ?? [])
  useEffect(() => {
    let mounted = true
    getLegalEntityOptions()
      .then((data) => {
        if (mounted) setLegalEntities(data)
      })
      .catch(() => {
        /* geen rechten of laadfout: lege select met alleen de tenant-standaard */
      })
    return () => {
      mounted = false
    }
  }, [])

  const [invoiceEmail, setInvoiceEmail] = useState(initial?.invoiceEmail ?? '')
  const [invoiceGrouping, setInvoiceGrouping] = useState(initial?.invoiceGrouping ?? 'Manual')
  const [documentStrategy, setDocumentStrategy] = useState(initial?.documentStrategy ?? 'GenerateOwn')
  const [invoiceLanguageCode, setInvoiceLanguageCode] = useState(initial?.invoiceLanguageCode ?? '')
  const [paymentTermDays, setPaymentTermDays] = useState(String(initial?.paymentTermDays ?? 30))
  const [defaultLanguageCode, setDefaultLanguageCode] = useState(initial?.defaultLanguageCode ?? '')
  const [purchaseOrderRequired, setPurchaseOrderRequired] = useState(initial?.purchaseOrderRequired ?? false)
  const [signedDeliveryNoteRequired, setSignedDeliveryNoteRequired] = useState(initial?.signedDeliveryNoteRequired ?? false)
  const [customerReferenceRequired, setCustomerReferenceRequired] = useState(initial?.customerReferenceRequired ?? false)

  const [notes, setNotes] = useState(initial?.notes ?? '')

  // Contact-person repeater (create flow only) — posted via `contacts[]` on the create request.
  const [contactRows, setContactRows] = useState<ContactRow[]>(() => [createContactRow()])
  const [contactRowErrors, setContactRowErrors] = useState<Record<string, string>>({})

  const [nameError, setNameError] = useState<string | undefined>(undefined)
  const [vatError, setVatError] = useState<string | undefined>(undefined)
  const [vatRateError, setVatRateError] = useState<string | undefined>(undefined)
  const [dirty, setDirty] = useState(false)
  const [lookup, setLookup] = useState<LookupState>({ kind: 'idle' })

  const treatmentInfo = useMemo(() => treatments.find((t) => t.treatment === vatTreatment), [treatments, vatTreatment])
  const rateControl = useMemo(() => resolveRateOptions(treatmentInfo, canManageFiscal), [treatmentInfo, canManageFiscal])
  const rateSelectOptions = useMemo(() => {
    const rates = rateControl.rates.map(String)
    // Keep an out-of-catalog stored value visible instead of silently blanking the select.
    if (vatRateChoice && vatRateChoice !== 'custom' && !rates.includes(vatRateChoice)) rates.push(vatRateChoice)
    return rates
  }, [rateControl, vatRateChoice])
  const showCustomOption = rateControl.allowCustom || vatRateChoice === 'custom'
  const vatNumberMissing = (treatmentInfo?.requiresVatNumber ?? false) && vatNumber.trim() === ''

  function touch() {
    if (!dirty) setDirty(true)
  }

  function patchContactRow(key: string, patch: Partial<Omit<ContactRow, 'key'>>) {
    setContactRows((rows) => updateContactRow(rows, key, patch))
    touch()
  }

  function onPeppolChange(next: { scheme: string; participantId: string }) {
    setPeppolScheme(next.scheme)
    setPeppolId(next.participantId)
    setPeppolStatus('manual')
    touch()
  }

  function resolveVatRate(): { ok: boolean; value: number | null } {
    if (rateControl.mode === 'locked') return { ok: true, value: rateControl.lockedRate }
    if (vatRateChoice === '') return { ok: true, value: null }
    if (vatRateChoice !== 'custom') return { ok: true, value: Number(vatRateChoice) }
    const parsed = Number(customVatRate.replace(',', '.'))
    if (!Number.isFinite(parsed) || parsed < 0 || parsed > 100) return { ok: false, value: null }
    return { ok: true, value: parsed }
  }

  async function handleRegistryLookup() {
    const number = vatNumber.trim() || companyNumber.trim()
    if (!number) {
      setLookup({ kind: 'error', message: 'Vul eerst een BTW-nummer of ondernemingsnummer in.' })
      return
    }
    setLookup({ kind: 'busy' })
    try {
      const response = await registryLookup(number)
      if (!response.configured) setLookup({ kind: 'not-configured' })
      else if (!response.result) setLookup({ kind: 'no-result' })
      else setLookup({ kind: 'result', result: response.result })
    } catch {
      setLookup({ kind: 'error', message: 'Het opzoeken is mislukt. Probeer het later opnieuw.' })
    }
  }

  /**
   * Applies registry values: empty fields are filled directly; already-filled fields that
   * differ are only overwritten after an explicit confirmation. Fiscal fields are skipped
   * for users without the fiscal permission (the backend would reject those changes). The
   * grouped Peppol control's status reflects whether its value came from the provider (auto),
   * the provider ran but returned no Peppol (not-found), or a manual value was kept.
   */
  function applyRegistryResult(result: CompanyRegistryResult) {
    const fields: { label: string; value: string | null; current: string; set: (v: string) => void; fiscal?: boolean }[] = [
      { label: 'Officiële naam', value: result.legalName, current: legalName, set: setLegalName },
      { label: 'Ondernemingsnummer', value: result.companyNumber, current: companyNumber, set: setCompanyNumber, fiscal: true },
      { label: 'BTW-nummer', value: result.vatNumber, current: vatNumber, set: setVatNumber, fiscal: true },
      { label: 'Straat', value: result.street, current: street, set: setStreet },
      { label: 'Nummer', value: result.houseNumber, current: houseNumber, set: setHouseNumber },
      { label: 'Postcode', value: result.postalCode, current: postalCode, set: setPostalCode },
      { label: 'Plaats', value: result.city, current: city, set: setCity },
      { label: 'Land', value: result.countryCode, current: countryCode ?? '', set: (v) => setCountryCode(v) },
    ]
    const candidates = fields.filter((f) => f.value !== null && f.value.trim() !== '' && (canManageFiscal || !f.fiscal))
    const conflicts: { label: string; current: string; next: string }[] = candidates
      .filter((f) => f.current.trim() !== '' && f.current.trim() !== f.value?.trim())
      .map((f) => ({ label: f.label, current: f.current, next: f.value?.trim() ?? '' }))

    // Peppol is one atomic identifier (scheme + id). Never apply half of it: a provider
    // scheme must not be glued onto a manually entered id, so any difference from a
    // non-empty manual value is a conflict — even when one of the columns is still empty.
    const providerPeppol = { scheme: result.peppolScheme?.trim() ?? '', id: result.peppolId?.trim() ?? '' }
    const providerHasPeppol = Boolean(providerPeppol.scheme || providerPeppol.id)
    const currentPeppol = combinePeppolValue(peppolScheme.trim(), peppolId.trim())
    const nextPeppol = combinePeppolValue(providerPeppol.scheme, providerPeppol.id)
    const peppolCandidate = canManageFiscal && providerHasPeppol && currentPeppol !== nextPeppol
    if (peppolCandidate && currentPeppol !== '') {
      conflicts.push({ label: 'Peppol-ID', current: currentPeppol, next: nextPeppol })
    }

    let overwrite = false
    if (conflicts.length > 0) {
      overwrite = window.confirm(
        'Deze velden zijn al ingevuld en wijken af van het register:\n\n' +
          conflicts.map((c) => `• ${c.label}: "${c.current}" → "${c.next}"`).join('\n') +
          '\n\nOverschrijven met de registerwaarden?',
      )
    }
    for (const f of candidates) {
      const next = f.value?.trim() ?? ''
      if (f.current.trim() === '' || (overwrite && f.current.trim() !== next)) f.set(next)
    }
    let peppolApplied = false
    if (peppolCandidate && (currentPeppol === '' || overwrite)) {
      setPeppolScheme(providerPeppol.scheme)
      setPeppolId(providerPeppol.id)
      peppolApplied = true
    }
    if (canManageFiscal) {
      if (peppolApplied) setPeppolStatus('auto')
      else if (!providerHasPeppol) setPeppolStatus('not-found')
      // provider had Peppol but the manual value was kept → leave the status unchanged.
    }
    touch()
    setLookup({ kind: 'idle' })
  }

  // De backend controleert de OPGESLAGEN identiteit; een nog niet bewaarde wijziging zou een
  // resultaat tonen dat over het oude ID gaat.
  const peppolIdentityDirty =
    mode === 'edit' &&
    (peppolId.trim() !== (initial?.peppolId ?? '') || peppolScheme.trim() !== (initial?.peppolScheme ?? ''))

  /** Provider-directorycontrole van het Peppol-ID van deze klant (alleen bestaande klanten). */
  async function handlePeppolVerify() {
    if (!initial) return
    setPeppolVerify({ kind: 'busy' })
    try {
      const result = await verifyCustomerPeppol(initial.id)
      setPeppolVerify({ kind: 'result', result })
    } catch (error) {
      setPeppolVerify({
        kind: 'error',
        message: describeApiError(error, 'De Peppol-controle is mislukt. Probeer het later opnieuw.').message,
      })
    }
  }

  // Badge a section when one of its fields is failing (client or server error).
  const peppolProblem = peppolFormatError(combinePeppolValue(peppolScheme, peppolId))
  const combinedErrorKeys = new Set<string>()
  if (nameError) combinedErrorKeys.add('name')
  if (vatError) combinedErrorKeys.add('vatNumber')
  if (peppolProblem) combinedErrorKeys.add('peppolId')
  if (vatRateError) combinedErrorKeys.add('defaultVatRatePercent')
  for (const key of Object.keys(contactRowErrors)) combinedErrorKeys.add(key)
  for (const key of Object.keys(serverFieldErrors ?? {})) combinedErrorKeys.add(key)
  const sectionHasError = (id: string) =>
    id === 'klantgegevens'
      ? [...combinedErrorKeys].some(isKlantgegevensFieldKey)
      : (CUSTOMER_SECTION_FIELD_KEYS[id] ?? []).some((key) => combinedErrorKeys.has(key))

  const isEdit = mode === 'edit'

  // Backend error paths (`contacts[i].…`) index the non-empty rows; map them back per row.
  const contactIndexByKey = payloadIndexByKey(contactRows)
  function contactError(rowKey: string, field: string): string | undefined {
    const index = contactIndexByKey.get(rowKey)
    if (index === undefined) return undefined
    const path = `contacts[${index}].${field}`
    return contactRowErrors[path] ?? getFieldError(serverFieldErrors, path)
  }

  const contactRepeater = (
    <>
      {contactRows.map((row, rowIndex) => (
        <div key={row.key} className="customer-contact-repeater-row">
          <FormField
            label="Voornaam"
            htmlFor={`c-ct-first-${row.key}`}
            required
            error={contactError(row.key, 'firstName')}
          >
            <input
              id={`c-ct-first-${row.key}`}
              value={row.firstName}
              onChange={(e) => patchContactRow(row.key, { firstName: e.target.value })}
              aria-invalid={contactError(row.key, 'firstName') ? 'true' : undefined}
              maxLength={100}
            />
          </FormField>
          <FormField
            label="Achternaam"
            htmlFor={`c-ct-last-${row.key}`}
            required
            error={contactError(row.key, 'lastName')}
          >
            <input
              id={`c-ct-last-${row.key}`}
              value={row.lastName}
              onChange={(e) => patchContactRow(row.key, { lastName: e.target.value })}
              aria-invalid={contactError(row.key, 'lastName') ? 'true' : undefined}
              maxLength={100}
            />
          </FormField>
          <FormField label="Functie" htmlFor={`c-ct-role-${row.key}`}>
            <input
              id={`c-ct-role-${row.key}`}
              value={row.role}
              onChange={(e) => patchContactRow(row.key, { role: e.target.value })}
              maxLength={100}
            />
          </FormField>
          <FormField label="Type" htmlFor={`c-ct-type-${row.key}`} error={contactError(row.key, 'contactType')}>
            <select
              id={`c-ct-type-${row.key}`}
              value={row.contactType}
              onChange={(e) => patchContactRow(row.key, { contactType: e.target.value as CustomerContactType })}
            >
              {CUSTOMER_CONTACT_TYPES.map((type) => (
                <option key={type} value={type}>
                  {CUSTOMER_CONTACT_TYPE_LABELS[type]}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="E-mail" htmlFor={`c-ct-email-${row.key}`}>
            <input
              id={`c-ct-email-${row.key}`}
              type="email"
              value={row.email}
              onChange={(e) => patchContactRow(row.key, { email: e.target.value })}
              maxLength={250}
            />
          </FormField>
          <FormField label="Telefoon" htmlFor={`c-ct-phone-${row.key}`}>
            <input
              id={`c-ct-phone-${row.key}`}
              value={row.phoneNumber}
              onChange={(e) => patchContactRow(row.key, { phoneNumber: e.target.value })}
              maxLength={30}
            />
          </FormField>
          <FormField label="GSM" htmlFor={`c-ct-mobile-${row.key}`}>
            <input
              id={`c-ct-mobile-${row.key}`}
              value={row.mobilePhone}
              onChange={(e) => patchContactRow(row.key, { mobilePhone: e.target.value })}
              maxLength={30}
            />
          </FormField>
          <div className="customer-contact-repeater-footer">
            <label className="customer-form-checkbox">
              <input
                type="checkbox"
                checked={row.isPrimary}
                onChange={(e) => patchContactRow(row.key, { isPrimary: e.target.checked })}
                aria-invalid={contactError(row.key, 'isPrimary') ? 'true' : undefined}
              />
              Primair voor dit type
            </label>
            <Button
              variant="ghost"
              onClick={() => {
                setContactRows((rows) => removeContactRow(rows, row.key))
                touch()
              }}
              aria-label={`Contactpersoon ${rowIndex + 1} verwijderen`}
            >
              Verwijderen
            </Button>
          </div>
          {contactError(row.key, 'isPrimary') && (
            <p className="ui-form-field-error form-span-all" role="alert">
              {contactError(row.key, 'isPrimary')}
            </p>
          )}
        </div>
      ))}
      <Button
        variant="secondary"
        onClick={() => {
          setContactRows((rows) => addContactRow(rows))
          touch()
        }}
      >
        + Contactpersoon toevoegen
      </Button>
    </>
  )

  const sections: SectionDef[] = [
    {
      id: 'klantgegevens',
      label: 'Klantgegevens',
      hasError: sectionHasError('klantgegevens'),
      render: () => (
        <>
          <FormSection title="Bedrijfsgegevens" columns={2}>
            <FormField label="Naam" htmlFor="c-name" error={nameError ?? getFieldError(serverFieldErrors, 'name')} required>
              <input id="c-name" value={name} onChange={(e) => setName(e.target.value)} aria-invalid={nameError ? 'true' : undefined} maxLength={200} />
            </FormField>
            <FormField label="Roepnaam" htmlFor="c-nickname" hint="Korte interne naam, bv. voor planning en zoeken." error={getFieldError(serverFieldErrors, 'nickname')}>
              <input id="c-nickname" value={nickname} onChange={(e) => setNickname(e.target.value)} maxLength={100} />
            </FormField>
            {mode === 'create' && (
              <FormField
                label="Klantnummer"
                htmlFor="c-number"
                hint="Leeg laten voor automatische nummering."
                error={getFieldError(serverFieldErrors, 'customerNumber')}
              >
                <input
                  id="c-number"
                  value={customerNumber}
                  onChange={(e) => setCustomerNumber(e.target.value)}
                  aria-invalid={getFieldError(serverFieldErrors, 'customerNumber') ? 'true' : undefined}
                  maxLength={30}
                />
              </FormField>
            )}
            <FormField label="Officiële naam" htmlFor="c-legal">
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
            {isEdit && (
              <div className="customer-form-requirements">
                <label className="customer-form-checkbox">
                  <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
                  Actief
                </label>
              </div>
            )}
          </FormSection>

          <FormSection title="Algemene contactgegevens" columns={2}>
            <FormField label="Algemeen e-mailadres" htmlFor="c-email">
              <input id="c-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} maxLength={250} />
            </FormField>
            <FormField label="Algemeen telefoonnummer" htmlFor="c-phone">
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
            <FormField
              label="Interne klantmemo"
              htmlFor="c-notes"
              hint="Alleen zichtbaar voor interne gebruikers."
              className="form-span-all"
              error={getFieldError(serverFieldErrors, 'notes')}
            >
              <textarea id="c-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} maxLength={2000} />
            </FormField>
          </FormSection>

          <FormSection
            title="Contactpersonen"
            columns={1}
            description={
              isEdit
                ? undefined
                : 'Voeg meteen één of meer contactpersonen toe; volledig lege rijen worden overgeslagen.'
            }
          >
            <div className="form-span-all">{isEdit ? (editPanels?.contactpersonen ?? null) : contactRepeater}</div>
          </FormSection>

          <FormSection title="Locaties & adressen" columns={3} description="Hoofdzetel / algemeen adres">
            <FormField label="Straat" htmlFor="c-street">
              <input id="c-street" value={street} onChange={(e) => setStreet(e.target.value)} maxLength={150} />
            </FormField>
            <FormField label="Nummer" htmlFor="c-houseno">
              <input id="c-houseno" value={houseNumber} onChange={(e) => setHouseNumber(e.target.value)} maxLength={20} />
            </FormField>
            <FormField label="Postcode" htmlFor="c-postal">
              <input id="c-postal" value={postalCode} onChange={(e) => setPostalCode(e.target.value)} maxLength={20} />
            </FormField>
            <FormField label="Gemeente" htmlFor="c-city">
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
            <div className="form-span-all">
              {isEdit
                ? editPanels?.adressen
                : (stagedLocationsSlot ?? (
                    <p className="customer-form-muted">
                      Bijkomende adressen (laden, leveren, facturatie) beheer je na het aanmaken.
                    </p>
                  ))}
            </div>
          </FormSection>
        </>
      ),
    },
    {
      id: 'fiscaal',
      label: 'Fiscaal & Peppol',
      hasError: sectionHasError('fiscaal'),
      render: () => (
        <FormSection
          title="Fiscaal & Peppol"
          columns={3}
          description={
            fiscalHint ??
            'BTW-regime, ondernemingsnummer en e-facturatiegegevens. De BTW-behandeling bepaalt hóe gefactureerd wordt; het tarief bepaalt het percentage.'
          }
        >
          <FormField
            label="BTW-nummer"
            htmlFor="c-vat"
            hint="Belgische nummers worden gecontroleerd (BE + 10 cijfers)."
            error={vatError ?? getFieldError(serverFieldErrors, 'vatNumber')}
          >
            <>
              <input
                id="c-vat"
                value={vatNumber}
                onChange={(e) => setVatNumber(e.target.value)}
                onBlur={() => setVatError(validateVatNumber(vatNumber) ?? undefined)}
                aria-invalid={vatError || getFieldError(serverFieldErrors, 'vatNumber') ? 'true' : undefined}
                maxLength={30}
                disabled={!canManageFiscal}
              />
              {vatNumberMissing && (
                <p className="customer-form-warning" role="status">
                  BTW-nummer vereist voor deze btw-regeling (blokkeert verzending van facturen).
                </p>
              )}
            </>
          </FormField>
          <FormField
            label="Ondernemingsnummer"
            htmlFor="c-company-number"
            hint="KBO-nummer, bv. 0123.456.749."
            error={getFieldError(serverFieldErrors, 'companyNumber')}
          >
            <input
              id="c-company-number"
              value={companyNumber}
              onChange={(e) => setCompanyNumber(e.target.value)}
              maxLength={30}
              disabled={!canManageFiscal}
            />
          </FormField>
          <div className="customer-form-lookup form-span-all">
            <div className="customer-form-lookup-row">
              <Button variant="secondary" onClick={handleRegistryLookup} disabled={lookup.kind === 'busy' || isSubmitting}>
                {lookup.kind === 'busy' ? 'Opzoeken…' : 'Gegevens opzoeken'}
              </Button>
              {lookup.kind === 'not-configured' && (
                <span className="customer-form-muted">
                  Geen officiële registerkoppeling geconfigureerd — vul de gegevens handmatig in.
                </span>
              )}
              {lookup.kind === 'no-result' && <span className="customer-form-muted">Geen gegevens gevonden voor dit nummer.</span>}
              {lookup.kind === 'error' && (
                <span className="ui-form-field-error" role="alert">
                  {lookup.message}
                </span>
              )}
            </div>
            {lookup.kind === 'result' && (
              <div className="customer-form-lookup-panel">
                <h4>Gevonden in het register</h4>
                <dl>
                  {(
                    [
                      ['Officiële naam', lookup.result.legalName],
                      ['Ondernemingsnummer', lookup.result.companyNumber],
                      ['BTW-nummer', lookup.result.vatNumber],
                      ['Straat', lookup.result.street],
                      ['Nummer', lookup.result.houseNumber],
                      ['Postcode', lookup.result.postalCode],
                      ['Plaats', lookup.result.city],
                      ['Land', lookup.result.countryCode],
                      ['Peppol-ID', combinePeppolValue(lookup.result.peppolScheme ?? '', lookup.result.peppolId ?? '') || null],
                    ] as const
                  )
                    .filter(([, value]) => value)
                    .map(([label, value]) => (
                      <div key={label}>
                        <dt>{label}</dt>
                        <dd>{value}</dd>
                      </div>
                    ))}
                </dl>
                <div className="customer-form-lookup-actions">
                  <Button onClick={() => applyRegistryResult(lookup.result)}>Overnemen</Button>
                  <Button variant="ghost" onClick={() => setLookup({ kind: 'idle' })}>
                    Sluiten
                  </Button>
                </div>
                <p className="customer-form-muted">Lege velden worden ingevuld; afwijkende velden pas na bevestiging overschreven.</p>
              </div>
            )}
          </div>
          <FormField label="BTW-behandeling" htmlFor="c-vat-treatment">
            <select
              id="c-vat-treatment"
              value={vatTreatment}
              onChange={(e) => setVatTreatment(e.target.value as VatTreatment)}
              disabled={!canManageFiscal}
            >
              {treatments.length > 0
                ? treatments.map((info) => (
                    <option key={info.treatment} value={info.treatment}>
                      {info.label}
                    </option>
                  ))
                : Object.entries(VAT_TREATMENT_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
            </select>
          </FormField>
          {rateControl.mode === 'locked' ? (
            <FormField
              label="Standaard BTW-tarief"
              htmlFor="c-vat-rate-locked"
              hint={treatmentInfo?.invoiceLegalText ?? 'Dit tarief ligt vast voor deze btw-regeling.'}
            >
              <input
                id="c-vat-rate-locked"
                value={rateControl.lockedRate !== null ? `${rateControl.lockedRate}%` : 'Bedrijfsstandaard'}
                readOnly
                disabled
              />
            </FormField>
          ) : (
            <FormField label="Standaard BTW-tarief" htmlFor="c-vat-rate" error={vatRateError} hint="Leeg = bedrijfsstandaard.">
              <div className="customer-form-rate">
                <select id="c-vat-rate" value={vatRateChoice} onChange={(e) => setVatRateChoice(e.target.value)} disabled={!canManageFiscal}>
                  <option value="">Bedrijfsstandaard</option>
                  {rateSelectOptions.map((rate) => (
                    <option key={rate} value={rate}>
                      {rate}%
                    </option>
                  ))}
                  {showCustomOption && <option value="custom">Aangepast…</option>}
                </select>
                {vatRateChoice === 'custom' && (
                  <input
                    aria-label="Aangepast BTW-tarief"
                    value={customVatRate}
                    onChange={(e) => setCustomVatRate(e.target.value)}
                    placeholder="bv. 9,5"
                    inputMode="decimal"
                    disabled={!canManageFiscal}
                  />
                )}
              </div>
            </FormField>
          )}
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
              disabled={!canManageFiscal}
            />
          </FormField>
          <div className="form-span-all">
            <PeppolFieldGroup
              scheme={peppolScheme}
              participantId={peppolId}
              status={peppolStatus}
              schemes={peppolSchemes}
              disabled={!canManageFiscal}
              error={getFieldError(serverFieldErrors, 'peppolId') ?? getFieldError(serverFieldErrors, 'peppolScheme')}
              onChange={onPeppolChange}
            />
          </div>
          <div className="customer-form-requirements form-span-all">
            <label className="customer-form-checkbox">
              <input
                type="checkbox"
                checked={peppolEnabled}
                disabled={!canManageFiscal}
                onChange={(e) => setPeppolEnabled(e.target.checked)}
              />
              Facturen via Peppol versturen
            </label>
          </div>
          <FormField
            label="Bezorgvoorkeur"
            htmlFor="c-peppol-delivery"
            hint="Hoe uitgaande facturen bezorgd worden zolang Peppol is ingeschakeld."
            error={getFieldError(serverFieldErrors, 'peppolDeliveryPreference')}
          >
            <select
              id="c-peppol-delivery"
              value={peppolDeliveryPreference}
              disabled={!canManageFiscal || !peppolEnabled}
              onChange={(e) => setPeppolDeliveryPreference(e.target.value as PeppolDeliveryPreference)}
            >
              {(Object.entries(PEPPOL_DELIVERY_PREFERENCE_LABELS) as [PeppolDeliveryPreference, string][]).map(
                ([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ),
              )}
            </select>
          </FormField>
          <FormField
            label="Kopersreferentie (buyer reference)"
            htmlFor="c-buyer-reference"
            hint="Wordt als kopersreferentie op uitgaande Peppol-facturen gezet, bv. een kostenplaats."
            error={getFieldError(serverFieldErrors, 'buyerReference')}
          >
            <input
              id="c-buyer-reference"
              value={buyerReference}
              onChange={(e) => setBuyerReference(e.target.value)}
              maxLength={200}
              disabled={!canManageFiscal}
            />
          </FormField>
          {isEdit && initial && (
            <div className="customer-form-lookup form-span-all">
              {canValidatePeppol && peppolId.trim() !== '' && peppolScheme.trim() !== '' && (
                <div className="customer-form-lookup-row">
                  <Button
                    variant="secondary"
                    onClick={() => void handlePeppolVerify()}
                    disabled={peppolVerify.kind === 'busy' || isSubmitting || peppolIdentityDirty}
                  >
                    {peppolVerify.kind === 'busy' ? 'Controleren…' : 'Peppol-gegevens controleren'}
                  </Button>
                  {peppolIdentityDirty && (
                    <span className="customer-form-muted">
                      Sla de gewijzigde Peppol-gegevens eerst op voordat je ze controleert.
                    </span>
                  )}
                  {peppolVerify.kind === 'error' && (
                    <span className="ui-form-field-error" role="alert">
                      {peppolVerify.message}
                    </span>
                  )}
                </div>
              )}
              {peppolVerify.kind === 'result' ? (
                <div className="customer-form-lookup-panel">
                  {peppolVerify.result.found ? (
                    <>
                      <div>
                        <Badge tone="success">Gevonden in het Peppol-netwerk</Badge>
                      </div>
                      {peppolVerify.result.supportedDocumentTypes.length > 0 && (
                        <p className="customer-form-muted">
                          Ondersteunde documenttypes: {peppolVerify.result.supportedDocumentTypes.join(', ')}
                        </p>
                      )}
                      <p className="customer-form-muted">
                        Laatst gecontroleerd: {formatDateTime(peppolVerify.result.lastCheckedAt)}
                      </p>
                      {peppolVerify.result.reference && (
                        <p className="customer-form-muted">Referentie: {peppolVerify.result.reference}</p>
                      )}
                    </>
                  ) : (
                    <>
                      <div>
                        <Badge tone="danger">Niet gevonden in het Peppol-netwerk</Badge>
                      </div>
                      <p className="customer-form-muted">
                        Laatst gecontroleerd: {formatDateTime(peppolVerify.result.lastCheckedAt)}
                      </p>
                    </>
                  )}
                </div>
              ) : initial.peppolValidationStatus === 'Found' ? (
                <div className="customer-form-lookup-row">
                  <Badge tone="success">Gevonden in het Peppol-netwerk</Badge>
                  {initial.peppolValidatedAt && (
                    <span className="customer-form-muted">
                      Laatst gecontroleerd: {formatDateTime(initial.peppolValidatedAt)}
                    </span>
                  )}
                  {initial.peppolValidationReference && (
                    <span className="customer-form-muted">Referentie: {initial.peppolValidationReference}</span>
                  )}
                </div>
              ) : initial.peppolValidationStatus === 'NotFound' ? (
                <div className="customer-form-lookup-row">
                  <Badge tone="danger">Niet gevonden in het Peppol-netwerk</Badge>
                  {initial.peppolValidatedAt && (
                    <span className="customer-form-muted">
                      Laatst gecontroleerd: {formatDateTime(initial.peppolValidatedAt)}
                    </span>
                  )}
                </div>
              ) : (
                <p className="customer-form-muted">Nog niet gecontroleerd.</p>
              )}
            </div>
          )}
          <FormField label="BTW-notities" htmlFor="c-vat-notes" className="form-span-all">
            <textarea id="c-vat-notes" value={vatNotes} onChange={(e) => setVatNotes(e.target.value)} rows={2} maxLength={1000} disabled={!canManageFiscal} />
          </FormField>
        </FormSection>
      ),
    },
    {
      id: 'bank',
      label: 'Bank',
      optional: true,
      hasError: sectionHasError('bank'),
      render: () => (
        <FormSection
          title="Bank"
          columns={3}
          description={fiscalHint ?? 'Bankrekening en valuta voor facturatie en betalingen.'}
        >
          <FormField label="IBAN" htmlFor="c-iban" error={getFieldError(serverFieldErrors, 'iban')}>
            <input id="c-iban" value={iban} onChange={(e) => setIban(e.target.value)} maxLength={40} disabled={!canManageFiscal} />
          </FormField>
          <FormField label="BIC" htmlFor="c-bic" error={getFieldError(serverFieldErrors, 'bic')}>
            <input id="c-bic" value={bic} onChange={(e) => setBic(e.target.value)} maxLength={11} disabled={!canManageFiscal} />
          </FormField>
          <FormField label="Banknaam" htmlFor="c-bank-name">
            <input id="c-bank-name" value={bankName} onChange={(e) => setBankName(e.target.value)} maxLength={100} disabled={!canManageFiscal} />
          </FormField>
          <FormField
            label="Rekeningnummer (niet-SEPA)"
            htmlFor="c-bank-account"
            hint="Alleen voor rekeningen zonder IBAN."
            error={getFieldError(serverFieldErrors, 'bankAccountNumber')}
          >
            <input
              id="c-bank-account"
              value={bankAccountNumber}
              onChange={(e) => setBankAccountNumber(e.target.value)}
              maxLength={40}
              disabled={!canManageFiscal}
            />
          </FormField>
          <FormField label="Valuta" htmlFor="c-currency" hint="3-letterige ISO-code, standaard EUR." error={getFieldError(serverFieldErrors, 'currencyCode')}>
            <input
              id="c-currency"
              value={currencyCode}
              onChange={(e) => setCurrencyCode(e.target.value.toUpperCase())}
              maxLength={3}
              disabled={!canManageFiscal}
            />
          </FormField>
        </FormSection>
      ),
    },
    {
      id: 'facturatie',
      label: 'Facturatie',
      hasError: sectionHasError('facturatie'),
      render: () => (
        <FormSection title="Facturatie & vereisten" columns={3}>
          <FormField label="Facturatie-e-mail" htmlFor="c-invoice-email">
            <input id="c-invoice-email" type="email" value={invoiceEmail} onChange={(e) => setInvoiceEmail(e.target.value)} maxLength={250} />
          </FormField>
          <FormField
            label="Factuurgroepering"
            htmlFor="c-invoice-grouping"
            hint="Hoe deze klant facturen verwacht. Handmatig = vandaag; automatische voorstellen volgen later."
          >
            <select id="c-invoice-grouping" value={invoiceGrouping} onChange={(e) => setInvoiceGrouping(e.target.value)}>
              <option value="Manual">Handmatig (standaard)</option>
              <option value="PerDossier">Eén factuur per dossier</option>
              <option value="Weekly">Wekelijks verzamelen</option>
              <option value="Monthly">Maandelijks verzamelen</option>
              <option value="ByReference">Per klantreferentie</option>
            </select>
          </FormField>
          <FormField
            label="Transportdocumenten"
            htmlFor="c-document-strategy"
            hint="Bepaalt wie de leveringsbon/CMR aanlevert; per opdracht blijft een afwijkende keuze mogelijk."
          >
            <select id="c-document-strategy" value={documentStrategy} onChange={(e) => setDocumentStrategy(e.target.value)}>
              {(Object.entries(CUSTOMER_DOCUMENT_STRATEGY_LABELS) as [CustomerDocumentStrategy, string][]).map(
                ([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ),
              )}
            </select>
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
          <FormField
            label="Standaard facturerende entiteit"
            htmlFor="c-legal-entity"
            hint="Wordt voorgesteld op facturen voor deze klant. Leeg = de tenant-standaard."
          >
            <select id="c-legal-entity" value={defaultLegalEntityId} onChange={(e) => setDefaultLegalEntityId(e.target.value)}>
              <option value="">— Tenant-standaard —</option>
              {legalEntities.map((entity) => (
                <option key={entity.id} value={entity.id}>
                  {entity.displayName}
                  {entity.isDefault ? ' (standaard)' : ''}
                  {!entity.isActive ? ' — inactief' : ''}
                </option>
              ))}
            </select>
          </FormField>
          <FormField
            label="Toegestane facturerende entiteiten"
            htmlFor="c-allowed-entities"
            hint="Niets aangevinkt = alle actieve entiteiten toegestaan. Bij een selectie moet de standaardentiteit erin zitten; dossiers/orders/facturen buiten de lijst worden geweigerd."
          >
            <div id="c-allowed-entities" className="customer-form-requirements">
              {legalEntities.filter((entity) => entity.isActive).map((entity) => (
                <label key={entity.id} className="customer-form-checkbox">
                  <input
                    type="checkbox"
                    checked={allowedLegalEntityIds.includes(entity.id)}
                    onChange={(e) =>
                      setAllowedLegalEntityIds((ids) =>
                        e.target.checked ? [...ids, entity.id] : ids.filter((id) => id !== entity.id),
                      )
                    }
                  />
                  {entity.displayName}
                </label>
              ))}
            </div>
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
      ),
    },
    {
      id: 'communicatie',
      label: 'Communicatie',
      optional: true,
      panel: true,
      render: () =>
        isEdit ? (
          editPanels?.communicatie ?? null
        ) : (
          <p className="placeholder-text">Communicatieregels zijn beschikbaar na het aanmaken van de klant.</p>
        ),
    },
    {
      id: 'tarieven',
      label: 'Tarieven & toeslagen',
      optional: true,
      panel: true,
      render: () =>
        isEdit ? (
          editPanels?.tarieven ?? null
        ) : (
          <p className="placeholder-text">Tarieven en toeslagen zijn beschikbaar na het aanmaken van de klant.</p>
        ),
    },
    {
      id: 'historiek',
      label: 'Historiek',
      optional: true,
      panel: true,
      render: () =>
        isEdit ? (
          editPanels?.historiek ?? null
        ) : (
          <p className="placeholder-text">De historiek is beschikbaar na het aanmaken van de klant.</p>
        ),
    },
  ]

  const { activeId, setActive } = useSectionNavigation(sections.map((s) => s.id), sections[0].id)
  const activeSection = sections.find((s) => s.id === activeId) ?? sections[0]

  // Which save button was clicked last; a plain Enter-submit counts as the normal save.
  const submitIntentRef = useRef<CustomerSubmitIntent>('save')

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const intent = submitIntentRef.current
    submitIntentRef.current = 'save'
    let valid = true
    const errorKeys: Record<string, string> = {}
    if (!name.trim()) {
      setNameError('Naam is verplicht.')
      errorKeys.name = 'Naam is verplicht.'
      valid = false
    } else {
      setNameError(undefined)
    }

    const vatMessage = canManageFiscal ? (validateVatNumber(vatNumber) ?? undefined) : undefined
    setVatError(vatMessage)
    if (vatMessage) {
      errorKeys.vatNumber = vatMessage
      valid = false
    }

    if (canManageFiscal && peppolProblem) {
      errorKeys.peppolId = peppolProblem
      valid = false
    }

    const rate = resolveVatRate()
    if (!rate.ok) {
      setVatRateError('Geef een tarief tussen 0 en 100 op.')
      errorKeys.defaultVatRatePercent = 'Geef een tarief tussen 0 en 100 op.'
      valid = false
    } else {
      setVatRateError(undefined)
    }

    // Contact rows are optional; filled rows need names, and per type at most one primary.
    const rowErrors = mode === 'create' ? validateContactRows(contactRows) : {}
    setContactRowErrors(rowErrors)
    for (const [key, message] of Object.entries(rowErrors)) {
      errorKeys[key] = message
      valid = false
    }

    if (!valid) {
      const target = firstSectionWithError(
        sections.map((s) => ({
          id: s.id,
          fieldKeys:
            s.id === 'klantgegevens'
              ? [
                  ...CUSTOMER_SECTION_FIELD_KEYS.klantgegevens,
                  ...Object.keys(errorKeys).filter(isKlantgegevensFieldKey),
                ]
              : CUSTOMER_SECTION_FIELD_KEYS[s.id],
        })),
        errorKeys,
      )
      if (target) setActive(target)
      return
    }

    // Without the fiscal permission the form echoes the stored fiscal/bank values verbatim,
    // so the backend's "fiscal values changed?" check never trips on a non-fiscal edit.
    const fiscalValues = canManageFiscal
      ? {
          vatNumber: nullable(vatNumber),
          vatTreatment,
          defaultVatRatePercent: rate.value,
          vatCountryCode: vatCountryCode || null,
          peppolId: nullable(peppolId),
          peppolScheme: nullable(peppolScheme),
          peppolEnabled,
          peppolDeliveryPreference,
          buyerReference: nullable(buyerReference),
          companyNumber: nullable(companyNumber),
          currencyCode: nullable(currencyCode)?.toUpperCase() ?? null,
          iban: nullable(iban),
          bic: nullable(bic),
          bankAccountNumber: nullable(bankAccountNumber),
        }
      : {
          vatNumber: initial?.vatNumber ?? null,
          vatTreatment: initial?.vatTreatment ?? ('DomesticVat' as VatTreatment),
          defaultVatRatePercent: initial?.defaultVatRatePercent ?? null,
          vatCountryCode: initial?.vatCountryCode ?? null,
          peppolId: initial?.peppolId ?? null,
          peppolScheme: initial?.peppolScheme ?? null,
          peppolEnabled: initial?.peppolEnabled ?? false,
          peppolDeliveryPreference: initial?.peppolDeliveryPreference ?? ('Peppol' as PeppolDeliveryPreference),
          buyerReference: initial?.buyerReference ?? null,
          companyNumber: initial?.companyNumber ?? null,
          currencyCode: initial?.currencyCode ?? null,
          iban: initial?.iban ?? null,
          bic: initial?.bic ?? null,
          bankAccountNumber: initial?.bankAccountNumber ?? null,
        }

    const base: CustomerInput = {
      name: name.trim(),
      // Create-only: manual number + the multi-contact payload (initialContact is history).
      ...(mode === 'create'
        ? { customerNumber: nullable(customerNumber), contacts: contactRowsToPayload(contactRows) }
        : {}),
      nickname: nullable(nickname),
      legalName: nullable(legalName),
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
      invoiceGrouping,
      documentStrategy,
      paymentTermDays: Number.isFinite(Number(paymentTermDays)) ? Number(paymentTermDays) : 0,
      defaultLanguageCode: defaultLanguageCode || null,
      defaultLegalEntityId: defaultLegalEntityId || null,
      allowedLegalEntityIds,
      notes: nullable(notes),
      vatNotes: nullable(vatNotes),
      bankName: nullable(bankName),
      ...fiscalValues,
      invoiceLanguageCode: invoiceLanguageCode || null,
      purchaseOrderRequired,
      signedDeliveryNoteRequired,
      customerReferenceRequired,
    }
    // Clear the guard before the parent saves + navigates away. In edit mode the Actief
    // checkbox is authoritative; a create is always active.
    setDirty(false)
    onSubmit({ ...base, isActive: isEdit ? isActive : true }, intent)
  }

  // Same buttons at the top and (sticky) bottom of the form; both submit the one form.
  const actionBar = (position: 'top' | 'bottom') => (
    <FormActions dirty={dirty} position={position}>
      <Button variant="secondary" onClick={onCancel} disabled={isSubmitting}>
        Annuleren
      </Button>
      {mode === 'create' && (
        <Button
          type="submit"
          variant="secondary"
          disabled={isSubmitting}
          onClick={() => {
            submitIntentRef.current = 'saveAndNew'
          }}
        >
          Opslaan en nieuwe klant
        </Button>
      )}
      <Button
        type="submit"
        disabled={isSubmitting}
        onClick={() => {
          submitIntentRef.current = 'save'
        }}
      >
        {isSubmitting ? 'Opslaan...' : 'Opslaan'}
      </Button>
    </FormActions>
  )

  return (
    <form onSubmit={handleSubmit} className="customer-form" onChange={touch}>
      <UnsavedChangesGuard when={dirty && !isSubmitting} />
      <ValidationSummary message={submitError} fieldErrors={serverFieldErrors} fieldLabels={FIELD_LABELS} />

      {/* SectionedForm renders `actions` only at the bottom; mirror its panel check here so the
          top bar also disappears on self-saving panel sections. */}
      {!activeSection.panel && actionBar('top')}

      <SectionedForm
        sections={sections}
        activeId={activeId}
        onActiveChange={setActive}
        actions={actionBar('bottom')}
      />
    </form>
  )
}
