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
import { useLocale } from '../../../i18n/localeContext'
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

/** i18n keys (employees.fields.*) for backend field paths, for the validation summary. */
const FIELD_LABEL_KEYS: Record<string, string> = {
  firstName: 'employees.fields.firstName',
  lastName: 'employees.fields.lastName',
  email: 'employees.fields.email',
  phoneNumber: 'employees.fields.phoneNumber',
  mobilePhone: 'employees.fields.mobilePhone',
  dateOfBirth: 'employees.fields.dateOfBirth',
  placeOfBirth: 'employees.fields.placeOfBirth',
  street: 'employees.fields.street',
  houseNumber: 'employees.fields.houseNumber',
  postalCode: 'employees.fields.postalCode',
  city: 'employees.fields.city',
  countryCode: 'employees.fields.countryCode',
  employmentStartDate: 'employees.fields.employmentStartDate',
  employmentEndDate: 'employees.fields.employmentEndDate',
  employmentStatus: 'employees.fields.employmentStatus',
  departmentId: 'employees.fields.departmentId',
  contractTypeId: 'employees.fields.contractTypeId',
  jobFunctionIds: 'employees.fields.jobFunctionIds',
  civilStatus: 'employees.fields.civilStatus',
  dimonaNumber: 'employees.fields.dimonaNumber',
  dependentChildren: 'employees.fields.dependentChildren',
  identityCardNumber: 'employees.fields.identityCardNumber',
  emergencyContacts: 'employees.fields.emergencyContacts',
  nationalRegisterNumber: 'employees.fields.nationalRegisterNumber',
  iban: 'employees.fields.iban',
  bic: 'employees.fields.bic',
  qualifications: 'employees.fields.qualifications',
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
  const { t } = useLocale()
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

  // Translated field-name map for the validation summary (backend field path → label).
  const fieldLabels = useMemo(
    () => Object.fromEntries(Object.entries(FIELD_LABEL_KEYS).map(([field, key]) => [field, t(key)])),
    [t],
  )

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

  // Zachte regel (spec §2.4): on create the rule always applies; on edit it only blocks the
  // save when THIS submit changes the contract type. A legacy dossier whose contract type
  // was backfilled to requiresEndDate=true, but which predates that end date, stays editable
  // for unrelated fields — the completeness card surfaces the gap instead of a hard block.
  const initialContractTypeIdRef = useRef(initial?.contractTypeId ?? null)
  const contractTypeChanged = mode === 'create' || contractTypeId !== initialContractTypeIdRef.current
  const missingEndDateBlocking = contractTypeRequiresEndDate && contractTypeChanged
  const missingEndDateHint =
    contractTypeRequiresEndDate && !contractTypeChanged && !employmentEndDate
      ? t('employees.validation.endDateMissingHint')
      : undefined

  function applyEndDatePreset(months: number) {
    setEmploymentEndDate(addContractEndDate(employmentStartDate || todayIsoDate(), months))
    touch()
  }

  function validate(): Record<string, string> {
    // Alleen voor- en achternaam blokkeren (spec §13); al de rest wordt enkel op formaat
    // gecontroleerd wanneer er effectief een waarde is ingevuld.
    const errors: Record<string, string> = {}
    if (!firstName.trim()) errors.firstName = t('employees.validation.firstNameRequired')
    if (!lastName.trim()) errors.lastName = t('employees.validation.lastNameRequired')
    if (email.trim() && !email.includes('@')) errors.email = t('employees.validation.invalidEmail')
    if (missingEndDateBlocking && !employmentEndDate) {
      errors.employmentEndDate = t('employees.validation.endDateRequired')
    } else if (employmentStartDate && employmentEndDate && employmentEndDate < employmentStartDate) {
      errors.employmentEndDate = t('employees.validation.endDateBeforeStart')
    }
    if (canSeeConfidential) {
      const nrnError = validateNrn(nationalRegisterNumber)
      if (nrnError) errors.nationalRegisterNumber = t(nrnError)
      const ibanError = validateIban(iban)
      if (ibanError) errors.iban = t(ibanError)
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
    label: t(EMPLOYEE_CORE_SECTIONS[0].labelKey),
    hasError: sectionHasError('algemeen'),
    render: () => (
      <>
        <FormSection title={t('employees.form.personalTitle')} columns={3}>
          <FormField label={t('employees.form.firstName')} htmlFor="e-firstname" error={fieldErrors.firstName} required>
            <input id="e-firstname" value={firstName} onChange={(e) => setFirstName(e.target.value)} maxLength={100} aria-invalid={fieldErrors.firstName ? 'true' : undefined} />
          </FormField>
          <FormField label={t('employees.form.lastName')} htmlFor="e-lastname" error={fieldErrors.lastName} required>
            <input id="e-lastname" value={lastName} onChange={(e) => setLastName(e.target.value)} maxLength={100} aria-invalid={fieldErrors.lastName ? 'true' : undefined} />
          </FormField>
          {duplicateNameHint && (
            <div className="form-span-all employee-form-duplicate-hint">{duplicateNameHint}</div>
          )}
          <FormField label={t('employees.form.dateOfBirth')} htmlFor="e-dob" error={fieldErrors.dateOfBirth}>
            <input id="e-dob" type="date" value={dateOfBirth} onChange={(e) => setDateOfBirth(e.target.value)} aria-invalid={fieldErrors.dateOfBirth ? 'true' : undefined} />
          </FormField>
          <FormField label={t('employees.form.placeOfBirth')} htmlFor="e-pob">
            <input id="e-pob" value={placeOfBirth} onChange={(e) => setPlaceOfBirth(e.target.value)} maxLength={100} />
          </FormField>
          <FormField label={t('employees.form.nationality')} htmlFor="e-nationality">
            <SearchableSelect
              id="e-nationality"
              value={nationalityCode}
              onChange={(v) => {
                setNationalityCode(v)
                touch()
              }}
              options={nationalities.options.map((o) => ({ value: o.code, label: o.name, keywords: o.code }))}
              isLoading={nationalities.isLoading}
              placeholder={t('ui.select.placeholder')}
            />
          </FormField>
          <FormField label={t('employees.form.preferredLanguage')} htmlFor="e-language">
            <select id="e-language" value={preferredLanguageCode} onChange={(e) => setPreferredLanguageCode(e.target.value)}>
              <option value="">{t('employees.form.noneOption')}</option>
              {languages.options.map((o) => (
                <option key={o.id} value={o.code}>
                  {o.name}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t('employees.form.civilStatus')} htmlFor="e-civil-status">
            <select
              id="e-civil-status"
              value={civilStatus}
              onChange={(e) => {
                setCivilStatus(e.target.value as CivilStatus | '')
                touch()
              }}
            >
              <option value="">{t('employees.form.unknownOption')}</option>
              {Object.entries(CIVIL_STATUS_LABELS).map(([value, labelKey]) => (
                <option key={value} value={value}>
                  {t(labelKey)}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t('employees.form.dependentChildren')} htmlFor="e-dependent-children">
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

        <FormSection title={t('employees.form.contactAddressTitle')} columns={3}>
          <FormField label={t('employees.form.email')} htmlFor="e-email" error={fieldErrors.email}>
            <input id="e-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} maxLength={250} aria-invalid={fieldErrors.email ? 'true' : undefined} />
          </FormField>
          <FormField label={t('employees.form.phone')} htmlFor="e-phone" error={fieldErrors.phoneNumber}>
            <input id="e-phone" value={phoneNumber} onChange={(e) => setPhoneNumber(e.target.value)} maxLength={30} aria-invalid={fieldErrors.phoneNumber ? 'true' : undefined} />
          </FormField>
          <FormField label={t('employees.form.mobile')} htmlFor="e-mobile">
            <input id="e-mobile" value={mobilePhone} onChange={(e) => setMobilePhone(e.target.value)} maxLength={30} />
          </FormField>
          <FormField label={t('employees.form.street')} htmlFor="e-street" error={fieldErrors.street}>
            <input id="e-street" value={street} onChange={(e) => setStreet(e.target.value)} maxLength={150} aria-invalid={fieldErrors.street ? 'true' : undefined} />
          </FormField>
          <FormField label={t('employees.form.houseNumber')} htmlFor="e-houseno" error={fieldErrors.houseNumber}>
            <input id="e-houseno" value={houseNumber} onChange={(e) => setHouseNumber(e.target.value)} maxLength={20} aria-invalid={fieldErrors.houseNumber ? 'true' : undefined} />
          </FormField>
          <FormField label={t('employees.form.postalCode')} htmlFor="e-postal" error={fieldErrors.postalCode}>
            <input id="e-postal" value={postalCode} onChange={(e) => setPostalCode(e.target.value)} maxLength={20} aria-invalid={fieldErrors.postalCode ? 'true' : undefined} />
          </FormField>
          <FormField label={t('employees.form.city')} htmlFor="e-city" error={fieldErrors.city}>
            <input id="e-city" value={city} onChange={(e) => setCity(e.target.value)} maxLength={100} aria-invalid={fieldErrors.city ? 'true' : undefined} />
          </FormField>
          <FormField label={t('employees.form.country')} htmlFor="e-country" error={getFieldError(serverFieldErrors, 'countryCode')}>
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
    label: t(EMPLOYEE_CORE_SECTIONS[1].labelKey),
    hasError: sectionHasError('dienstverband'),
    render: () => (
      <FormSection
        title={t('employees.form.employmentTitle')}
        columns={3}
        description={t('employees.form.employmentDescription')}
      >
        <FormField label={t('employees.form.startDate')} htmlFor="e-start" error={fieldErrors.employmentStartDate}>
          <input id="e-start" type="date" value={employmentStartDate} onChange={(e) => setEmploymentStartDate(e.target.value)} aria-invalid={fieldErrors.employmentStartDate ? 'true' : undefined} />
        </FormField>
        <FormField
          label={t('employees.form.endDate')}
          htmlFor="e-end"
          hint={missingEndDateHint ?? (contractTypeRequiresEndDate ? undefined : t('employees.form.endDateOpenEndedHint'))}
          required={missingEndDateBlocking}
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
        <FormField label={t('employees.form.employmentStatus')} htmlFor="e-status">
          <select id="e-status" value={employmentStatus} onChange={(e) => setEmploymentStatus(e.target.value as EmploymentStatus)}>
            {Object.entries(EMPLOYMENT_STATUS_LABELS).map(([value, labelKey]) => (
              <option key={value} value={value}>
                {t(labelKey)}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('employees.form.department')} htmlFor="e-department">
          <LookupSelect
            id="e-department"
            basePath="/api/departments"
            managePermission="departments.manage"
            singular={t('employees.form.departmentSingular')}
            value={departmentId}
            onChange={(v) => {
              setDepartmentId(v)
              touch()
            }}
            placeholder={t('employees.form.noDepartmentOption')}
          />
        </FormField>
        <FormField label={t('employees.form.contractType')} htmlFor="e-contract">
          <LookupSelect
            id="e-contract"
            basePath="/api/contract-types"
            managePermission="reference_data.manage"
            singular={t('employees.form.contractTypeSingular')}
            value={contractTypeId}
            onChange={(v) => {
              setContractTypeId(v)
              touch()
            }}
            placeholder={t('employees.form.noneOption')}
          />
        </FormField>
        <FormField label={t('employees.form.dimonaNumber')} htmlFor="e-dimona">
          <input id="e-dimona" value={dimonaNumber} onChange={(e) => setDimonaNumber(e.target.value)} maxLength={50} />
        </FormField>
        <div className="employee-form-functions form-span-all">
          <FormField label={t('employees.form.functions')} htmlFor="e-function-add" hint={t('employees.form.functionsHint')}>
            <div className="employee-form-function-chips">
              {selectedFunctions.map((f) => (
                <Badge key={f.id} tone="info">
                  {f.name}
                  <button
                    type="button"
                    className="employee-form-chip-remove"
                    aria-label={t('employees.form.removeFunction', { name: f.name })}
                    onClick={() => updateFunctions(jobFunctionIds.filter((id) => id !== f.id))}
                  >
                    ×
                  </button>
                </Badge>
              ))}
              {selectedFunctions.length === 0 && <span className="employee-form-no-functions">{t('employees.form.noFunctionsChosen')}</span>}
            </div>
            <SearchableSelect
              id="e-function-add"
              value={null}
              onChange={(v) => {
                if (v) updateFunctions([...jobFunctionIds, v])
              }}
              options={functionOptions}
              isLoading={jobFunctions.isLoading}
              placeholder={t('employees.form.addFunctionPlaceholder')}
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
    label: t(EMPLOYEE_CORE_SECTIONS[2].labelKey),
    hasError: sectionHasError('hr'),
    render: () =>
      canSeeConfidential ? (
        <FormSection
          title={t('employees.form.identityBankTitle')}
          columns={3}
          description={t('employees.form.identityBankDescription')}
        >
          <FormField
            label={t('employees.form.identityCardNumber')}
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
            label={t('employees.form.nationalRegisterNumber')}
            htmlFor="e-nrn"
            hint={t('employees.form.nrnHint')}
            error={fieldErrors.nationalRegisterNumber ?? getFieldError(serverFieldErrors, 'nationalRegisterNumber')}
          >
            <input
              id="e-nrn"
              value={nationalRegisterNumber}
              onChange={(e) => setNationalRegisterNumber(e.target.value)}
              onBlur={() => {
                const message = validateNrn(nationalRegisterNumber)
                setFieldErrors((current) => ({ ...current, nationalRegisterNumber: message ? t(message) : undefined }) as Record<string, string>)
                if (!message) setNationalRegisterNumber((value) => formatNrn(value))
              }}
              aria-invalid={fieldErrors.nationalRegisterNumber ? 'true' : undefined}
              maxLength={15}
            />
          </FormField>
          <FormField label={t('employees.form.iban')} htmlFor="e-iban" error={fieldErrors.iban ?? getFieldError(serverFieldErrors, 'iban')}>
            <input
              id="e-iban"
              value={iban}
              onChange={(e) => setIban(e.target.value)}
              onBlur={() => {
                const message = validateIban(iban)
                setFieldErrors((current) => ({ ...current, iban: message ? t(message) : undefined }) as Record<string, string>)
                if (!message) setIban((value) => formatIban(value))
              }}
              aria-invalid={fieldErrors.iban ? 'true' : undefined}
              maxLength={42}
              placeholder="BE68 5390 0754 7034"
            />
          </FormField>
          <FormField label={t('employees.form.bic')} htmlFor="e-bic" error={getFieldError(serverFieldErrors, 'bic')}>
            <input id="e-bic" value={bic} onChange={(e) => setBic(e.target.value)} maxLength={11} placeholder="KREDBEBB" />
          </FormField>
        </FormSection>
      ) : (
        <p className="placeholder-text">{t('employees.form.noConfidentialPermission')}</p>
      ),
  }

  const noodcontactenSection: SectionDef = {
    ...EMPLOYEE_CORE_SECTIONS[3],
    label: t(EMPLOYEE_CORE_SECTIONS[3].labelKey),
    hasError: sectionHasError('noodcontacten'),
    render: () => (
      <FormSection
        title={t('employees.form.emergencyTitle')}
        columns={1}
        description={t('employees.form.emergencyDescription')}
      >
        <div className="form-span-all">
          {emergencyRows.map((row, index) => (
            <div key={row.key} className="employee-emergency-row">
              <FormField label={t('employees.form.emergencyName', { index: index + 1 })} htmlFor={`e-ec-name-${row.key}`}>
                <input
                  id={`e-ec-name-${row.key}`}
                  value={row.name}
                  onChange={(e) => patchEmergencyRow(row.key, { name: e.target.value })}
                  maxLength={150}
                />
              </FormField>
              <FormField label={t('employees.form.emergencyRelationship')} htmlFor={`e-ec-rel-${row.key}`}>
                <input
                  id={`e-ec-rel-${row.key}`}
                  value={row.relationship}
                  onChange={(e) => patchEmergencyRow(row.key, { relationship: e.target.value })}
                  maxLength={100}
                />
              </FormField>
              <FormField label={t('employees.form.emergencyPhone')} htmlFor={`e-ec-phone-${row.key}`}>
                <input
                  id={`e-ec-phone-${row.key}`}
                  value={row.phone}
                  onChange={(e) => patchEmergencyRow(row.key, { phone: e.target.value })}
                  maxLength={30}
                />
              </FormField>
              <FormField label={t('employees.form.emergencyMobile')} htmlFor={`e-ec-mobile-${row.key}`}>
                <input
                  id={`e-ec-mobile-${row.key}`}
                  value={row.mobilePhone}
                  onChange={(e) => patchEmergencyRow(row.key, { mobilePhone: e.target.value })}
                  maxLength={30}
                />
              </FormField>
              <FormField label={t('employees.form.emergencyPriority')} htmlFor={`e-ec-prio-${row.key}`}>
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
                  aria-label={t('employees.form.emergencyRemove', { index: index + 1 })}
                >
                  {t('employees.form.remove')}
                </Button>
              </div>
              <FormField label={t('employees.form.emergencyNotes')} htmlFor={`e-ec-notes-${row.key}`} className="employee-emergency-notes">
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
            {t('employees.form.addEmergencyContact')}
          </Button>
        </div>
      </FormSection>
    ),
  }

  // Self-saving panel (`panel: true` hides the shared Save bar): every note action calls its
  // own endpoint directly, replacing the legacy single-textarea-over-Employee.Notes section.
  const notitiesSection: SectionDef = {
    ...EMPLOYEE_NOTITIES_SECTION,
    label: t(EMPLOYEE_NOTITIES_SECTION.labelKey),
    panel: true,
    render: () =>
      mode === 'edit' && initial ? (
        <EmployeeNotesPanel employeeId={initial.id} />
      ) : (
        <p className="placeholder-text">{t('employees.form.notesAfterSave')}</p>
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
        {t('employees.form.cancel')}
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
          {t('employees.form.saveAndNew')}
        </Button>
      )}
      <Button
        type="submit"
        disabled={isSubmitting}
        onClick={() => {
          submitIntentRef.current = 'save'
        }}
      >
        {isSubmitting ? t('employees.form.saving') : t('employees.form.save')}
      </Button>
    </FormActions>
  )

  return (
    <form onSubmit={handleSubmit} className="employee-form" onChange={touch} noValidate>
      <UnsavedChangesGuard when={dirty && !isSubmitting} />
      <ValidationSummary message={submitError} fieldErrors={serverFieldErrors} fieldLabels={fieldLabels} />

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
