import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormActions } from '../../../components/ui/FormActions'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { SectionedForm, type SectionDef } from '../../../components/ui/SectionedForm'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { firstSectionWithError, useSectionNavigation } from '../../../components/ui/useSectionNavigation'
import { useLocale } from '../../../i18n/localeContext'
import { computeVolumeM3 } from '../../../utils/volume'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import {
  TRAILER_OWNERSHIP_LABELS,
  TRAILER_STATUS_LABELS,
  type TrailerInput,
  type TrailerOperationalStatus,
  type TrailerOwnershipType,
} from '../types'
import '../pages/trailer-form.css'

export interface TrailerFormProps {
  mode: 'create' | 'edit'
  initial: TrailerInput
  isSubmitting: boolean
  submitError?: string | null
  onSubmit: (values: TrailerInput) => void | Promise<void>
  onCancel: () => void
  /** Documenten section body: create → PreparedFleetDocumentsEditor, edit → FleetDocumentsPanel. */
  documentsSection?: ReactNode
  /** Onderhoud & keuringen section body (edit: effective-policy summary; create: informational). */
  maintenanceSection?: ReactNode
}

const SECTION_FIELD_KEYS: Record<string, string[]> = {
  algemeen: ['licensePlate'],
  registratie: ['year'],
}

/**
 * Sectioned create/edit form for a trailer; same architecture as VehicleForm — field state
 * lifted here, section switching is internal (never triggers the unsaved-changes guard).
 */
export function TrailerForm({
  mode,
  initial,
  isSubmitting,
  submitError,
  onSubmit,
  onCancel,
  documentsSection,
  maintenanceSection,
}: TrailerFormProps) {
  const { t } = useLocale()
  const [form, setForm] = useState<TrailerInput>(initial)
  const [dirty, setDirty] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})

  const currentYear = new Date().getFullYear()
  const autoVolume = computeVolumeM3(form.lengthMeters, form.widthMeters, form.heightMeters)

  const sectionIds = useMemo(
    () => ['algemeen', 'registratie', 'capaciteit', 'techniek', 'documenten', 'onderhoud', 'notities'],
    [],
  )
  const { activeId, setActive } = useSectionNavigation(sectionIds, 'algemeen')

  function set<K extends keyof TrailerInput>(key: K, value: TrailerInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
    setDirty(true)
  }

  function validate(): Record<string, string> {
    const errors: Record<string, string> = {}
    if (!form.licensePlate.trim()) errors.licensePlate = t('fleet.form.plateRequired')
    if (form.year !== null && form.year > currentYear) {
      errors.year = t('fleet.form.yearFuture', { year: currentYear })
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
    await onSubmit(form)
  }

  const sectionHasError = (id: string) => (SECTION_FIELD_KEYS[id] ?? []).some((key) => fieldErrors[key])

  const sections: SectionDef[] = [
    {
      id: 'algemeen',
      label: t('fleet.sections.general'),
      hasError: sectionHasError('algemeen'),
      render: () => (
        <FormSection title={t('fleet.sections.general')} columns={3}>
          <FormField label={t('fleet.form.plate')} htmlFor="tf-plate" required error={fieldErrors.licensePlate}>
            <input
              id="tf-plate"
              value={form.licensePlate}
              onChange={(e) => set('licensePlate', e.target.value)}
              aria-invalid={fieldErrors.licensePlate ? 'true' : undefined}
              disabled={isSubmitting}
              maxLength={20}
            />
          </FormField>
          <FormField label={t('fleet.form.category')} htmlFor="tf-category">
            <LookupSelect
              id="tf-category"
              basePath="/api/trailer-categories"
              managePermission="trailer_categories.manage"
              singular="masterData.singular.trailer-categories"
              value={form.categoryId}
              onChange={(v) => set('categoryId', v)}
              placeholder={t('fleet.form.none')}
              disabled={isSubmitting}
            />
          </FormField>
          <FormField label={t('fleet.form.brand')} htmlFor="tf-brand">
            <input id="tf-brand" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value || null)} disabled={isSubmitting} maxLength={100} />
          </FormField>
          <FormField label={t('fleet.form.model')} htmlFor="tf-model">
            <input id="tf-model" value={form.model ?? ''} onChange={(e) => set('model', e.target.value || null)} disabled={isSubmitting} maxLength={100} />
          </FormField>
          <FormField label={t('fleet.form.ownership')} htmlFor="tf-ownership">
            <select id="tf-ownership" value={form.ownershipType} onChange={(e) => set('ownershipType', e.target.value as TrailerOwnershipType)} disabled={isSubmitting}>
              {Object.entries(TRAILER_OWNERSHIP_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {t(label)}
                </option>
              ))}
            </select>
          </FormField>
          {mode === 'edit' && (
            <>
              <FormField label={t('fleet.form.operationalStatus')} htmlFor="tf-status">
                <select
                  id="tf-status"
                  value={form.operationalStatus}
                  onChange={(e) => set('operationalStatus', e.target.value as TrailerOperationalStatus)}
                  disabled={isSubmitting}
                >
                  {Object.entries(TRAILER_STATUS_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {t(label)}
                    </option>
                  ))}
                </select>
              </FormField>
              {form.operationalStatus !== 'Available' && (
                <FormField label={t('fleet.form.statusReason')} htmlFor="tf-status-reason">
                  <input id="tf-status-reason" value={form.statusReason ?? ''} onChange={(e) => set('statusReason', e.target.value || null)} maxLength={500} disabled={isSubmitting} />
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
          <FormField label={t('fleet.form.vin')} htmlFor="tf-vin">
            <input id="tf-vin" value={form.vin ?? ''} onChange={(e) => set('vin', e.target.value || null)} disabled={isSubmitting} maxLength={50} />
          </FormField>
          <FormField label={t('fleet.form.year')} htmlFor="tf-year" error={fieldErrors.year}>
            <input
              id="tf-year"
              type="number"
              min={1900}
              max={currentYear}
              value={form.year ?? ''}
              onChange={(e) => set('year', e.target.value === '' ? null : Number(e.target.value))}
              aria-invalid={fieldErrors.year ? 'true' : undefined}
              disabled={isSubmitting}
            />
          </FormField>
          <FormField label={t('trailers.form.firstRegistration')} htmlFor="tf-firstreg">
            <input id="tf-firstreg" type="date" value={form.firstRegistrationDate ?? ''} onChange={(e) => set('firstRegistrationDate', e.target.value || null)} disabled={isSubmitting} />
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
          <FormField label={t('fleet.form.payloadKg')} htmlFor="tf-capacity">
            <input id="tf-capacity" type="number" step="0.01" value={form.capacityKg ?? ''} onChange={(e) => set('capacityKg', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField label={t('fleet.form.axles')} htmlFor="tf-axles" hint={t('fleet.form.axlesHint')}>
            <input id="tf-axles" type="number" min={0} max={12} value={form.axleCount} onChange={(e) => set('axleCount', Number(e.target.value) || 0)} disabled={isSubmitting} />
          </FormField>
          <FormField label={t('fleet.form.loadingMeters')} htmlFor="tf-ldm">
            <input id="tf-ldm" type="number" min={0} step="0.01" value={form.loadingMeters} onChange={(e) => set('loadingMeters', Number(e.target.value) || 0)} disabled={isSubmitting} />
          </FormField>
          <FormField label={t('fleet.form.length')} htmlFor="tf-length">
            <input id="tf-length" type="number" step="0.01" value={form.lengthMeters ?? ''} onChange={(e) => set('lengthMeters', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField label={t('fleet.form.width')} htmlFor="tf-width">
            <input id="tf-width" type="number" step="0.01" value={form.widthMeters ?? ''} onChange={(e) => set('widthMeters', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField label={t('fleet.form.height')} htmlFor="tf-height">
            <input id="tf-height" type="number" step="0.01" value={form.heightMeters ?? ''} onChange={(e) => set('heightMeters', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField
            label={t('fleet.form.volume')}
            htmlFor="tf-volume"
            hint={form.volumeIsManual ? t('fleet.form.volumeManualHint') : t('fleet.form.volumeAutoHint')}
          >
            <input
              id="tf-volume"
              type="number"
              min={0}
              step="0.001"
              value={form.volumeIsManual ? (form.volumeM3 ?? '') : (autoVolume ?? '')}
              onChange={(e) => set('volumeM3', e.target.value === '' ? null : Number(e.target.value))}
              disabled={isSubmitting || !form.volumeIsManual}
            />
            <label className="trailer-checkbox">
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
        </FormSection>
      ),
    },
    {
      id: 'techniek',
      label: t('fleet.sections.technical'),
      optional: true,
      render: () => (
        <FormSection title={t('fleet.sections.technical')} columns={1}>
          <div className="trailer-form-checkboxes">
            <label className="trailer-checkbox">
              <input type="checkbox" checked={form.hasRefrigeration} onChange={(e) => set('hasRefrigeration', e.target.checked)} disabled={isSubmitting} />
              <span>{t('fleet.common.equipment.refrigeration')}</span>
            </label>
            <label className="trailer-checkbox">
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
      panel: mode === 'edit',
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
          {maintenanceSection ?? <p className="placeholder-text">{t('trailers.form.maintenanceInfo')}</p>}
        </FormSection>
      ),
    },
    {
      id: 'notities',
      label: t('fleet.sections.notes'),
      optional: true,
      render: () => (
        <FormSection title={t('fleet.sections.notes')} columns={1}>
          <FormField label={t('fleet.form.internalNotes')} htmlFor="tf-notes" className="form-span-all">
            <textarea id="tf-notes" rows={3} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={isSubmitting} maxLength={2000} />
          </FormField>
        </FormSection>
      ),
    },
  ]

  return (
    <form className="trailer-form" onSubmit={handleSubmit} noValidate>
      <UnsavedChangesGuard when={dirty && !isSubmitting} />
      {submitError && (
        <div className="trailer-form-error" role="alert">
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
              {isSubmitting ? t('fleet.common.busy') : mode === 'create' ? t('trailers.form.create') : t('ui.actions.save')}
            </Button>
          </FormActions>
        }
      />
    </form>
  )
}
