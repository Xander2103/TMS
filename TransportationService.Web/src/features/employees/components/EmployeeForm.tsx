import { useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormActions } from '../../../components/ui/FormActions'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { SectionedForm, type SectionDef } from '../../../components/ui/SectionedForm'
import { useSectionNavigation, firstSectionWithError } from '../../../components/ui/useSectionNavigation'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { EmployeeNotesPanel } from './EmployeeNotesPanel'
import { formatIban, formatNrn, validateIban, validateNrn } from '../utils/personFormats'
import { addContractEndDate, todayIsoDate, CONTRACT_END_DATE_PRESETS } from '../utils/contractPresets'
import {
  addEmergencyContactRow,
  emergencyContactRowsFromDetail,
  emergencyContactRowsToPayload,
  removeEmergencyContactRow,
  updateEmergencyContactRow,
} from '../utils/emergencyContacts'
import {
  CIVIL_STATUS_LABELS,
  EMPLOYMENT_STATUS_LABELS,
  type CivilStatus,
  type EmployeeDetail,
  type EmployeeInput,
  type EmploymentStatus,
} from '../types/employee'
import {
  EMPLOYEE_CORE_SECTIONS,
  EMPLOYEE_NOTITIES_SECTION,
  EMPLOYEE_SECTION_FIELD_KEYS,
} from './employeeSections'
import './EmployeeForm.css'

/**
 * Which save button triggered the submit: 'save' is the normal flow (navigate/reload by the
 * parent), 'saveAndNew' (create mode only) asks the parent to reset for a fresh entry.
 */
export type EmployeeSubmitIntent = 'save' | 'saveAndNew'

interface EmployeeFormProps {
  mode: 'create' | 'edit'
  initial?: EmployeeDetail
  isSubmitting: boolean
  submitError: string | null
  /** Per-field backend validation messages, shown next to the fields + in the summary. */
  serverFieldErrors?: FieldErrors
  onSubmit: (values: EmployeeInput, intent: EmployeeSubmitIntent) => void
  onCancel: () => void
  /**
   * Extra sections injected between the core sections and Notities. Create passes the
   * inline driver-profile + qualifications + "available after creation" placeholders; edit
   * passes the self-saving panel sections (Chauffeursgegevens, Kwalificaties, Documenten,
   * Bedrijfsmiddelen). Both modes therefore present the same section configuration.
   */
  extraSections?: SectionDef[]
  /** Notifies the parent when the set of chosen job functions changes (driver suggestion). */
  onFunctionsChanged?: (functionCodes: string[]) => void
  /** Section to activate on mount (e.g. a deep link to "chauffeursgegevens"); falls back to the first section when absent or unknown. */
  initialSectionId?: string
  /** Notifies the parent when the first/last name changes (create page's duplicate-name check). */
  onNameChanged?: (firstName: string, lastName: string) => void
  /** Non-blocking hint rendered right under the name fields (create page's duplicate-name warning). */
  duplicateNameHint?: ReactNode
}

/** User-facing labels for backend field paths, for the validation summary. */
const FIELD_LABELS: Record<string, string> = {
  firstName: 'Voornaam',
  lastName: 'Achternaam',
  email: 'E-mailadres',
  phoneNumber: 'Telefoon',
  mobilePhone: 'GSM',
  dateOfBirth: 'Geboortedatum',
  placeOfBirth: 'Geboorteplaats',
  street: 'Straat',
  houseNumber: 'Huisnummer',
  postalCode: 'Postcode',
  city: 'Gemeente',
  countryCode: 'Land',
  employmentStartDate: 'Startdatum',
  employmentEndDate: 'Einddatum',
  employmentStatus: 'Status dienstverband',
  departmentId: 'Afdeling',
  contractTypeId: 'Contracttype',
  jobFunctionIds: 'Functies',
  civilStatus: 'Burgerlijke staat',
  dimonaNumber: 'Dimonanummer',
  dependentChildren: 'Kinderen ten laste',
  identityCardNumber: 'Identiteitskaartnummer',
  emergencyContacts: 'Noodcontacten',
  nationalRegisterNumber: 'Rijksregisternummer',
  iban: 'IBAN',
  bic: 'BIC',
  qualifications: 'Kwalificaties',
}

function nullable(value: string): string | null {
  const trimmed = value.trim()
  return trimmed ? trimmed : null
}

export function EmployeeForm({
  mode,
  initial,
  isSubmitting,
  submitError,
  serverFieldErrors,
  onSubmit,
  onCancel,
  extraSections,
  onFunctionsChanged,
  initialSectionId,
  onNameChanged,
  duplicateNameHint,
}: EmployeeFormProps) {
  const { hasPermission } = useAuth()
  const canSeeConfidential = hasPermission('employees.view_confidential')

  const jobFunctions = useLookupOptions('/api/job-functions')
  const nationalities = useLookupOptions('/api/nationalities')
  const languages = useLookupOptions('/api/languages')
  const contractTypes = useLookupOptions('/api/contract-types')

  const [firstName, setFirstName] = useState(initial?.firstName ?? '')
  const [lastName, setLastName] = useState(initial?.lastName ?? '')
  const [dateOfBirth, setDateOfBirth] = useState(initial?.dateOfBirth ?? '')
  const [placeOfBirth, setPlaceOfBirth] = useState(initial?.placeOfBirth ?? '')
  const [nationalityCode, setNationalityCode] = useState<string | null>(initial?.nationalityCode ?? null)
  const [preferredLanguageCode, setPreferredLanguageCode] = useState(initial?.preferredLanguageCode ?? '')

  const [email, setEmail] = useState(initial?.email ?? '')
  const [phoneNumber, setPhoneNumber] = useState(initial?.phoneNumber ?? '')
  const [mobilePhone, setMobilePhone] = useState(initial?.mobilePhone ?? '')

  const [street, setStreet] = useState(initial?.street ?? '')
  const [houseNumber, setHouseNumber] = useState(initial?.houseNumber ?? '')
  const [postalCode, setPostalCode] = useState(initial?.postalCode ?? '')
  const [city, setCity] = useState(initial?.city ?? '')
  const [countryCode, setCountryCode] = useState<string | null>(initial?.countryCode ?? 'BE')

  const [employmentStartDate, setEmploymentStartDate] = useState(initial?.employmentStartDate ?? '')
  const [employmentEndDate, setEmploymentEndDate] = useState(initial?.employmentEndDate ?? '')
  const [employmentStatus, setEmploymentStatus] = useState<EmploymentStatus>(initial?.employmentStatus ?? 'Active')
  const [departmentId, setDepartmentId] = useState<string | null>(initial?.departmentId ?? null)
  const [contractTypeId, setContractTypeId] = useState<string | null>(initial?.contractTypeId ?? null)
  const [jobFunctionIds, setJobFunctionIds] = useState<string[]>(initial?.jobFunctionIds ?? [])

  const [emergencyRows, setEmergencyRows] = useState(() => emergencyContactRowsFromDetail(initial?.emergencyContacts))

  const [civilStatus, setCivilStatus] = useState<CivilStatus | ''>(initial?.civilStatus ?? '')
  const [dimonaNumber, setDimonaNumber] = useState(initial?.dimonaNumber ?? '')
  const [dependentChildren, setDependentChildren] = useState(
    initial?.dependentChildren != null ? String(initial.dependentChildren) : '',
  )
  const [identityCardNumber, setIdentityCardNumber] = useState(initial?.identityCardNumber ?? '')

  const [nationalRegisterNumber, setNationalRegisterNumber] = useState(initial?.nationalRegisterNumber ?? '')
  const [iban, setIban] = useState(initial?.iban ?? '')
  const [bic, setBic] = useState(initial?.bic ?? '')

  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [dirty, setDirty] = useState(false)

  // Create page's duplicate-name check lives outside this component (it's create-only and
  // needs its own debounce/API state); this just relays every name change up to it.
  useEffect(() => {
    onNameChanged?.(firstName, lastName)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [firstName, lastName])

  function touch() {
    if (!dirty) setDirty(true)
  }

  function patchEmergencyRow(key: string, patch: Parameters<typeof updateEmergencyContactRow>[2]) {
    setEmergencyRows((rows) => updateEmergencyContactRow(rows, key, patch))
    touch()
  }

  function updateFunctions(next: string[]) {
    setJobFunctionIds(next)
    touch()
    if (onFunctionsChanged) {
      const codes = next
        .map((id) => jobFunctions.options.find((o) => o.id === id)?.code)
        .filter((code): code is string => Boolean(code))
      onFunctionsChanged(codes)
    }
  }

  const functionOptions = useMemo(
    () =>
      jobFunctions.options
        .filter((o) => !jobFunctionIds.includes(o.id))
        .map((o) => ({ value: o.id, label: o.name, keywords: o.code })),
    [jobFunctions.options, jobFunctionIds],
  )
  const selectedFunctions = jobFunctionIds
    .map((id) => jobFunctions.options.find((o) => o.id === id))
    .filter((o): o is NonNullable<typeof o> => Boolean(o))

  // Task 5: contract types can mandate an end date; the backend enforces the same rule and
  // returns the identical message on employmentEndDate, so client + server never disagree.
  const selectedContractType = contractTypes.options.find((o) => o.id === contractTypeId)
  const contractTypeRequiresEndDate = Boolean(selectedContractType?.requiresEndDate)

  function applyEndDatePreset(months: number) {
    setEmploymentEndDate(addContractEndDate(employmentStartDate || todayIsoDate(), months))
    touch()
  }

  function validate(): Record<string, string> {
    // Alleen voor- en achternaam blokkeren (spec §13); al de rest wordt enkel op formaat
    // gecontroleerd wanneer er effectief een waarde is ingevuld.
    const errors: Record<string, string> = {}
    if (!firstName.trim()) errors.firstName = 'Voornaam is verplicht.'
    if (!lastName.trim()) errors.lastName = 'Achternaam is verplicht.'
    if (email.trim() && !email.includes('@')) errors.email = 'Geef een geldig e-mailadres op.'
    if (contractTypeRequiresEndDate && !employmentEndDate) {
      errors.employmentEndDate = 'Einddatum is verplicht voor dit contracttype.'
    } else if (employmentStartDate && employmentEndDate && employmentEndDate < employmentStartDate) {
      errors.employmentEndDate = 'De einddatum moet na de startdatum liggen.'
    }
    if (canSeeConfidential) {
      const nrnError = validateNrn(nationalRegisterNumber)
      if (nrnError) errors.nationalRegisterNumber = nrnError
      const ibanError = validateIban(iban)
      if (ibanError) errors.iban = ibanError
    }
    setFieldErrors(errors)
    return errors
  }

  // Section-nav config: badge a section when one of its fields is failing (client or server),
  // and route to the first failing section after a rejected submit.
  const combinedErrorKeys = new Set<string>([
    ...Object.keys(fieldErrors).filter((key) => fieldErrors[key]),
    ...Object.keys(serverFieldErrors ?? {}),
  ])
  const sectionHasError = (id: string) =>
    (EMPLOYEE_SECTION_FIELD_KEYS[id] ?? []).some((key) => combinedErrorKeys.has(key))

  const algemeenSection: SectionDef = {
    ...EMPLOYEE_CORE_SECTIONS[0],
    hasError: sectionHasError('algemeen'),
    render: () => (
      <>
        <FormSection title="Persoonlijk" columns={3}>
          <FormField label="Voornaam" htmlFor="e-firstname" error={fieldErrors.firstName} required>
            <input id="e-firstname" value={firstName} onChange={(e) => setFirstName(e.target.value)} maxLength={100} aria-invalid={fieldErrors.firstName ? 'true' : undefined} />
          </FormField>
          <FormField label="Achternaam" htmlFor="e-lastname" error={fieldErrors.lastName} required>
            <input id="e-lastname" value={lastName} onChange={(e) => setLastName(e.target.value)} maxLength={100} aria-invalid={fieldErrors.lastName ? 'true' : undefined} />
          </FormField>
          {duplicateNameHint && (
            <div className="form-span-all employee-form-duplicate-hint">{duplicateNameHint}</div>
          )}
          <FormField label="Geboortedatum" htmlFor="e-dob" error={fieldErrors.dateOfBirth}>
            <input id="e-dob" type="date" value={dateOfBirth} onChange={(e) => setDateOfBirth(e.target.value)} aria-invalid={fieldErrors.dateOfBirth ? 'true' : undefined} />
          </FormField>
          <FormField label="Geboorteplaats" htmlFor="e-pob">
            <input id="e-pob" value={placeOfBirth} onChange={(e) => setPlaceOfBirth(e.target.value)} maxLength={100} />
          </FormField>
          <FormField label="Nationaliteit" htmlFor="e-nationality">
            <SearchableSelect
              id="e-nationality"
              value={nationalityCode}
              onChange={(v) => {
                setNationalityCode(v)
                touch()
              }}
              options={nationalities.options.map((o) => ({ value: o.code, label: o.name, keywords: o.code }))}
              isLoading={nationalities.isLoading}
              placeholder="— Selecteer —"
            />
          </FormField>
          <FormField label="Voorkeurstaal" htmlFor="e-language">
            <select id="e-language" value={preferredLanguageCode} onChange={(e) => setPreferredLanguageCode(e.target.value)}>
              <option value="">— Geen —</option>
              {languages.options.map((o) => (
                <option key={o.id} value={o.code}>
                  {o.name}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Burgerlijke staat" htmlFor="e-civil-status">
            <select
              id="e-civil-status"
              value={civilStatus}
              onChange={(e) => {
                setCivilStatus(e.target.value as CivilStatus | '')
                touch()
              }}
            >
              <option value="">— Onbekend —</option>
              {Object.entries(CIVIL_STATUS_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Aantal kinderen ten laste" htmlFor="e-dependent-children">
            <input
              id="e-dependent-children"
              type="number"
              min={0}
              max={30}
              value={dependentChildren}
              onChange={(e) => setDependentChildren(e.target.value)}
            />
          </FormField>
        </FormSection>

        <FormSection title="Contact & adres" columns={3}>
          <FormField label="E-mail" htmlFor="e-email" error={fieldErrors.email}>
            <input id="e-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} maxLength={250} aria-invalid={fieldErrors.email ? 'true' : undefined} />
          </FormField>
          <FormField label="Telefoon" htmlFor="e-phone" error={fieldErrors.phoneNumber}>
            <input id="e-phone" value={phoneNumber} onChange={(e) => setPhoneNumber(e.target.value)} maxLength={30} aria-invalid={fieldErrors.phoneNumber ? 'true' : undefined} />
          </FormField>
          <FormField label="GSM" htmlFor="e-mobile">
            <input id="e-mobile" value={mobilePhone} onChange={(e) => setMobilePhone(e.target.value)} maxLength={30} />
          </FormField>
          <FormField label="Straat" htmlFor="e-street" error={fieldErrors.street}>
            <input id="e-street" value={street} onChange={(e) => setStreet(e.target.value)} maxLength={150} aria-invalid={fieldErrors.street ? 'true' : undefined} />
          </FormField>
          <FormField label="Nummer" htmlFor="e-houseno" error={fieldErrors.houseNumber}>
            <input id="e-houseno" value={houseNumber} onChange={(e) => setHouseNumber(e.target.value)} maxLength={20} aria-invalid={fieldErrors.houseNumber ? 'true' : undefined} />
          </FormField>
          <FormField label="Postcode" htmlFor="e-postal" error={fieldErrors.postalCode}>
            <input id="e-postal" value={postalCode} onChange={(e) => setPostalCode(e.target.value)} maxLength={20} aria-invalid={fieldErrors.postalCode ? 'true' : undefined} />
          </FormField>
          <FormField label="Plaats" htmlFor="e-city" error={fieldErrors.city}>
            <input id="e-city" value={city} onChange={(e) => setCity(e.target.value)} maxLength={100} aria-invalid={fieldErrors.city ? 'true' : undefined} />
          </FormField>
          <FormField label="Land" htmlFor="e-country" error={getFieldError(serverFieldErrors, 'countryCode')}>
            <CountryCombobox
              id="e-country"
              value={countryCode}
              onChange={(code) => {
                setCountryCode(code)
                touch()
              }}
            />
          </FormField>
        </FormSection>
      </>
    ),
  }

  const dienstverbandSection: SectionDef = {
    ...EMPLOYEE_CORE_SECTIONS[1],
    hasError: sectionHasError('dienstverband'),
    render: () => (
      <FormSection
        title="Dienstverband"
        columns={3}
        description="Functies zijn HR-informatie en geven géén toegangsrechten in de applicatie; rechten beheer je via Gebruikers en Rollen."
      >
        <FormField label="Startdatum" htmlFor="e-start" error={fieldErrors.employmentStartDate}>
          <input id="e-start" type="date" value={employmentStartDate} onChange={(e) => setEmploymentStartDate(e.target.value)} aria-invalid={fieldErrors.employmentStartDate ? 'true' : undefined} />
        </FormField>
        <FormField
          label="Einddatum tewerkstelling"
          htmlFor="e-end"
          hint={contractTypeRequiresEndDate ? undefined : 'Leeg = onbepaalde duur.'}
          required={contractTypeRequiresEndDate}
          error={fieldErrors.employmentEndDate ?? getFieldError(serverFieldErrors, 'employmentEndDate')}
        >
          <div className="employee-form-enddate-row">
            <input
              id="e-end"
              type="date"
              value={employmentEndDate}
              onChange={(e) => setEmploymentEndDate(e.target.value)}
              aria-invalid={fieldErrors.employmentEndDate ? 'true' : undefined}
            />
            <div className="employee-form-enddate-presets">
              {CONTRACT_END_DATE_PRESETS.map((preset) => (
                <Button key={preset.months} type="button" variant="ghost" onClick={() => applyEndDatePreset(preset.months)}>
                  {preset.label}
                </Button>
              ))}
            </div>
          </div>
        </FormField>
        <FormField label="Dienstverbandstatus" htmlFor="e-status">
          <select id="e-status" value={employmentStatus} onChange={(e) => setEmploymentStatus(e.target.value as EmploymentStatus)}>
            {Object.entries(EMPLOYMENT_STATUS_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Afdeling" htmlFor="e-department">
          <LookupSelect
            id="e-department"
            basePath="/api/departments"
            managePermission="departments.manage"
            singular="afdeling"
            value={departmentId}
            onChange={(v) => {
              setDepartmentId(v)
              touch()
            }}
            placeholder="— Geen afdeling —"
          />
        </FormField>
        <FormField label="Contracttype" htmlFor="e-contract">
          <LookupSelect
            id="e-contract"
            basePath="/api/contract-types"
            managePermission="reference_data.manage"
            singular="contracttype"
            value={contractTypeId}
            onChange={(v) => {
              setContractTypeId(v)
              touch()
            }}
            placeholder="— Geen —"
          />
        </FormField>
        <FormField label="DIMONA-nummer" htmlFor="e-dimona">
          <input id="e-dimona" value={dimonaNumber} onChange={(e) => setDimonaNumber(e.target.value)} maxLength={50} />
        </FormField>
        <div className="employee-form-functions form-span-all">
          <FormField label="Functies" htmlFor="e-function-add" hint="Meerdere functies mogelijk; typ om te zoeken.">
            <div className="employee-form-function-chips">
              {selectedFunctions.map((f) => (
                <Badge key={f.id} tone="info">
                  {f.name}
                  <button
                    type="button"
                    className="employee-form-chip-remove"
                    aria-label={`Functie ${f.name} verwijderen`}
                    onClick={() => updateFunctions(jobFunctionIds.filter((id) => id !== f.id))}
                  >
                    ×
                  </button>
                </Badge>
              ))}
              {selectedFunctions.length === 0 && <span className="employee-form-no-functions">Nog geen functies gekozen.</span>}
            </div>
            <SearchableSelect
              id="e-function-add"
              value={null}
              onChange={(v) => {
                if (v) updateFunctions([...jobFunctionIds, v])
              }}
              options={functionOptions}
              isLoading={jobFunctions.isLoading}
              placeholder="Functie toevoegen…"
              clearable={false}
            />
          </FormField>
        </div>
      </FormSection>
    ),
  }

  // Task 10 (dossier-UX restructure): "hr" now holds only identity & bank fields — burgerlijke
  // staat / kinderen ten laste moved to Algemeen, DIMONA to Dienstverband. Every remaining field
  // is confidential, so the section is entirely permission-gated with a friendly placeholder.
  const hrSection: SectionDef = {
    ...EMPLOYEE_CORE_SECTIONS[2],
    hasError: sectionHasError('hr'),
    render: () =>
      canSeeConfidential ? (
        <FormSection
          title="Identiteit & bank"
          columns={3}
          description="Vertrouwelijke gegevens — alleen zichtbaar voor gebruikers met de juiste rechten."
        >
          <FormField
            label="Identiteitskaartnummer"
            htmlFor="e-identity-card"
          >
            <input
              id="e-identity-card"
              value={identityCardNumber}
              onChange={(e) => setIdentityCardNumber(e.target.value)}
              maxLength={50}
            />
          </FormField>
          <FormField
            label="Rijksregisternummer"
            htmlFor="e-nrn"
            hint="Met of zonder leestekens, bv. 90.05.01-123.26."
            error={fieldErrors.nationalRegisterNumber ?? getFieldError(serverFieldErrors, 'nationalRegisterNumber')}
          >
            <input
              id="e-nrn"
              value={nationalRegisterNumber}
              onChange={(e) => setNationalRegisterNumber(e.target.value)}
              onBlur={() => {
                const message = validateNrn(nationalRegisterNumber)
                setFieldErrors((current) => ({ ...current, nationalRegisterNumber: message ?? undefined }) as Record<string, string>)
                if (!message) setNationalRegisterNumber((value) => formatNrn(value))
              }}
              aria-invalid={fieldErrors.nationalRegisterNumber ? 'true' : undefined}
              maxLength={15}
            />
          </FormField>
          <FormField label="IBAN" htmlFor="e-iban" error={fieldErrors.iban ?? getFieldError(serverFieldErrors, 'iban')}>
            <input
              id="e-iban"
              value={iban}
              onChange={(e) => setIban(e.target.value)}
              onBlur={() => {
                const message = validateIban(iban)
                setFieldErrors((current) => ({ ...current, iban: message ?? undefined }) as Record<string, string>)
                if (!message) setIban((value) => formatIban(value))
              }}
              aria-invalid={fieldErrors.iban ? 'true' : undefined}
              maxLength={42}
              placeholder="BE68 5390 0754 7034"
            />
          </FormField>
          <FormField label="BIC" htmlFor="e-bic" error={getFieldError(serverFieldErrors, 'bic')}>
            <input id="e-bic" value={bic} onChange={(e) => setBic(e.target.value)} maxLength={11} placeholder="KREDBEBB" />
          </FormField>
        </FormSection>
      ) : (
        <p className="placeholder-text">Je hebt geen rechten om deze gegevens te bekijken.</p>
      ),
  }

  const noodcontactenSection: SectionDef = {
    ...EMPLOYEE_CORE_SECTIONS[3],
    hasError: sectionHasError('noodcontacten'),
    render: () => (
      <FormSection
        title="Noodcontacten"
        columns={1}
        description="Eén of meer contactpersonen voor noodgevallen. De laagste prioriteit wordt als eerste gecontacteerd."
      >
        <div className="form-span-all">
          {emergencyRows.map((row, index) => (
            <div key={row.key} className="employee-emergency-row">
              <FormField label={`Naam ${index + 1}`} htmlFor={`e-ec-name-${row.key}`}>
                <input
                  id={`e-ec-name-${row.key}`}
                  value={row.name}
                  onChange={(e) => patchEmergencyRow(row.key, { name: e.target.value })}
                  maxLength={150}
                />
              </FormField>
              <FormField label="Relatie" htmlFor={`e-ec-rel-${row.key}`}>
                <input
                  id={`e-ec-rel-${row.key}`}
                  value={row.relationship}
                  onChange={(e) => patchEmergencyRow(row.key, { relationship: e.target.value })}
                  maxLength={100}
                />
              </FormField>
              <FormField label="Telefoon" htmlFor={`e-ec-phone-${row.key}`}>
                <input
                  id={`e-ec-phone-${row.key}`}
                  value={row.phone}
                  onChange={(e) => patchEmergencyRow(row.key, { phone: e.target.value })}
                  maxLength={30}
                />
              </FormField>
              <FormField label="GSM" htmlFor={`e-ec-mobile-${row.key}`}>
                <input
                  id={`e-ec-mobile-${row.key}`}
                  value={row.mobilePhone}
                  onChange={(e) => patchEmergencyRow(row.key, { mobilePhone: e.target.value })}
                  maxLength={30}
                />
              </FormField>
              <FormField label="Prioriteit" htmlFor={`e-ec-prio-${row.key}`}>
                <input
                  id={`e-ec-prio-${row.key}`}
                  type="number"
                  min={1}
                  value={row.priority}
                  onChange={(e) => patchEmergencyRow(row.key, { priority: Number(e.target.value) || index + 1 })}
                />
              </FormField>
              <div className="employee-emergency-row-actions">
                <Button
                  variant="ghost"
                  onClick={() => {
                    setEmergencyRows((rows) => removeEmergencyContactRow(rows, row.key))
                    touch()
                  }}
                  aria-label={`Noodcontact ${index + 1} verwijderen`}
                >
                  Verwijderen
                </Button>
              </div>
              <FormField label="Notities" htmlFor={`e-ec-notes-${row.key}`} className="employee-emergency-notes">
                <input
                  id={`e-ec-notes-${row.key}`}
                  value={row.notes}
                  onChange={(e) => patchEmergencyRow(row.key, { notes: e.target.value })}
                  maxLength={500}
                />
              </FormField>
            </div>
          ))}
          <Button
            variant="secondary"
            onClick={() => {
              setEmergencyRows((rows) => addEmergencyContactRow(rows))
              touch()
            }}
          >
            + Noodcontact toevoegen
          </Button>
        </div>
      </FormSection>
    ),
  }

  // Self-saving panel (`panel: true` hides the shared Save bar): every note action calls its
  // own endpoint directly, replacing the legacy single-textarea-over-Employee.Notes section.
  const notitiesSection: SectionDef = {
    ...EMPLOYEE_NOTITIES_SECTION,
    panel: true,
    render: () =>
      mode === 'edit' && initial ? (
        <EmployeeNotesPanel employeeId={initial.id} />
      ) : (
        <p className="placeholder-text">Notities kun je toevoegen nadat je de medewerker hebt opgeslagen.</p>
      ),
  }

  const sections: SectionDef[] = [
    algemeenSection,
    dienstverbandSection,
    hrSection,
    noodcontactenSection,
    ...(extraSections ?? []),
    notitiesSection,
  ]

  const defaultSectionId =
    initialSectionId && sections.some((s) => s.id === initialSectionId) ? initialSectionId : sections[0].id
  const { activeId, setActive } = useSectionNavigation(sections.map((s) => s.id), defaultSectionId)
  const activeSection = sections.find((s) => s.id === activeId) ?? sections[0]

  // Which save button was clicked last; a plain Enter-submit counts as the normal save.
  const submitIntentRef = useRef<EmployeeSubmitIntent>('save')

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const intent = submitIntentRef.current
    submitIntentRef.current = 'save'
    const errors = validate()
    if (Object.keys(errors).length > 0) {
      // Route to the first section that owns a failing field so the error is visible.
      const target = firstSectionWithError(
        sections.map((s) => ({ id: s.id, fieldKeys: EMPLOYEE_SECTION_FIELD_KEYS[s.id] })),
        errors,
      )
      if (target) setActive(target)
      return
    }

    const values: EmployeeInput = {
      firstName: firstName.trim(),
      lastName: lastName.trim(),
      dateOfBirth: dateOfBirth || null,
      placeOfBirth: nullable(placeOfBirth),
      nationalityCode: nationalityCode || null,
      preferredLanguageCode: preferredLanguageCode || null,
      email: nullable(email),
      phoneNumber: nullable(phoneNumber),
      mobilePhone: nullable(mobilePhone),
      street: nullable(street),
      houseNumber: nullable(houseNumber),
      postalCode: nullable(postalCode),
      city: nullable(city),
      countryCode: countryCode || null,
      employmentStartDate: employmentStartDate || null,
      employmentEndDate: employmentEndDate || null,
      employmentStatus,
      departmentId: departmentId || null,
      contractTypeId: contractTypeId || null,
      jobFunctionIds,
      nationalRegisterNumber: canSeeConfidential ? nullable(nationalRegisterNumber) : null,
      iban: canSeeConfidential ? nullable(iban) : null,
      bic: canSeeConfidential ? nullable(bic) : null,
      // Legacy Employee.Notes is deliberately absent: EmployeeNote records (EmployeeNotesPanel)
      // are the source of truth and the backend preserves the stored legacy value itself.
      civilStatus: civilStatus || null,
      dependentChildren: dependentChildren.trim() ? Number(dependentChildren) : null,
      dimonaNumber: nullable(dimonaNumber),
      identityCardNumber: canSeeConfidential ? nullable(identityCardNumber) : null,
      emergencyContacts: emergencyContactRowsToPayload(emergencyRows),
    }
    setDirty(false)
    onSubmit(values, intent)
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
          Opslaan en nieuwe werknemer
        </Button>
      )}
      <Button
        type="submit"
        disabled={isSubmitting}
        onClick={() => {
          submitIntentRef.current = 'save'
        }}
      >
        {isSubmitting ? 'Opslaan…' : 'Opslaan'}
      </Button>
    </FormActions>
  )

  return (
    <form onSubmit={handleSubmit} className="employee-form" onChange={touch} noValidate>
      <UnsavedChangesGuard when={dirty && !isSubmitting} />
      <ValidationSummary message={submitError} fieldErrors={serverFieldErrors} fieldLabels={FIELD_LABELS} />

      {/* SectionedForm renders `actions` only at the bottom; mirror its panel check here so the
          top bar also disappears on self-saving panel sections. */}
      {!activeSection.panel && actionBar('top')}

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
