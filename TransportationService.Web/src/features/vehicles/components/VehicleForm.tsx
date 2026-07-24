import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormActions } from '../../../components/ui/FormActions'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { SectionedForm, type SectionDef } from '../../../components/ui/SectionedForm'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { firstSectionWithError, useSectionNavigation } from '../../../components/ui/useSectionNavigation'
import { computeVolumeM3 } from '../../../utils/volume'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import type { DriverListItem } from '../../drivers/types'
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
  /** Create-only: pickers for the initial driver assignment (Toewijzing section). */
  drivers?: DriverListItem[]
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
  drivers,
  documentsSection,
  documentsSectionIsPanel,
  maintenanceSection,
}: VehicleFormProps) {
  const [form, setForm] = useState<CreateVehicleInput>(initial)
  const [dirty, setDirty] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})

  const currentYear = new Date().getFullYear()
  const autoVolume = computeVolumeM3(form.lengthMeters, form.widthMeters, form.heightMeters)

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
    if (!form.licensePlate.trim()) errors.licensePlate = 'Kenteken is verplicht.'
    if (form.year !== null && form.year > currentYear) {
      errors.year = `Bouwjaar mag niet in de toekomst liggen (maximaal ${currentYear}).`
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
      label: 'Algemeen',
      hasError: sectionHasError('algemeen'),
      render: () => (
        <FormSection title="Algemeen" columns={3}>
          <FormField label="Kenteken" htmlFor="vf-plate" required error={fieldErrors.licensePlate}>
            <input
              id="vf-plate"
              value={form.licensePlate}
              onChange={(e) => set('licensePlate', e.target.value)}
              aria-invalid={fieldErrors.licensePlate ? 'true' : undefined}
              disabled={isSubmitting}
              maxLength={20}
            />
          </FormField>
          <FormField label="Categorie" htmlFor="vf-category">
            <LookupSelect
              id="vf-category"
              basePath="/api/vehicle-categories"
              managePermission="vehicle_categories.manage"
              singular="voertuigcategorie"
              value={form.categoryId}
              onChange={(v) => set('categoryId', v)}
              placeholder="— Geen —"
              disabled={isSubmitting}
            />
          </FormField>
          <FormField label="Merk" htmlFor="vf-brand">
            <input id="vf-brand" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value || null)} disabled={isSubmitting} maxLength={100} />
          </FormField>
          <FormField label="Model" htmlFor="vf-model">
            <input id="vf-model" value={form.model ?? ''} onChange={(e) => set('model', e.target.value || null)} disabled={isSubmitting} maxLength={100} />
          </FormField>
          <FormField label="Eigendomsvorm" htmlFor="vf-ownership">
            <select id="vf-ownership" value={form.ownershipType} onChange={(e) => set('ownershipType', e.target.value as VehicleOwnershipType)} disabled={isSubmitting}>
              {Object.entries(OWNERSHIP_TYPE_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </FormField>
          {mode === 'edit' && (
            <>
              <FormField label="Operationele status" htmlFor="vf-status" hint="Beschikbaar / in gebruik / in onderhoud / buiten dienst.">
                <select
                  id="vf-status"
                  value={form.operationalStatus}
                  onChange={(e) => set('operationalStatus', e.target.value as VehicleOperationalStatus)}
                  disabled={isSubmitting}
                >
                  {Object.entries(OPERATIONAL_STATUS_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </FormField>
              {form.operationalStatus !== 'Available' && (
                <FormField label="Reden" htmlFor="vf-status-reason" hint="Waarom is het voertuig niet beschikbaar?">
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
      label: 'Registratie',
      optional: true,
      hasError: sectionHasError('registratie'),
      render: () => (
        <FormSection title="Registratie" columns={3}>
          <FormField label="Chassisnummer (VIN)" htmlFor="vf-vin">
            <input id="vf-vin" value={form.vin ?? ''} onChange={(e) => set('vin', e.target.value || null)} disabled={isSubmitting} maxLength={50} />
          </FormField>
          <FormField label="Bouwjaar" htmlFor="vf-year" error={fieldErrors.year}>
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
          <FormField label="Eerste inschrijving" htmlFor="vf-firstreg">
            <input id="vf-firstreg" type="date" value={form.firstRegistrationDate ?? ''} onChange={(e) => set('firstRegistrationDate', e.target.value || null)} disabled={isSubmitting} />
          </FormField>
        </FormSection>
      ),
    },
    {
      id: 'capaciteit',
      label: 'Capaciteit & afmetingen',
      optional: true,
      render: () => (
        <FormSection title="Capaciteit & afmetingen" columns={3}>
          <FormField label="MTM (kg)" htmlFor="vf-gvw" hint="Maximaal toegelaten massa.">
            <input id="vf-gvw" type="number" min={0} value={form.grossVehicleWeightKg ?? ''} onChange={(e) => set('grossVehicleWeightKg', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField label="Laadvermogen (kg)" htmlFor="vf-payload">
            <input id="vf-payload" type="number" min={0} value={form.payloadKg ?? ''} onChange={(e) => set('payloadKg', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField label="Aantal assen" htmlFor="vf-axles" hint="Voor Maut/tolberekening.">
            <input id="vf-axles" type="number" min={0} max={12} value={form.axleCount} onChange={(e) => set('axleCount', Number(e.target.value) || 0)} disabled={isSubmitting} />
          </FormField>
          <FormField label="Lengte (m)" htmlFor="vf-length">
            <input id="vf-length" type="number" min={0} step="0.01" value={form.lengthMeters ?? ''} onChange={(e) => set('lengthMeters', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField label="Breedte (m)" htmlFor="vf-width">
            <input id="vf-width" type="number" min={0} step="0.01" value={form.widthMeters ?? ''} onChange={(e) => set('widthMeters', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField label="Hoogte (m)" htmlFor="vf-height">
            <input id="vf-height" type="number" min={0} step="0.01" value={form.heightMeters ?? ''} onChange={(e) => set('heightMeters', e.target.value === '' ? null : Number(e.target.value))} disabled={isSubmitting} />
          </FormField>
          <FormField
            label="Volume (m³)"
            htmlFor="vf-volume"
            hint={form.volumeIsManual ? 'Handmatige waarde; vink uit om opnieuw te berekenen.' : 'Automatisch berekend uit lengte × breedte × hoogte.'}
          >
            <input
              id="vf-volume"
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
              <span>Handmatig invullen</span>
            </label>
          </FormField>
          <FormField label="Laadmeters" htmlFor="vf-ldm">
            <input id="vf-ldm" type="number" min={0} step="0.01" value={form.loadingMeters} onChange={(e) => set('loadingMeters', Number(e.target.value) || 0)} disabled={isSubmitting} />
          </FormField>
        </FormSection>
      ),
    },
    {
      id: 'techniek',
      label: 'Techniek',
      render: () => (
        <FormSection title="Techniek" columns={3}>
          <FormField label="Brandstof" htmlFor="vf-fuel">
            <select id="vf-fuel" value={form.fuelType} onChange={(e) => set('fuelType', e.target.value as FuelType)} disabled={isSubmitting}>
              {Object.entries(FUEL_TYPE_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Emissieklasse" htmlFor="vf-emission">
            <select id="vf-emission" value={form.emissionClass ?? ''} onChange={(e) => set('emissionClass', (e.target.value || null) as EmissionClass | null)} disabled={isSubmitting}>
              <option value="">— Onbekend —</option>
              {Object.entries(EMISSION_CLASS_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Vereist rijbewijs" htmlFor="vf-licence" hint="Minimaal rijbewijs voor de eligibiliteitscontrole; leeg = geen controle.">
            <select id="vf-licence" value={form.requiredLicenceCode ?? ''} onChange={(e) => set('requiredLicenceCode', e.target.value || null)} disabled={isSubmitting}>
              <option value="">— Geen controle —</option>
              {REQUIRED_LICENCE_CODES.map((code) => (
                <option key={code} value={code}>
                  {code}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Kilometerstand" htmlFor="vf-odometer">
            <input id="vf-odometer" type="number" min={0} value={form.odometerKm} onChange={(e) => set('odometerKm', Number(e.target.value) || 0)} disabled={isSubmitting} />
          </FormField>
          <FormField label="Normverbruik (l/100km)" htmlFor="vf-consumption" hint="Voor kostenramingen; leeg = standaard uit de tarievenset.">
            <input
              id="vf-consumption"
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
              <span>Kraan</span>
            </label>
            <label className="vehicle-checkbox">
              <input type="checkbox" checked={form.hasRefrigeration} onChange={(e) => set('hasRefrigeration', e.target.checked)} disabled={isSubmitting} />
              <span>Koeling</span>
            </label>
            <label className="vehicle-checkbox">
              <input type="checkbox" checked={form.hasTailLift} onChange={(e) => set('hasTailLift', e.target.checked)} disabled={isSubmitting} />
              <span>Laadklep</span>
            </label>
            <label className="vehicle-checkbox">
              <input type="checkbox" checked={form.adrSuitable} onChange={(e) => set('adrSuitable', e.target.checked)} disabled={isSubmitting} />
              <span>ADR-geschikt</span>
            </label>
          </div>
        </FormSection>
      ),
    },
    {
      id: 'documenten',
      label: 'Documenten',
      optional: true,
      panel: mode === 'edit' && documentsSectionIsPanel !== false,
      render: () => (
        <FormSection title="Documenten" columns={1}>
          {documentsSection ?? <p className="placeholder-text">Documenten zijn beschikbaar na het aanmaken.</p>}
        </FormSection>
      ),
    },
    {
      id: 'onderhoud',
      label: 'Onderhoud & keuringen',
      optional: true,
      panel: mode === 'edit',
      render: () => (
        <FormSection title="Onderhoud & keuringen" columns={1}>
          {maintenanceSection ?? (
            <p className="placeholder-text">
              Na het aanmaken worden onderhoud en keuringen automatisch ingepland volgens het geldende onderhoudsbeleid
              (voertuigregel → categorieregel → bedrijfsstandaard).
            </p>
          )}
        </FormSection>
      ),
    },
    ...(mode === 'create'
      ? [
          {
            id: 'toewijzing',
            label: 'Toewijzing',
            optional: true,
            render: () => (
              <FormSection title="Toewijzing" columns={2} description="Kan later ook via het tabblad Toewijzing beheerd worden.">
                <FormField label="Vaste chauffeur" htmlFor="vf-fixed-driver">
                  <select id="vf-fixed-driver" value={form.fixedDriverId ?? ''} onChange={(e) => set('fixedDriverId', e.target.value || null)} disabled={isSubmitting}>
                    <option value="">— Geen —</option>
                    {(drivers ?? []).map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.fullName} ({d.driverNumber})
                      </option>
                    ))}
                  </select>
                </FormField>
                <FormField label="Huidige chauffeur" htmlFor="vf-current-driver">
                  <select id="vf-current-driver" value={form.currentDriverId ?? ''} onChange={(e) => set('currentDriverId', e.target.value || null)} disabled={isSubmitting}>
                    <option value="">— Geen —</option>
                    {(drivers ?? []).map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.fullName} ({d.driverNumber})
                      </option>
                    ))}
                  </select>
                </FormField>
              </FormSection>
            ),
          } satisfies SectionDef,
        ]
      : []),
    {
      id: 'notities',
      label: 'Notities',
      optional: true,
      render: () => (
        <FormSection title="Notities" columns={1}>
          <FormField label="Interne notities" htmlFor="vf-notes" className="form-span-all">
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
              Annuleren
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? 'Bezig…' : mode === 'create' ? 'Voertuig aanmaken' : 'Opslaan'}
            </Button>
          </FormActions>
        }
      />
    </form>
  )
}
