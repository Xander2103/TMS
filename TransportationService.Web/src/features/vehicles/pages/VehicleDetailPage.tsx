import { useEffect, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { BackButton } from '../../../components/ui/BackButton'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { StatusBadges } from '../../../components/ui/StatusBadges'
import { TabPanel, Tabs } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { ApiError } from '../../../api/apiClient'
import { AuditHistoryPanel } from '../../auditing/components/AuditHistoryPanel'
import { AssignmentSlot } from '../../fleet-assignment/AssignmentSlot'
import { FleetDocumentsPanel } from '../../fleet-documents/components/FleetDocumentsPanel'
import { MaintenancePanel } from '../../maintenance/components/MaintenancePanel'
import { DamagePanel } from '../../damage/components/DamagePanel'
import { FuelPanel } from '../../fuel/components/FuelPanel'
import { FleetKpiPanel } from '../../fleet-kpi/FleetKpiPanel'
import { TachographPanel } from '../../fleet-compliance/TachographPanel'
import { LeasingPanel } from '../../fleet-compliance/LeasingPanel'
import { InspectionsPanel } from '../../inspections/components/InspectionsPanel'
import { searchDrivers } from '../../drivers/api/driversApi'
import { deleteVehicle, getVehicle, setVehicleActive, setVehicleDriver, updateVehicle } from '../api/vehiclesApi'
import { VehicleForm } from '../components/VehicleForm'
import {
  EMISSION_CLASS_LABELS,
  FUEL_TYPE_LABELS,
  OPERATIONAL_STATUS_LABELS,
  OPERATIONAL_STATUS_TONES,
  OWNERSHIP_TYPE_LABELS,
  type VehicleDetail,
  type VehicleInput,
} from '../types'
import './vehicle-form.css'

const TAB_IDS = ['overzicht', 'techniek', 'toewijzing', 'documenten', 'tachograaf', 'leasing', 'onderhoud', 'keuringen', 'schade', 'brandstof', 'kpi', 'historiek'] as const
type TabId = (typeof TAB_IDS)[number]

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
  const [saving, setSaving] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [confirmActive, setConfirmActive] = useState<null | 'activate' | 'deactivate'>(null)

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
      axleCount: vehicle.axleCount,
      loadingMeters: vehicle.loadingMeters,
      requiredLicenceCode: vehicle.requiredLicenceCode,
      hasCrane: vehicle.hasCrane,
      hasRefrigeration: vehicle.hasRefrigeration,
      hasTailLift: vehicle.hasTailLift,
      adrSuitable: vehicle.adrSuitable,
      ownershipType: vehicle.ownershipType,
      operationalStatus: vehicle.operationalStatus,
      statusReason: vehicle.statusReason,
      isActive: vehicle.isActive,
      notes: vehicle.notes,
      volumeIsManual: vehicle.volumeIsManual,
    })
    setEditing(true)
  }

  async function saveEdit(values: VehicleInput) {
    if (!id) return
    setSaving(true)
    try {
      const updated = await updateVehicle(id, values)
      setVehicle(updated)
      setEditing(false)
      showSuccess('Voertuig bijgewerkt.')
    } catch (err) {
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
      <BackButton to="/vehicles" label="Terug naar voertuigen" />
      <PageHeader
        title={`${vehicle.brand ?? ''} ${vehicle.model ?? ''}`.trim() || vehicle.internalNumber}
        subtitle={`${vehicle.internalNumber} · ${vehicle.licensePlate}`}
        action={
          <div className="vehicle-detail-actions">
            {canEdit && !editing && <Button variant="secondary" onClick={startEdit}>Bewerken</Button>}
            {canEdit && !editing && (
              <Button variant="secondary" onClick={() => setConfirmActive(vehicle.isActive ? 'deactivate' : 'activate')}>
                {vehicle.isActive ? 'Deactiveren' : 'Heractiveren'}
              </Button>
            )}
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
        <VehicleForm
          mode="edit"
          initial={{ ...form, fixedDriverId: vehicle.fixedDriverId, currentDriverId: vehicle.currentDriverId }}
          isSubmitting={saving}
          onSubmit={saveEdit}
          onCancel={() => setEditing(false)}
          documentsSection={<FleetDocumentsPanel ownerType="vehicle" ownerId={vehicle.id} />}
        />
      ) : (
        <>
          <Tabs
            tabs={[
              { id: 'overzicht', label: 'Overzicht' },
              { id: 'techniek', label: 'Techniek' },
              { id: 'toewijzing', label: 'Toewijzing' },
              { id: 'documenten', label: 'Documenten' },
              ...(hasPermission('tachograph.view') ? [{ id: 'tachograaf', label: 'Tachograaf' }] : []),
              { id: 'leasing', label: 'Leasing' },
              { id: 'onderhoud', label: 'Onderhoud' },
              { id: 'keuringen', label: 'Keuringen' },
              { id: 'schade', label: 'Schade' },
              { id: 'brandstof', label: 'Brandstof' },
              { id: 'kpi', label: 'KPI' },
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
                <FormField label="Aantal assen" hint="Voor Maut/tolberekening."><span>{vehicle.axleCount || '—'}</span></FormField>
                <FormField label="Laadmeters"><span>{vehicle.loadingMeters ? `${vehicle.loadingMeters} ldm` : '—'}</span></FormField>
                <FormField label="Vereist rijbewijs"><span>{vehicle.requiredLicenceCode ?? '—'}</span></FormField>
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
          {tab === 'tachograaf' && id && hasPermission('tachograph.view') && (
            <TabPanel tabId="tachograaf">
              <TachographPanel vehicleId={id} />
            </TabPanel>
          )}
          {tab === 'leasing' && id && (
            <TabPanel tabId="leasing">
              <LeasingPanel ownerType="vehicle" ownerId={id} />
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
          {tab === 'kpi' && id && (
            <TabPanel tabId="kpi">
              <FleetKpiPanel ownerType="vehicle" ownerId={id} />
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

      {confirmActive && (
        <ConfirmDialog
          title={confirmActive === 'deactivate' ? 'Voertuig deactiveren' : 'Voertuig heractiveren'}
          message={
            confirmActive === 'deactivate'
              ? `${vehicle.internalNumber} deactiveren? Historiek blijft behouden, maar het voertuig is niet meer inzetbaar voor nieuwe ritten.`
              : `${vehicle.internalNumber} heractiveren? Het voertuig is daarna weer inzetbaar.`
          }
          confirmLabel={confirmActive === 'deactivate' ? 'Deactiveren' : 'Heractiveren'}
          onConfirm={async () => {
            if (!id) return
            try {
              await setVehicleActive(id, confirmActive === 'activate')
              showSuccess(confirmActive === 'activate' ? 'Voertuig geheractiveerd.' : 'Voertuig gedeactiveerd.')
              setConfirmActive(null)
              reloadVehicle()
            } catch (err) {
              showError(err instanceof ApiError ? err.message : 'De actie kon niet worden uitgevoerd.')
              setConfirmActive(null)
            }
          }}
          onCancel={() => setConfirmActive(null)}
        />
      )}
    </div>
  )
}
