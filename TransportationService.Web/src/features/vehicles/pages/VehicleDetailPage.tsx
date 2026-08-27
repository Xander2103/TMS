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
import { useLocale } from '../../../i18n/localeContext'
import { formatInteger } from '../../../utils/numbers'
import { AuditHistoryPanel } from '../../auditing/components/AuditHistoryPanel'
import { AssignmentSlot } from '../../fleet-assignment/AssignmentSlot'
import { FleetDocumentsPanel } from '../../fleet-documents/components/FleetDocumentsPanel'
import { MaintenancePanel } from '../../maintenance/components/MaintenancePanel'
import { MaintenancePolicySummary } from '../../maintenance-policies/components/MaintenancePolicySummary'
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
  const { t } = useLocale()
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
        if (mounted) setLoadError(t('vehicles.detail.loadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [id, t])

  const loading = vehicle === null && loadError === null
  const canEdit = hasPermission('vehicles.edit')

  function setTab(next: string) {
    setSearchParams(next === 'overzicht' ? {} : { tab: next }, { replace: true })
  }

  function reloadVehicle() {
    if (!id) return
    getVehicle(id)
      .then(setVehicle)
      .catch(() => showError(t('vehicles.detail.reloadFailed')))
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
      showSuccess(t('vehicles.detail.updated'))
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('fleet.common.saveChangesFailed'))
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!id) return
    try {
      await deleteVehicle(id)
      showSuccess(t('vehicles.detail.deleted'))
      navigate('/vehicles')
    } catch {
      showError(t('vehicles.detail.deleteFailed'))
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

  if (loading) return <p className="placeholder-text">{t('vehicles.detail.loading')}</p>
  if (loadError || !vehicle) return <p className="placeholder-text">{loadError ?? t('fleet.common.notFound')}</p>

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.vehicles'), to: '/vehicles' }, { label: vehicle.internalNumber }]} />
      <BackButton to="/vehicles" label={t('vehicles.detail.back')} />
      <PageHeader
        title={`${vehicle.brand ?? ''} ${vehicle.model ?? ''}`.trim() || vehicle.internalNumber}
        subtitle={`${vehicle.internalNumber} · ${vehicle.licensePlate}`}
        action={
          <div className="vehicle-detail-actions">
            {canEdit && !editing && <Button variant="secondary" onClick={startEdit}>{t('ui.actions.edit')}</Button>}
            {canEdit && !editing && (
              <Button variant="secondary" onClick={() => setConfirmActive(vehicle.isActive ? 'deactivate' : 'activate')}>
                {vehicle.isActive ? t('vehicles.detail.deactivate') : t('vehicles.detail.reactivate')}
              </Button>
            )}
            {hasPermission('vehicles.delete') && !editing && (
              <Button variant="danger" onClick={() => setConfirmDelete(true)}>{t('ui.actions.delete')}</Button>
            )}
          </div>
        }
      />

      <div className="vehicle-detail-badges">
        <StatusBadges
          active={vehicle.isActive}
          operational={{
            label: t(OPERATIONAL_STATUS_LABELS[vehicle.operationalStatus]),
            tone: OPERATIONAL_STATUS_TONES[vehicle.operationalStatus],
            reason: vehicle.statusReason,
          }}
        />
        {vehicle.adrSuitable && <Badge tone="warning">{t('fleet.common.equipment.adrShort')}</Badge>}
        {vehicle.hasCrane && <Badge tone="info">{t('fleet.common.equipment.crane')}</Badge>}
        {vehicle.hasRefrigeration && <Badge tone="info">{t('fleet.common.equipment.refrigeration')}</Badge>}
        {vehicle.hasTailLift && <Badge tone="info">{t('fleet.common.equipment.tailLift')}</Badge>}
      </div>

      {editing && form ? (
        <VehicleForm
          mode="edit"
          initial={{ ...form, fixedDriverId: vehicle.fixedDriverId, currentDriverId: vehicle.currentDriverId }}
          isSubmitting={saving}
          onSubmit={saveEdit}
          onCancel={() => setEditing(false)}
          documentsSection={<FleetDocumentsPanel ownerType="vehicle" ownerId={vehicle.id} />}
          maintenanceSection={<MaintenancePolicySummary assetKind="Vehicle" assetId={vehicle.id} />}
        />
      ) : (
        <>
          <Tabs
            tabs={[
              { id: 'overzicht', label: t('fleet.tabs.overview') },
              { id: 'techniek', label: t('fleet.tabs.technical') },
              { id: 'toewijzing', label: t('fleet.tabs.assignment') },
              { id: 'documenten', label: t('fleet.tabs.documents') },
              ...(hasPermission('tachograph.view') ? [{ id: 'tachograaf', label: t('fleet.tabs.tachograph') }] : []),
              { id: 'leasing', label: t('fleet.tabs.leasing') },
              { id: 'onderhoud', label: t('fleet.tabs.maintenance') },
              { id: 'keuringen', label: t('fleet.tabs.inspections') },
              { id: 'schade', label: t('fleet.tabs.damage') },
              { id: 'brandstof', label: t('fleet.tabs.fuel') },
              { id: 'kpi', label: t('fleet.tabs.kpi') },
              { id: 'historiek', label: t('fleet.tabs.history') },
            ]}
            activeId={tab}
            onChange={setTab}
          />

          {tab === 'overzicht' && (
            <TabPanel tabId="overzicht">
              <div className="vehicle-detail-grid">
                <FormField label={t('fleet.form.plate')}><span>{vehicle.licensePlate}</span></FormField>
                <FormField label={t('vehicles.detail.vin')}><span>{vehicle.vin ?? '—'}</span></FormField>
                <FormField label={t('fleet.form.category')}><span>{vehicle.categoryName ?? '—'}</span></FormField>
                <FormField label={t('fleet.form.year')}><span>{vehicle.year ?? '—'}</span></FormField>
                <FormField label={t('vehicles.form.firstRegistration')}><span>{vehicle.firstRegistrationDate ?? '—'}</span></FormField>
                <FormField label={t('fleet.form.ownership')}><span>{t(OWNERSHIP_TYPE_LABELS[vehicle.ownershipType])}</span></FormField>
                <FormField label={t('vehicles.form.odometer')}><span>{formatInteger(vehicle.odometerKm)} km</span></FormField>
                <FormField label={t('vehicles.detail.fixedDriver')}><span>{vehicle.fixedDriverName ?? '—'}</span></FormField>
                <FormField label={t('vehicles.detail.currentDriver')}><span>{vehicle.currentDriverName ?? '—'}</span></FormField>
                <FormField label={t('fleet.sections.notes')} className="vehicle-detail-full"><span>{vehicle.notes ?? '—'}</span></FormField>
              </div>
            </TabPanel>
          )}

          {tab === 'techniek' && (
            <TabPanel tabId="techniek">
              <div className="vehicle-detail-grid">
                <FormField label={t('vehicles.form.fuel')}><span>{t(FUEL_TYPE_LABELS[vehicle.fuelType])}</span></FormField>
                <FormField label={t('vehicles.form.emissionClass')}><span>{vehicle.emissionClass ? t(EMISSION_CLASS_LABELS[vehicle.emissionClass]) : '—'}</span></FormField>
                <FormField label={t('fleet.form.axles')} hint={t('fleet.form.axlesHint')}><span>{vehicle.axleCount || '—'}</span></FormField>
                <FormField label={t('fleet.form.loadingMeters')}><span>{vehicle.loadingMeters ? `${vehicle.loadingMeters} ldm` : '—'}</span></FormField>
                <FormField label={t('vehicles.form.requiredLicence')}><span>{vehicle.requiredLicenceCode ?? '—'}</span></FormField>
                <FormField label={t('vehicles.detail.gvw')}><span>{vehicle.grossVehicleWeightKg !== null ? `${vehicle.grossVehicleWeightKg} kg` : '—'}</span></FormField>
                <FormField label={t('vehicles.detail.payload')}><span>{vehicle.payloadKg !== null ? `${vehicle.payloadKg} kg` : '—'}</span></FormField>
                <FormField label={t('vehicles.detail.dimensions')}>
                  <span>
                    {vehicle.lengthMeters ?? '—'} × {vehicle.widthMeters ?? '—'} × {vehicle.heightMeters ?? '—'} m
                  </span>
                </FormField>
                <FormField label={t('vehicles.detail.volume')}><span>{vehicle.volumeM3 !== null ? `${vehicle.volumeM3} m³` : '—'}</span></FormField>
                <FormField label={t('vehicles.detail.equipment')} className="vehicle-detail-full">
                  <span>
                    {[
                      vehicle.hasCrane ? t('fleet.common.equipment.crane') : null,
                      vehicle.hasRefrigeration ? t('fleet.common.equipment.refrigeration') : null,
                      vehicle.hasTailLift ? t('fleet.common.equipment.tailLift') : null,
                      vehicle.adrSuitable ? t('fleet.common.equipment.adrShort') : null,
                    ]
                      .filter(Boolean)
                      .join(' · ') || t('vehicles.detail.noEquipment')}
                  </span>
                </FormField>
              </div>
            </TabPanel>
          )}

          {tab === 'toewijzing' && (
            <TabPanel tabId="toewijzing">
              <p className="assignment-slots-note">
                <strong>{t('vehicles.detail.noteFixedLead')}</strong> {t('vehicles.detail.noteFixedText')}{' '}
                <strong>{t('vehicles.detail.noteCurrentLead')}</strong> {t('vehicles.detail.noteCurrentText')}
              </p>
              <div className="assignment-slots">
                <AssignmentSlot
                  title={t('vehicles.detail.fixedDriver')}
                  description={t('vehicles.detail.fixedDesc')}
                  assigned={vehicle.fixedDriverId && vehicle.fixedDriverName ? { label: vehicle.fixedDriverName, linkTo: `/drivers/${vehicle.fixedDriverId}` } : null}
                  canEdit={canEdit}
                  pickerLabel={t('vehicles.detail.pickerDriver')}
                  loadOptions={loadDriverOptions}
                  assign={async (driverId, replaceExisting) => {
                    await setVehicleDriver(vehicle.id, 'fixed-driver', driverId, replaceExisting)
                  }}
                  onChanged={reloadVehicle}
                />
                <AssignmentSlot
                  title={t('vehicles.detail.currentDriver')}
                  description={t('vehicles.detail.currentDesc')}
                  assigned={vehicle.currentDriverId && vehicle.currentDriverName ? { label: vehicle.currentDriverName, linkTo: `/drivers/${vehicle.currentDriverId}` } : null}
                  canEdit={canEdit}
                  pickerLabel={t('vehicles.detail.pickerDriver')}
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
              <MaintenancePolicySummary assetKind="Vehicle" assetId={id} />
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
          title={t('vehicles.detail.confirmDeleteTitle')}
          message={t('vehicles.detail.confirmDeleteMessage', { number: vehicle.internalNumber })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}

      {confirmActive && (
        <ConfirmDialog
          title={confirmActive === 'deactivate' ? t('vehicles.detail.deactivateTitle') : t('vehicles.detail.reactivateTitle')}
          message={
            confirmActive === 'deactivate'
              ? t('vehicles.detail.deactivateMessage', { number: vehicle.internalNumber })
              : t('vehicles.detail.reactivateMessage', { number: vehicle.internalNumber })
          }
          confirmLabel={confirmActive === 'deactivate' ? t('vehicles.detail.deactivate') : t('vehicles.detail.reactivate')}
          onConfirm={async () => {
            if (!id) return
            try {
              await setVehicleActive(id, confirmActive === 'activate')
              showSuccess(confirmActive === 'activate' ? t('vehicles.detail.reactivated') : t('vehicles.detail.deactivated'))
              setConfirmActive(null)
              reloadVehicle()
            } catch (err) {
              showError(err instanceof ApiError ? err.message : t('fleet.common.actionFailed'))
              setConfirmActive(null)
            }
          }}
          onCancel={() => setConfirmActive(null)}
        />
      )}
    </div>
  )
}
