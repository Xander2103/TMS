import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormActions } from '../../../components/ui/FormActions'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { SectionedForm, type SectionDef } from '../../../components/ui/SectionedForm'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { firstSectionWithError, useSectionNavigation } from '../../../components/ui/useSectionNavigation'
import { useLocale } from '../../../i18n/localeContext'
import { negativeFieldErrors } from '../../../utils/nonNegative'
import { computeVolumeM3 } from '../../../utils/volume'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { searchActiveDriverOptions } from '../../drivers/api/driverOptions'
import {
  EMISSION_CLASS_LABELS,
  REQUIRED_LICENCE_CODES,
  FUEL_TYPE_LABELS,
  OPERATIONAL_STATUS_LABELS,
  OWNERSHIP_TYPE_LABELS,
  type CreateVehicleInput,
  type EmissionClass,
  type FuelType,
  type VehicleOperationalStatus,
  type VehicleOwnershipType,
} from '../types'
import '../pages/vehicle-form.css'

export interface VehicleFormProps {
  mode: 'create' | 'edit'
  initial: CreateVehicleInput
  isSubmitting: boolean
  submitError?: string | null
  onSubmit: (values: CreateVehicleInput) => void | Promise<void>
  onCancel: () => void
  /** Documenten section body: create → PreparedFleetDocumentsEditor, edit → FleetDocumentsPanel. */
  documentsSection?: ReactNode
  /** True when the documents section saves itself (edit-mode panel): hides the shared actions there. */
  documentsSectionIsPanel?: boolean
  /** Onderhoud & keuringen section body (edit: effective-policy summary; create: informational). */
  maintenanceSection?: ReactNode
}

/** Field keys per section; drives error badges + first-error routing. */
const SECTION_FIELD_KEYS: Record<string, string[]> = {
  algemeen: ['licensePlate'],
  registratie: ['year'],
  capaciteit: ['grossVehicleWeightKg', 'payloadKg', 'axleCount', 'lengthMeters', 'widthMeters', 'heightMeters', 'volumeM3', 'loadingMeters'],
  techniek: ['odometerKm', 'odometerCorrectionReason', 'consumptionLPer100Km'],
}

/**
 * Sectioned create/edit form for a vehicle. All field state lives here (lifted above the
 * section bodies), so switching sections never loses data and never touches the router —
 * the UnsavedChangesGuard only fires on real page navigation.
 */
export function VehicleForm({
  mode,
  initial,
  isSubmitting,
  submitError,
  onSubmit,
  onCancel,
  documentsSection,
  documentsSectionIsPanel,
  maintenanceSection,
}: VehicleFormProps) {
  const { t } = useLocale()
  const [form, setForm] = useState<CreateVehicleInput>(initial)
  const [dirty, setDirty] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})

  const currentYear = new Date().getFullYear()
  const autoVolume = computeVolumeM3(form.lengthMeters, form.widthMeters, form.heightMeters)
  /** FL-M6: lowering the odometer is a correction and needs a reason; the server rejects it otherwise. */
  const currentOdometer = initial.odometerKm
  const odometerLowered = mode === 'edit' && form.odometerKm < currentOdometer

  const sectionIds = useMemo(() => {
    const ids = ['algemeen', 'registratie', 'capaciteit', 'techniek', 'documenten', 'onderhoud']
    if (mode === 'create') ids.push('toewijzing')
    ids.push('notities')
    return ids
  }, [mode])
  const { activeId, setActive } = useSectionNavigation(sectionIds, 'algemeen')

  function set<K extends keyof CreateVehicleInput>(key: K, value: CreateVehicleInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
    setDirty(true)
  }

  function validate(): Record<string, string> {
    const errors: Record<string, string> = {}
    if (!form.licensePlate.trim()) errors.licensePlate = t('fleet.form.plateRequired')
    if (form.year !== null && form.year > currentYear) {
      errors.year = t('fleet.form.yearFuture', { year: currentYear })
    }
    Object.assign(
      errors,
      negativeFieldErrors(
        {
          grossVehicleWeightKg: form.grossVehicleWeightKg,
          payloadKg: form.payloadKg,
          axleCount: form.axleCount,
          lengthMeters: form.lengthMeters,
          widthMeters: form.widthMeters,
          heightMeters: form.heightMeters,
          volumeM3: form.volumeIsManual ? form.volumeM3 : null,
          loadingMeters: form.loadingMeters,
          odometerKm: form.odometerKm,
          consumptionLPer100Km: form.consumptionLPer100Km,
        },
        t('fleet.form.negativeNotAllowed'),
      ),
    )
    if (odometerLowered && !form.odometerCorrectionReason?.trim()) {
      errors.odometerCorrectionReason = t('vehicles.form.odometerReasonRequired')
    }
    return errors
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const errors = validate()
    setFieldErrors(errors)
    if (Object.keys(errors).length > 0) {
      const target = firstSectionWithError(
        sectionIds.map((id) => ({ id, fieldKeys: SECTION_FIELD_KEYS[id] })),
        errors,
      )
      if (target) setActive(target)
      return
    }
    setDirty(false)
    await onSubmit({ ...form, odometerCorrectionReason: odometerLowered ? form.odometerCorrectionReason?.trim() || null : null })
  }

  const sectionHasError = (id: string) => (SECTION_FIELD_KEYS[id] ?? []).some((key) => fieldErrors[key])

  const sections: SectionDef[] = [
    {
      id: 'algemeen',
      label: t('fleet.sections.general'),
      hasError: sectionHasError('algemeen'),
      render: () => (
        <FormSection title={t('fleet.sections.general')} columns={3}>
          <FormField label={t('fleet.form.plate')} htmlFor="vf-plate" required error={fieldErrors.licensePlate}>
            <input
              id="vf-plate"
              value={form.licensePlate}
              onChange={(e) => set('licensePlate', e.target.value)}
              aria-invalid={fieldErrors.licensePlate ? 'true' : undefined}
              disabled={isSubmitting}
              maxLength={20}
            />
          </FormField>
          <FormField label={t('fleet.form.category')} htmlFor="vf-category">
            <LookupSelect
              id="vf-category"
              basePath="/api/vehicle-categories"
              managePermission="vehicle_categories.manage"
              singular="masterData.singular.vehicle-categories"
              value={form.categoryId}
              onChange={(v) => set('categoryId', v)}
              placeholder={t('fleet.form.none')}
              disabled={isSubmitting}
            />
          </FormField>
          <FormField label={t('fleet.form.brand')} htmlFor="vf-brand">
            <input id="vf-brand" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value || null)} disabled={isSubmitting} maxLength={100} />
          </FormField>
          <FormField label={t('fleet.form.model')} htmlFor="vf-model">
            <input id="vf-model" value={form.model ?? ''} onChange={(e) => set('model', e.target.value || null)} disabled={isSubmitting} maxLength={100} />
          </FormField>
          <FormField label={t('fleet.form.ownership')} htmlFor="vf-ownership">
            <select id="vf-ownership" value={form.ownershipType} onChange={(e) => set('ownershipType', e.target.value as VehicleOwnershipType)} disabled={isSubmitting}>
              {Object.entries(OWNERSHIP_TYPE_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {t(label)}
                </option>
              ))}
            </select>
          </FormField>
          {mode === 'edit' && (
            <>
              <FormField label={t('fleet.form.operationalStatus')} htmlFor="vf-status" hint={t('vehicles.form.statusHint')}>
                <select
                  id="vf-status"
                  value={form.operationalStatus}
                  onChange={(e) => set('operationalStatus', e.target.value as VehicleOperationalStatus)}
                  disabled={isSubmitting}
                >
                  {Object.entries(OPERATIONAL_STATUS_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {t(label)}
                    </option>
                  ))}
                </select>
              </FormField>
              {form.operationalStatus !== 'Available' && (
                <FormField label={t('fleet.form.statusReason')} htmlFor="vf-status-reason" hint={t('vehicles.form.statusReasonHint')}>
                  <input id="vf-status-reason" value={form.statusReason ?? ''} onChange={(e) => set('statusReason', e.target.value || null)} maxLength={500} disabled={isSubmitting} />
                </FormField>
              )}
            </>
          )}
        </FormSection>
      ),
    },
    {
      id: 'registratie',
      label: t('fleet.sections.registration'),
      optional: true,
      hasError: sectionHasError('registratie'),
      render: () => (
        <FormSection title={t('fleet.sections.registration')} columns={3}>
          <FormField label={t('fleet.form.vin')} htmlFor="vf-vin">
            <input id="vf-vin" value={form.vin ?? ''} onChange={(e) => set('vin', e.target.value || null)} disabled={isSubmitting} maxLength={50} />
          </FormField>
          <FormField label={t('fleet.form.year')} htmlFor="vf-year" error={fieldErrors.year}>
            <input
              id="vf-year"
              type="number"
              min={1900}
              max={currentYear}
              value={form.year ?? ''}
              onChange={(e) => set('year', e.target.value === '' ? null : Number(e.target.value))}
              aria-invalid={fieldErrors.year ? 'true' : undefined}
              disabled={isSubmitting}
            />
          </FormField>
          <FormField label={t('vehicles.form.firstRegistration')} htmlFor="vf-firstreg">
            <input id="vf-firstreg" type="date" value={form.firstRegistrationDate ?? ''} onChange={(e) => set('firstRegistrationDate', e.target.value || null)} disabled={isSubmitting} />
          </FormField>
        </FormSection>
      ),
    },
    {
      id: 'capaciteit',
      label: t('fleet.sections.capacity'),
      optional: true,
      render: () => (
        <FormSection title={t('fleet.sections.capacity')} columns={3}>
          <FormField label={t('vehicles.form.gvw')} htmlFor="vf-gvw" error={fieldErrors.grossVehicleWeightKg} hint={t('vehicles.form.gvwHint')}>
            <input id="vf-gvw"
              aria-invalid={fieldErrors.grossVehicleWeightKg ? 'true' : undefined} type="number" min={0} value={form.grossVehicleWeightKg ?? ''} onChange={(e) => set('grossVehicleWeightKg', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField label={t('fleet.form.payloadKg')} htmlFor="vf-payload" error={fieldErrors.payloadKg}>
            <input id="vf-payload"
              aria-invalid={fieldErrors.payloadKg ? 'true' : undefined} type="number" min={0} value={form.payloadKg ?? ''} onChange={(e) => set('payloadKg', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField label={t('fleet.form.axles')} htmlFor="vf-axles" error={fieldErrors.axleCount} hint={t('fleet.form.axlesHint')}>
            <input id="vf-axles"
              aria-invalid={fieldErrors.axleCount ? 'true' : undefined} type="number" min={0} max={12} value={form.axleCount} onChange={(e) => set('axleCount', Number(e.target.value) || 0)} disabled={isSubmitting} />
          </FormField>
          <FormField label={t('fleet.form.length')} htmlFor="vf-length" error={fieldErrors.lengthMeters}>
            <input id="vf-length"
              aria-invalid={fieldErrors.lengthMeters ? 'true' : undefined} type="number" min={0} step="0.01" value={form.lengthMeters ?? ''} onChange={(e) => set('lengthMeters', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField label={t('fleet.form.width')} htmlFor="vf-width" error={fieldErrors.widthMeters}>
            <input id="vf-width"
              aria-invalid={fieldErrors.widthMeters ? 'true' : undefined} type="number" min={0} step="0.01" value={form.widthMeters ?? ''} onChange={(e) => set('widthMeters', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField label={t('fleet.form.height')} htmlFor="vf-height" error={fieldErrors.heightMeters}>
            <input id="vf-height"
              aria-invalid={fieldErrors.heightMeters ? 'true' : undefined} type="number" min={0} step="0.01" value={form.heightMeters ?? ''} onChange={(e) => set('heightMeters', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField
            label={t('fleet.form.volume')}
            htmlFor="vf-volume" error={fieldErrors.volumeM3}
            hint={form.volumeIsManual ? t('fleet.form.volumeManualHint') : t('fleet.form.volumeAutoHint')}
          >
            <input
              id="vf-volume"
              aria-invalid={fieldErrors.volumeM3 ? 'true' : undefined}
              type="number"
              min={0}
              step="0.001"
              value={form.volumeIsManual ? (form.volumeM3 ?? '') : (autoVolume ?? '')}
              onChange={(e) => set('volumeM3', e.target.value === '' ? null : Number(e.target.value))}
              disabled={isSubmitting || !form.volumeIsManual}
            />
            <label className="vehicle-checkbox">
              <input
                type="checkbox"
                checked={form.volumeIsManual}
                onChange={(e) => {
                  set('volumeIsManual', e.target.checked)
                  if (e.target.checked) set('volumeM3', form.volumeM3 ?? autoVolume)
                }}
                disabled={isSubmitting}
              />
              <span>{t('fleet.common.equipment.manualFill')}</span>
            </label>
          </FormField>
          <FormField label={t('fleet.form.loadingMeters')} htmlFor="vf-ldm" error={fieldErrors.loadingMeters}>
            <input id="vf-ldm"
              aria-invalid={fieldErrors.loadingMeters ? 'true' : undefined} type="number" min={0} step="0.01" value={form.loadingMeters} onChange={(e) => set('loadingMeters', Number(e.target.value) || 0)} disabled={isSubmitting} />
          </FormField>
        </FormSection>
      ),
    },
    {
      id: 'techniek',
      label: t('fleet.sections.technical'),
      render: () => (
        <FormSection title={t('fleet.sections.technical')} columns={3}>
          <FormField label={t('vehicles.form.fuel')} htmlFor="vf-fuel">
            <select id="vf-fuel" value={form.fuelType} onChange={(e) => set('fuelType', e.target.value as FuelType)} disabled={isSubmitting}>
              {Object.entries(FUEL_TYPE_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {t(label)}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t('vehicles.form.emissionClass')} htmlFor="vf-emission">
            <select id="vf-emission" value={form.emissionClass ?? ''} onChange={(e) => set('emissionClass', (e.target.value || null) as EmissionClass | null)} disabled={isSubmitting}>
              <option value="">{t('vehicles.form.emissionUnknown')}</option>
              {Object.entries(EMISSION_CLASS_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {t(label)}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t('vehicles.form.requiredLicence')} htmlFor="vf-licence" hint={t('vehicles.form.requiredLicenceHint')}>
            <select id="vf-licence" value={form.requiredLicenceCode ?? ''} onChange={(e) => set('requiredLicenceCode', e.target.value || null)} disabled={isSubmitting}>
              <option value="">{t('vehicles.form.noCheck')}</option>
              {REQUIRED_LICENCE_CODES.map((code) => (
                <option key={code} value={code}>
                  {code}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t('vehicles.form.odometer')} htmlFor="vf-odometer" error={fieldErrors.odometerKm}>
            <input id="vf-odometer"
              aria-invalid={fieldErrors.odometerKm ? 'true' : undefined} type="number" min={0} value={form.odometerKm} onChange={(e) => set('odometerKm', Number(e.target.value) || 0)} disabled={isSubmitting} />
          </FormField>
          {odometerLowered && (
            <FormField
              label={t('vehicles.form.odometerReason')}
              htmlFor="vf-odometer-reason"
              required
              hint={t('vehicles.form.odometerReasonHint', { current: currentOdometer })}
              error={fieldErrors.odometerCorrectionReason}
            >
              <input
                id="vf-odometer-reason"
                value={form.odometerCorrectionReason ?? ''}
                onChange={(e) => set('odometerCorrectionReason', e.target.value || null)}
                aria-invalid={fieldErrors.odometerCorrectionReason ? 'true' : undefined}
                maxLength={500}
                disabled={isSubmitting}
              />
            </FormField>
          )}
          <FormField label={t('vehicles.form.consumption')} htmlFor="vf-consumption" error={fieldErrors.consumptionLPer100Km} hint={t('vehicles.form.consumptionHint')}>
            <input
              id="vf-consumption"
              aria-invalid={fieldErrors.consumptionLPer100Km ? 'true' : undefined}
              type="number"
              min={0}
              step="0.1"
              value={form.consumptionLPer100Km ?? ''}
              onChange={(e) => set('consumptionLPer100Km', e.target.value === '' ? null : Number(e.target.value) || 0)}
              disabled={isSubmitting}
            />
          </FormField>
          <div className="vehicle-form-checkboxes form-span-all">
            <label className="vehicle-checkbox">
              <input type="checkbox" checked={form.hasCrane} onChange={(e) => set('hasCrane', e.target.checked)} disabled={isSubmitting} />
              <span>{t('fleet.common.equipment.crane')}</span>
            </label>
            <label className="vehicle-checkbox">
              <input type="checkbox" checked={form.hasRefrigeration} onChange={(e) => set('hasRefrigeration', e.target.checked)} disabled={isSubmitting} />
              <span>{t('fleet.common.equipment.refrigeration')}</span>
            </label>
            <label className="vehicle-checkbox">
              <input type="checkbox" checked={form.hasTailLift} onChange={(e) => set('hasTailLift', e.target.checked)} disabled={isSubmitting} />
              <span>{t('fleet.common.equipment.tailLift')}</span>
            </label>
            <label className="vehicle-checkbox">
              <input type="checkbox" checked={form.adrSuitable} onChange={(e) => set('adrSuitable', e.target.checked)} disabled={isSubmitting} />
              <span>{t('fleet.common.equipment.adr')}</span>
            </label>
          </div>
        </FormSection>
      ),
    },
    {
      id: 'documenten',
      label: t('fleet.sections.documents'),
      optional: true,
      panel: mode === 'edit' && documentsSectionIsPanel !== false,
      render: () => (
        <FormSection title={t('fleet.sections.documents')} columns={1}>
          {documentsSection ?? <p className="placeholder-text">{t('fleet.form.docsAfterCreate')}</p>}
        </FormSection>
      ),
    },
    {
      id: 'onderhoud',
      label: t('fleet.sections.maintenance'),
      optional: true,
      panel: mode === 'edit',
      render: () => (
        <FormSection title={t('fleet.sections.maintenance')} columns={1}>
          {maintenanceSection ?? <p className="placeholder-text">{t('vehicles.form.maintenanceInfo')}</p>}
        </FormSection>
      ),
    },
    ...(mode === 'create'
      ? [
          {
            id: 'toewijzing',
            label: t('fleet.sections.assignment'),
            optional: true,
            render: () => (
              <FormSection title={t('fleet.sections.assignment')} columns={2} description={t('vehicles.form.assignmentDescription')}>
                {/* PERF-H3: drivers are searched server-side (name, driver number) as the user types. */}
                <FormField label={t('vehicles.form.fixedDriver')} htmlFor="vf-fixed-driver">
                  <SearchableSelect
                    id="vf-fixed-driver"
                    value={form.fixedDriverId}
                    onChange={(value) => set('fixedDriverId', value)}
                    options={[]}
                    onSearch={searchActiveDriverOptions}
                    placeholder={t('fleet.form.none')}
                    disabled={isSubmitting}
                  />
                </FormField>
                <FormField label={t('vehicles.form.currentDriver')} htmlFor="vf-current-driver">
                  <SearchableSelect
                    id="vf-current-driver"
                    value={form.currentDriverId}
                    onChange={(value) => set('currentDriverId', value)}
                    options={[]}
                    onSearch={searchActiveDriverOptions}
                    placeholder={t('fleet.form.none')}
                    disabled={isSubmitting}
                  />
                </FormField>
              </FormSection>
            ),
          } satisfies SectionDef,
        ]
      : []),
    {
      id: 'notities',
      label: t('fleet.sections.notes'),
      optional: true,
      render: () => (
        <FormSection title={t('fleet.sections.notes')} columns={1}>
          <FormField label={t('fleet.form.internalNotes')} htmlFor="vf-notes" className="form-span-all">
            <textarea id="vf-notes" rows={3} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={isSubmitting} maxLength={2000} />
          </FormField>
        </FormSection>
      ),
    },
  ]

  return (
    <form className="vehicle-form" onSubmit={handleSubmit} noValidate>
      <UnsavedChangesGuard when={dirty && !isSubmitting} />
      {submitError && (
        <div className="vehicle-form-error" role="alert">
          {submitError}
        </div>
      )}
      <SectionedForm
        sections={sections}
        activeId={activeId}
        onActiveChange={setActive}
        actions={
          <FormActions dirty={dirty}>
            <Button type="button" variant="secondary" onClick={onCancel} disabled={isSubmitting}>
              {t('ui.actions.cancel')}
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? t('fleet.common.busy') : mode === 'create' ? t('vehicles.form.create') : t('ui.actions.save')}
            </Button>
          </FormActions>
        }
      />
    </form>
  )
}
