import { useEffect, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormActions } from '../../../components/ui/FormActions'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { StatusBadges } from '../../../components/ui/StatusBadges'
import { TabPanel, Tabs } from '../../../components/ui/Tabs'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { ApiError } from '../../../api/apiClient'
import { AuditHistoryPanel } from '../../auditing/components/AuditHistoryPanel'
import { AssignmentSlot } from '../../fleet-assignment/AssignmentSlot'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import { FleetDocumentsPanel } from '../../fleet-documents/components/FleetDocumentsPanel'
import { MaintenancePanel } from '../../maintenance/components/MaintenancePanel'
import { DamagePanel } from '../../damage/components/DamagePanel'
import { FuelPanel } from '../../fuel/components/FuelPanel'
import { InspectionsPanel } from '../../inspections/components/InspectionsPanel'
import { searchDrivers } from '../../drivers/api/driversApi'
import { deleteVehicle, getVehicle, setVehicleDriver, updateVehicle } from '../api/vehiclesApi'
import {
  EMISSION_CLASS_LABELS,
  FUEL_TYPE_LABELS,
  OPERATIONAL_STATUS_LABELS,
  OPERATIONAL_STATUS_TONES,
  OWNERSHIP_TYPE_LABELS,
  type EmissionClass,
  type FuelType,
  type VehicleDetail,
  type VehicleInput,
  type VehicleOperationalStatus,
  type VehicleOwnershipType,
} from '../types'
import './vehicle-form.css'

const TAB_IDS = ['overzicht', 'techniek', 'toewijzing', 'documenten', 'onderhoud', 'keuringen', 'schade', 'brandstof', 'historiek'] as const
type TabId = (typeof TAB_IDS)[number]

function numberOrNull(value: string): number | null {
  if (value.trim() === '') return null
  const parsed = Number(value.replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : null
}

export function VehicleDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()

  const [vehicle, setVehicle] = useState<VehicleDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<VehicleInput | null>(null)
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const requestedTab = searchParams.get('tab')
  const tab: TabId = TAB_IDS.includes(requestedTab as TabId) ? (requestedTab as TabId) : 'overzicht'

  useEffect(() => {
    if (!id) return
    let mounted = true
    getVehicle(id)
      .then((result) => {
        if (!mounted) return
        setVehicle(result)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Voertuig kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

  const loading = vehicle === null && loadError === null
  const canEdit = hasPermission('vehicles.edit')

  function setTab(next: string) {
    setSearchParams(next === 'overzicht' ? {} : { tab: next }, { replace: true })
  }

  function reloadVehicle() {
    if (!id) return
    getVehicle(id)
      .then(setVehicle)
      .catch(() => showError('Voertuig kon niet opnieuw worden geladen.'))
  }

  function startEdit() {
    if (!vehicle) return
    setForm({
      licensePlate: vehicle.licensePlate,
      vin: vehicle.vin,
      categoryId: vehicle.categoryId,
      brand: vehicle.brand,
      model: vehicle.model,
      year: vehicle.year,
      firstRegistrationDate: vehicle.firstRegistrationDate,
      fuelType: vehicle.fuelType,
      emissionClass: vehicle.emissionClass,
      grossVehicleWeightKg: vehicle.grossVehicleWeightKg,
      payloadKg: vehicle.payloadKg,
      lengthMeters: vehicle.lengthMeters,
      widthMeters: vehicle.widthMeters,
      heightMeters: vehicle.heightMeters,
      volumeM3: vehicle.volumeM3,
      odometerKm: vehicle.odometerKm,
      consumptionLPer100Km: vehicle.consumptionLPer100Km,
      hasCrane: vehicle.hasCrane,
      hasRefrigeration: vehicle.hasRefrigeration,
      hasTailLift: vehicle.hasTailLift,
      adrSuitable: vehicle.adrSuitable,
      ownershipType: vehicle.ownershipType,
      operationalStatus: vehicle.operationalStatus,
      statusReason: vehicle.statusReason,
      isActive: vehicle.isActive,
      notes: vehicle.notes,
    })
    setDirty(false)
    setEditing(true)
  }

  function set<K extends keyof VehicleInput>(key: K, value: VehicleInput[K]) {
    setForm((f) => (f ? { ...f, [key]: value } : f))
    setDirty(true)
  }

  async function saveEdit() {
    if (!id || !form) return
    setSaving(true)
    setDirty(false)
    try {
      const updated = await updateVehicle(id, form)
      setVehicle(updated)
      setEditing(false)
      showSuccess('Voertuig bijgewerkt.')
    } catch (err) {
      setDirty(true)
      showError(err instanceof ApiError ? err.message : 'Wijzigingen konden niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!id) return
    try {
      await deleteVehicle(id)
      showSuccess('Voertuig verwijderd.')
      navigate('/vehicles')
    } catch {
      showError('Voertuig kon niet worden verwijderd.')
      setConfirmDelete(false)
    }
  }

  async function loadDriverOptions() {
    const drivers = await searchDrivers({ isActive: true, page: 1, pageSize: 200 })
    return drivers.items.map((d) => ({
      value: d.id,
      label: d.fullName,
      description: d.driverNumber,
      keywords: `${d.driverNumber} ${d.employeeNumber}`,
    }))
  }

  if (loading) return <p className="placeholder-text">Voertuig laden…</p>
  if (loadError || !vehicle) return <p className="placeholder-text">{loadError ?? 'Niet gevonden.'}</p>

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Voertuigen', to: '/vehicles' }, { label: vehicle.internalNumber }]} />
      <PageHeader
        title={`${vehicle.brand ?? ''} ${vehicle.model ?? ''}`.trim() || vehicle.internalNumber}
        subtitle={`${vehicle.internalNumber} · ${vehicle.licensePlate}`}
        action={
          <div className="vehicle-detail-actions">
            {canEdit && !editing && <Button variant="secondary" onClick={startEdit}>Bewerken</Button>}
            {hasPermission('vehicles.delete') && !editing && (
              <Button variant="danger" onClick={() => setConfirmDelete(true)}>Verwijderen</Button>
            )}
          </div>
        }
      />

      <div className="vehicle-detail-badges">
        <StatusBadges
          active={vehicle.isActive}
          operational={{
            label: OPERATIONAL_STATUS_LABELS[vehicle.operationalStatus],
            tone: OPERATIONAL_STATUS_TONES[vehicle.operationalStatus],
            reason: vehicle.statusReason,
          }}
        />
        {vehicle.adrSuitable && <Badge tone="warning">ADR</Badge>}
        {vehicle.hasCrane && <Badge tone="info">Kraan</Badge>}
        {vehicle.hasRefrigeration && <Badge tone="info">Koeling</Badge>}
        {vehicle.hasTailLift && <Badge tone="info">Laadklep</Badge>}
      </div>

      {editing && form ? (
        <form
          className="vehicle-form"
          onSubmit={(e) => {
            e.preventDefault()
            void saveEdit()
          }}
        >
          <UnsavedChangesGuard when={dirty && !saving} />

          <FormSection title="Status" columns={3} description="Administratief actief en operationele status zijn twee aparte begrippen.">
            <FormField
              label="Operationele status"
              htmlFor="e-status"
              hint="Beschikbaar / in gebruik / in onderhoud / buiten dienst."
            >
              <select id="e-status" value={form.operationalStatus} onChange={(e) => set('operationalStatus', e.target.value as VehicleOperationalStatus)} disabled={saving}>
                {Object.entries(OPERATIONAL_STATUS_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            {form.operationalStatus !== 'Available' && (
              <FormField label="Reden" htmlFor="e-status-reason" hint="Waarom is het voertuig niet beschikbaar?">
                <input id="e-status-reason" value={form.statusReason ?? ''} onChange={(e) => set('statusReason', e.target.value || null)} maxLength={500} disabled={saving} />
              </FormField>
            )}
            <FormField label="Administratief actief" htmlFor="e-active" hint="Inactieve voertuigen verdwijnen uit keuzelijsten en planning.">
              <label className="vehicle-checkbox">
                <input id="e-active" type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} disabled={saving} />
                <span>Voertuig is actief</span>
              </label>
            </FormField>
          </FormSection>

          <FormSection title="Basisgegevens" columns={3}>
            <FormField label="Kenteken" htmlFor="e-plate" required>
              <input id="e-plate" value={form.licensePlate} onChange={(e) => set('licensePlate', e.target.value)} disabled={saving} maxLength={20} />
            </FormField>
            <FormField label="VIN" htmlFor="e-vin">
              <input id="e-vin" value={form.vin ?? ''} onChange={(e) => set('vin', e.target.value || null)} disabled={saving} maxLength={50} />
            </FormField>
            <FormField label="Categorie" htmlFor="e-category">
              <LookupSelect
                id="e-category"
                basePath="/api/vehicle-categories"
                managePermission="vehicle_categories.manage"
                singular="voertuigcategorie"
                value={form.categoryId}
                onChange={(v) => set('categoryId', v)}
                placeholder="— Geen —"
                disabled={saving}
              />
            </FormField>
            <FormField label="Merk" htmlFor="e-brand">
              <input id="e-brand" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value || null)} disabled={saving} maxLength={100} />
            </FormField>
            <FormField label="Model" htmlFor="e-model">
              <input id="e-model" value={form.model ?? ''} onChange={(e) => set('model', e.target.value || null)} disabled={saving} maxLength={100} />
            </FormField>
            <FormField label="Bouwjaar" htmlFor="e-year">
              <input id="e-year" type="number" value={form.year ?? ''} onChange={(e) => set('year', e.target.value ? Number(e.target.value) : null)} disabled={saving} />
            </FormField>
            <FormField label="Eerste inschrijving" htmlFor="e-firstreg">
              <input id="e-firstreg" type="date" value={form.firstRegistrationDate ?? ''} onChange={(e) => set('firstRegistrationDate', e.target.value || null)} disabled={saving} />
            </FormField>
            <FormField label="Eigendomsvorm" htmlFor="e-ownership">
              <select id="e-ownership" value={form.ownershipType} onChange={(e) => set('ownershipType', e.target.value as VehicleOwnershipType)} disabled={saving}>
                {Object.entries(OWNERSHIP_TYPE_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Kilometerstand" htmlFor="e-odometer">
              <input id="e-odometer" type="number" min={0} value={form.odometerKm} onChange={(e) => set('odometerKm', Number(e.target.value) || 0)} disabled={saving} />
            </FormField>
            <FormField label="Normverbruik (l/100km)" htmlFor="e-consumption" hint="Voor kostenramingen; leeg = standaard uit de tarievenset.">
              <input
                id="e-consumption"
                type="number"
                min={0}
                step="0.1"
                value={form.consumptionLPer100Km ?? ''}
                onChange={(e) => set('consumptionLPer100Km', e.target.value === '' ? null : Number(e.target.value) || 0)}
                disabled={saving}
              />
            </FormField>
          </FormSection>

          <FormSection title="Techniek" columns={3}>
            <FormField label="Brandstof" htmlFor="e-fuel">
              <select id="e-fuel" value={form.fuelType} onChange={(e) => set('fuelType', e.target.value as FuelType)} disabled={saving}>
                {Object.entries(FUEL_TYPE_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Emissieklasse" htmlFor="e-emission">
              <select id="e-emission" value={form.emissionClass ?? ''} onChange={(e) => set('emissionClass', (e.target.value || null) as EmissionClass | null)} disabled={saving}>
                <option value="">— Onbekend —</option>
                {Object.entries(EMISSION_CLASS_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="MTM (kg)" htmlFor="e-gvw">
              <input id="e-gvw" inputMode="decimal" value={form.grossVehicleWeightKg ?? ''} onChange={(e) => set('grossVehicleWeightKg', numberOrNull(e.target.value))} disabled={saving} />
            </FormField>
            <FormField label="Laadvermogen (kg)" htmlFor="e-payload">
              <input id="e-payload" inputMode="decimal" value={form.payloadKg ?? ''} onChange={(e) => set('payloadKg', numberOrNull(e.target.value))} disabled={saving} />
            </FormField>
            <FormField label="Lengte (m)" htmlFor="e-length">
              <input id="e-length" inputMode="decimal" value={form.lengthMeters ?? ''} onChange={(e) => set('lengthMeters', numberOrNull(e.target.value))} disabled={saving} />
            </FormField>
            <FormField label="Breedte (m)" htmlFor="e-width">
              <input id="e-width" inputMode="decimal" value={form.widthMeters ?? ''} onChange={(e) => set('widthMeters', numberOrNull(e.target.value))} disabled={saving} />
            </FormField>
            <FormField label="Hoogte (m)" htmlFor="e-height">
              <input id="e-height" inputMode="decimal" value={form.heightMeters ?? ''} onChange={(e) => set('heightMeters', numberOrNull(e.target.value))} disabled={saving} />
            </FormField>
            <FormField label="Volume (m³)" htmlFor="e-volume">
              <input id="e-volume" inputMode="decimal" value={form.volumeM3 ?? ''} onChange={(e) => set('volumeM3', numberOrNull(e.target.value))} disabled={saving} />
            </FormField>
            <div className="vehicle-form-equipment form-span-all">
              <label className="vehicle-checkbox">
                <input type="checkbox" checked={form.hasCrane} onChange={(e) => set('hasCrane', e.target.checked)} disabled={saving} />
                <span>Kraan</span>
              </label>
              <label className="vehicle-checkbox">
                <input type="checkbox" checked={form.hasRefrigeration} onChange={(e) => set('hasRefrigeration', e.target.checked)} disabled={saving} />
                <span>Koeling</span>
              </label>
              <label className="vehicle-checkbox">
                <input type="checkbox" checked={form.hasTailLift} onChange={(e) => set('hasTailLift', e.target.checked)} disabled={saving} />
                <span>Laadklep</span>
              </label>
              <label className="vehicle-checkbox">
                <input type="checkbox" checked={form.adrSuitable} onChange={(e) => set('adrSuitable', e.target.checked)} disabled={saving} />
                <span>ADR-geschikt</span>
              </label>
            </div>
          </FormSection>

          <FormSection title="Notities" columns={1}>
            <FormField label="Interne notities" htmlFor="e-notes" className="form-span-all">
              <textarea id="e-notes" rows={3} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={saving} maxLength={2000} />
            </FormField>
          </FormSection>

          <FormActions dirty={dirty}>
            <Button type="button" variant="secondary" onClick={() => setEditing(false)} disabled={saving}>
              Annuleren
            </Button>
            <Button type="submit" disabled={saving}>
              {saving ? 'Opslaan…' : 'Opslaan'}
            </Button>
          </FormActions>
        </form>
      ) : (
        <>
          <Tabs
            tabs={[
              { id: 'overzicht', label: 'Overzicht' },
              { id: 'techniek', label: 'Techniek' },
              { id: 'toewijzing', label: 'Toewijzing' },
              { id: 'documenten', label: 'Documenten' },
              { id: 'onderhoud', label: 'Onderhoud' },
              { id: 'keuringen', label: 'Keuringen' },
              { id: 'schade', label: 'Schade' },
              { id: 'brandstof', label: 'Brandstof' },
              { id: 'historiek', label: 'Historiek' },
            ]}
            activeId={tab}
            onChange={setTab}
          />

          {tab === 'overzicht' && (
            <TabPanel tabId="overzicht">
              <div className="vehicle-detail-grid">
                <FormField label="Kenteken"><span>{vehicle.licensePlate}</span></FormField>
                <FormField label="VIN"><span>{vehicle.vin ?? '—'}</span></FormField>
                <FormField label="Categorie"><span>{vehicle.categoryName ?? '—'}</span></FormField>
                <FormField label="Bouwjaar"><span>{vehicle.year ?? '—'}</span></FormField>
                <FormField label="Eerste inschrijving"><span>{vehicle.firstRegistrationDate ?? '—'}</span></FormField>
                <FormField label="Eigendomsvorm"><span>{OWNERSHIP_TYPE_LABELS[vehicle.ownershipType]}</span></FormField>
                <FormField label="Kilometerstand"><span>{vehicle.odometerKm.toLocaleString('nl-BE')} km</span></FormField>
                <FormField label="Vaste chauffeur"><span>{vehicle.fixedDriverName ?? '—'}</span></FormField>
                <FormField label="Actuele chauffeur"><span>{vehicle.currentDriverName ?? '—'}</span></FormField>
                <FormField label="Notities" className="vehicle-detail-full"><span>{vehicle.notes ?? '—'}</span></FormField>
              </div>
            </TabPanel>
          )}

          {tab === 'techniek' && (
            <TabPanel tabId="techniek">
              <div className="vehicle-detail-grid">
                <FormField label="Brandstof"><span>{FUEL_TYPE_LABELS[vehicle.fuelType]}</span></FormField>
                <FormField label="Emissieklasse"><span>{vehicle.emissionClass ? EMISSION_CLASS_LABELS[vehicle.emissionClass] : '—'}</span></FormField>
                <FormField label="MTM"><span>{vehicle.grossVehicleWeightKg !== null ? `${vehicle.grossVehicleWeightKg} kg` : '—'}</span></FormField>
                <FormField label="Laadvermogen"><span>{vehicle.payloadKg !== null ? `${vehicle.payloadKg} kg` : '—'}</span></FormField>
                <FormField label="Afmetingen (L×B×H)">
                  <span>
                    {vehicle.lengthMeters ?? '—'} × {vehicle.widthMeters ?? '—'} × {vehicle.heightMeters ?? '—'} m
                  </span>
                </FormField>
                <FormField label="Volume"><span>{vehicle.volumeM3 !== null ? `${vehicle.volumeM3} m³` : '—'}</span></FormField>
                <FormField label="Uitrusting" className="vehicle-detail-full">
                  <span>
                    {[
                      vehicle.hasCrane ? 'Kraan' : null,
                      vehicle.hasRefrigeration ? 'Koeling' : null,
                      vehicle.hasTailLift ? 'Laadklep' : null,
                      vehicle.adrSuitable ? 'ADR' : null,
                    ]
                      .filter(Boolean)
                      .join(' · ') || 'Geen bijzondere uitrusting'}
                  </span>
                </FormField>
              </div>
            </TabPanel>
          )}

          {tab === 'toewijzing' && (
            <TabPanel tabId="toewijzing">
              <p className="assignment-slots-note">
                <strong>Vaste chauffeur</strong> is een langetermijnvoorkeur; <strong>actuele chauffeur</strong> is de
                tijdelijke operationele toewijzing. De rittoewijzing voor een specifieke dag gebeurt in de planning.
                Wijzigingen hier worden ook op de chauffeurspagina zichtbaar — het is dezelfde koppeling.
              </p>
              <div className="assignment-slots">
                <AssignmentSlot
                  title="Vaste chauffeur"
                  description="Rijdt standaard met dit voertuig."
                  assigned={vehicle.fixedDriverId && vehicle.fixedDriverName ? { label: vehicle.fixedDriverName, linkTo: `/drivers/${vehicle.fixedDriverId}` } : null}
                  canEdit={canEdit}
                  pickerLabel="Chauffeur"
                  loadOptions={loadDriverOptions}
                  assign={async (driverId, replaceExisting) => {
                    await setVehicleDriver(vehicle.id, 'fixed-driver', driverId, replaceExisting)
                  }}
                  onChanged={reloadVehicle}
                />
                <AssignmentSlot
                  title="Actuele chauffeur"
                  description="Tijdelijke operationele toewijzing."
                  assigned={vehicle.currentDriverId && vehicle.currentDriverName ? { label: vehicle.currentDriverName, linkTo: `/drivers/${vehicle.currentDriverId}` } : null}
                  canEdit={canEdit}
                  pickerLabel="Chauffeur"
                  loadOptions={loadDriverOptions}
                  assign={async (driverId, replaceExisting) => {
                    await setVehicleDriver(vehicle.id, 'current-driver', driverId, replaceExisting)
                  }}
                  onChanged={reloadVehicle}
                />
              </div>
            </TabPanel>
          )}

          {tab === 'documenten' && id && (
            <TabPanel tabId="documenten">
              <FleetDocumentsPanel ownerType="vehicle" ownerId={id} />
            </TabPanel>
          )}
          {tab === 'onderhoud' && id && (
            <TabPanel tabId="onderhoud">
              <MaintenancePanel ownerType="vehicle" ownerId={id} />
            </TabPanel>
          )}
          {tab === 'keuringen' && id && (
            <TabPanel tabId="keuringen">
              <InspectionsPanel ownerType="vehicle" ownerId={id} />
            </TabPanel>
          )}
          {tab === 'schade' && id && (
            <TabPanel tabId="schade">
              <DamagePanel ownerType="vehicle" ownerId={id} />
            </TabPanel>
          )}
          {tab === 'brandstof' && id && (
            <TabPanel tabId="brandstof">
              <FuelPanel vehicleId={id} />
            </TabPanel>
          )}
          {tab === 'historiek' && (
            <TabPanel tabId="historiek">
              <AuditHistoryPanel entityType="Vehicle" entityId={vehicle.id} />
            </TabPanel>
          )}
        </>
      )}

      {confirmDelete && (
        <ConfirmDialog
          title="Voertuig verwijderen"
          message={`Weet je zeker dat je voertuig ${vehicle.internalNumber} wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}
