import { useEffect, useState, type ReactNode } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DetailCard } from '../../../components/ui/DetailCard'
import { FactList } from '../../../components/ui/FactList'
import { StatusBadges } from '../../../components/ui/StatusBadges'
import { TabPanel, Tabs } from '../../../components/ui/Tabs'
import { useDetailTab, type DetailTabAlias } from '../../../hooks/useDetailTab'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { ApiError } from '../../../api/apiClient'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { formatInteger } from '../../../utils/numbers'
import { AuditHistoryPanel } from '../../auditing/components/AuditHistoryPanel'
import { FleetHistoryPanel } from '../../fleet-history/FleetHistoryPanel'
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
import { searchActiveDriverOptions } from '../../drivers/api/driverOptions'
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
import '../detail/vehicle-detail.css'
import { formatDate } from '../../../utils/dates'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { AssetDeactivateDialog, AssetDeleteDialog } from '../../fleet-readiness/AssetLifecycleDialogs'
import { blockedWhileAvailable } from '../../fleet-readiness/readiness'
import { useAssetReadiness } from '../../fleet-readiness/useAssetReadiness'
import { ActionMenu } from '../../../components/ui/ActionMenu'
import { VehicleOverview } from '../detail/VehicleOverview'
import { useVehicleCompliance } from '../detail/useVehicleCompliance'
import { FOCUS_TAB, type VehicleFocus, type VehicleTab } from '../detail/vehicleCompliance'

/**
 * B16: twelve tabs became six groups. The retired ids keep working: an old bookmark opens the
 * group that now hosts that block and scrolls to it (`?focus=`).
 */
const TAB_ALIASES: Partial<Record<string, DetailTabAlias<VehicleTab>>> = Object.fromEntries(
  (['toewijzing', 'tachograaf', 'keuringen', 'schade', 'leasing', 'brandstof', 'kpi'] as const).map((focus) => [
    focus,
    { tab: FOCUS_TAB[focus], params: { focus } },
  ]),
)

export function VehicleDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [vehicle, setVehicle] = useState<VehicleDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  /** FL-M10: 404 → "niet gevonden" with a way back; 403 → no access; anything else → retry. */
  const [loadErrorKind, setLoadErrorKind] = useState<'notFound' | 'forbidden' | 'failed'>('failed')
  /** Re-fetches the readiness block after edits that may change it. */
  const [readinessToken, setReadinessToken] = useState(0)
  const [reloadToken, setReloadToken] = useState(0)
  const headerReadiness = useAssetReadiness('vehicle', id, readinessToken)
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<VehicleInput | null>(null)
  const [saving, setSaving] = useState(false)
  /** FL-M5/M6: the server's validation reason stays visible above the edit form. */
  const [editError, setEditError] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [confirmActive, setConfirmActive] = useState<null | 'activate' | 'deactivate'>(null)

  const canViewDocumentsGroup = hasPermission('fleet_documents.view') || hasPermission('inspections.view') || hasPermission('tachograph.view')
  const canViewMaintenanceGroup =
    hasPermission('maintenance.view') || hasPermission('damage_reports.view') || hasPermission('maintenance_policies.view') || hasPermission('maintenance_policies.manage')
  // FL-H3: the fleet timeline is readable by every vehicle viewer (amounts are shaped server-side);
  // the raw audit trail stays an extra for audit_logs.view holders.
  const canViewHistory = hasPermission('vehicles.view')
  const canViewAudit = hasPermission('audit_logs.view')
  const tabIds: VehicleTab[] = [
    'overzicht',
    'techniek',
    ...(canViewDocumentsGroup ? (['documenten'] as const) : []),
    ...(canViewMaintenanceGroup ? (['onderhoud'] as const) : []),
    'kosten',
    ...(canViewHistory ? (['historiek'] as const) : []),
  ]
  const { tab, setTab, searchParams } = useDetailTab<VehicleTab>({ ids: tabIds, defaultId: 'overzicht', aliases: TAB_ALIASES })
  const focus = searchParams.get('focus')
  const compliance = useVehicleCompliance(id ?? '', Boolean(id) && tab === 'overzicht')

  useEffect(() => {
    if (!id) return
    let mounted = true
    getVehicle(id)
      .then((result) => {
        if (!mounted) return
        setVehicle(result)
        setLoadError(null)
      })
      .catch((err) => {
        if (!mounted) return
        const kind = err instanceof ApiError && err.status === 404 ? 'notFound' : err instanceof ApiError && err.status === 403 ? 'forbidden' : 'failed'
        setLoadErrorKind(kind)
        setLoadError(t(kind === 'notFound' ? 'vehicles.detail.notFound' : kind === 'forbidden' ? 'vehicles.detail.forbidden' : 'vehicles.detail.loadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [id, t, reloadToken])

  const loading = vehicle === null && loadError === null
  const canEdit = hasPermission('vehicles.edit')

  /** Opens the group hosting a block (attention line, overview link) and scrolls to it. */
  function openFocus(next: VehicleFocus) {
    setTab(FOCUS_TAB[next], { focus: next })
  }

  // Scroll to the requested block once its group has rendered (deep links, attention jumps).
  // The panels load their own rows after mounting and push the later blocks down, so the jump
  // is repeated once they have had time to render.
  useEffect(() => {
    if (!focus || !vehicle) return
    const scroll = () => document.getElementById(`vehicle-block-${focus}`)?.scrollIntoView({ block: 'start' })
    scroll()
    const timer = window.setTimeout(scroll, 700)
    return () => window.clearTimeout(timer)
  }, [focus, tab, vehicle])

  function reloadVehicle() {
    if (!id) return
    getVehicle(id)
      .then((next) => {
        setVehicle(next)
        setReadinessToken((n) => n + 1)
      })
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
    setEditError(null)
    setEditing(true)
  }

  async function saveEdit(values: VehicleInput) {
    if (!id) return
    setSaving(true)
    setEditError(null)
    try {
      const updated = await updateVehicle(id, values)
      setVehicle(updated)
      setReadinessToken((n) => n + 1)
      setEditing(false)
      showSuccess(t('vehicles.detail.updated'))
    } catch (err) {
      const message = localizeApiError(t, err, t('fleet.common.saveChangesFailed'))
      setEditError(message)
      showError(message)
    } finally {
      setSaving(false)
    }
  }

  /** FL-B1: errors (409 "nog gekoppeld aan …") propagate to the dialog, which shows the reason. */
  async function handleDelete() {
    if (!id) return
    await deleteVehicle(id)
    showSuccess(t('vehicles.detail.deleted'))
    navigate('/vehicles')
  }

  async function applyActive(isActive: boolean) {
    if (!id) return
    try {
      await setVehicleActive(id, isActive)
      showSuccess(isActive ? t('vehicles.detail.reactivated') : t('vehicles.detail.deactivated'))
      reloadVehicle()
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('fleet.common.actionFailed'))
    } finally {
      setConfirmActive(null)
    }
  }

  if (loading) return <p className="placeholder-text">{t('vehicles.detail.loading')}</p>
  if (loadError || !vehicle) {
    return (
      <ErrorState
        message={loadError ?? t('vehicles.detail.notFound')}
        onRetry={loadErrorKind === 'failed' ? () => { setLoadError(null); setReloadToken((n) => n + 1) } : undefined}
        backTo="/vehicles"
        backLabel={t('vehicles.detail.back')}
      />
    )
  }

  const hasFuel = hasPermission('fuel.view')
  const hasKpi = hasPermission('kpi.view')
  const block = (blockId: VehicleFocus, content: ReactNode) => (
    <div id={`vehicle-block-${blockId}`} className="vehicle-group-block">
      {content}
    </div>
  )

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.vehicles'), to: '/vehicles' }, { label: vehicle.internalNumber }]} />
      <PageHeader
        title={`${vehicle.brand ?? ''} ${vehicle.model ?? ''}`.trim() || vehicle.internalNumber}
        subtitle={[vehicle.internalNumber, vehicle.licensePlate, vehicle.categoryName].filter(Boolean).join(' · ')}
        badges={
          <>
            <StatusBadges
              active={vehicle.isActive}
              operational={blockedWhileAvailable(vehicle.operationalStatus, headerReadiness?.level)
                ? { label: t('fleet.readiness.level.Blocked'), tone: 'danger' }
                : {
                    label: t(OPERATIONAL_STATUS_LABELS[vehicle.operationalStatus]),
                    tone: OPERATIONAL_STATUS_TONES[vehicle.operationalStatus],
                  }}
            />
            {vehicle.adrSuitable && <Badge tone="warning">{t('fleet.common.equipment.adrShort')}</Badge>}
          </>
        }
        action={
          !editing && (
            <>
              <ActionMenu
                items={[
                  ...(canEdit
                    ? [{
                        key: 'active',
                        label: vehicle.isActive ? t('vehicles.detail.deactivate') : t('vehicles.detail.reactivate'),
                        onSelect: () => setConfirmActive(vehicle.isActive ? 'deactivate' : 'activate'),
                        danger: vehicle.isActive,
                      }]
                    : []),
                  ...(hasPermission('vehicles.delete')
                    ? [{ key: 'delete', label: t('ui.actions.delete'), onSelect: () => setConfirmDelete(true), danger: true }]
                    : []),
                ]}
              />
              {canEdit && <Button onClick={startEdit}>{t('ui.actions.edit')}</Button>}
            </>
          )
        }
      />

      {editing && form ? (
        <VehicleForm
          mode="edit"
          initial={{ ...form, fixedDriverId: vehicle.fixedDriverId, currentDriverId: vehicle.currentDriverId }}
          isSubmitting={saving}
          submitError={editError}
          onSubmit={saveEdit}
          onCancel={() => setEditing(false)}
          documentsSection={<FleetDocumentsPanel ownerType="vehicle" ownerId={vehicle.id} />}
          maintenanceSection={<MaintenancePolicySummary assetKind="Vehicle" assetId={vehicle.id} />}
        />
      ) : (
        <>
          <Tabs
            tabs={[
              { id: 'overzicht', label: t('vehicles.detail.groups.overview') },
              { id: 'techniek', label: t('vehicles.detail.groups.technical') },
              ...(canViewDocumentsGroup ? [{ id: 'documenten', label: t('vehicles.detail.groups.documents') }] : []),
              ...(canViewMaintenanceGroup ? [{ id: 'onderhoud', label: t('vehicles.detail.groups.maintenance') }] : []),
              { id: 'kosten', label: t('vehicles.detail.groups.costs') },
              ...(canViewHistory ? [{ id: 'historiek', label: t('vehicles.detail.groups.history') }] : []),
            ]}
            activeId={tab}
            onChange={(next) => setTab(next as VehicleTab)}
          />

          {tab === 'overzicht' && (
            <TabPanel tabId="overzicht">
              <VehicleOverview
                vehicle={vehicle}
                compliance={compliance.data}
                readinessToken={readinessToken}
                complianceLoading={compliance.isLoading}
                canEdit={canEdit}
                onFocus={openFocus}
                onOpenTechnical={() => setTab('techniek')}
              />
            </TabPanel>
          )}

          {tab === 'techniek' && (
            <TabPanel tabId="techniek">
              <DetailCard title={t('vehicles.detail.overview.technicalBlock')}>
                <FactList
                  columns={2}
                  facts={[
                    { key: 'plate', label: t('fleet.form.plate'), value: vehicle.licensePlate },
                    { key: 'vin', label: t('vehicles.detail.vin'), value: vehicle.vin },
                    { key: 'category', label: t('fleet.form.category'), value: vehicle.categoryName },
                    { key: 'year', label: t('fleet.form.year'), value: vehicle.year },
                    { key: 'firstReg', label: t('vehicles.form.firstRegistration'), value: vehicle.firstRegistrationDate ? formatDate(vehicle.firstRegistrationDate) : null },
                    { key: 'odometer', label: t('vehicles.form.odometer'), value: `${formatInteger(vehicle.odometerKm)} km` },
                    { key: 'fuel', label: t('vehicles.form.fuel'), value: t(FUEL_TYPE_LABELS[vehicle.fuelType]) },
                    { key: 'emission', label: t('vehicles.form.emissionClass'), value: vehicle.emissionClass ? t(EMISSION_CLASS_LABELS[vehicle.emissionClass]) : null },
                    { key: 'consumption', label: t('vehicles.detail.overview.consumption'), value: vehicle.consumptionLPer100Km !== null ? `${vehicle.consumptionLPer100Km} l/100 km` : null },
                    { key: 'licence', label: t('vehicles.form.requiredLicence'), value: vehicle.requiredLicenceCode },
                    { key: 'gvw', label: t('vehicles.detail.gvw'), value: vehicle.grossVehicleWeightKg !== null ? `${formatInteger(vehicle.grossVehicleWeightKg)} kg` : null },
                    { key: 'payload', label: t('vehicles.detail.payload'), value: vehicle.payloadKg !== null ? `${formatInteger(vehicle.payloadKg)} kg` : null },
                    { key: 'axles', label: t('fleet.form.axles'), value: vehicle.axleCount || null },
                    { key: 'ldm', label: t('fleet.form.loadingMeters'), value: vehicle.loadingMeters ? `${vehicle.loadingMeters} ldm` : null },
                    {
                      key: 'dimensions',
                      label: t('vehicles.detail.dimensions'),
                      value:
                        vehicle.lengthMeters === null && vehicle.widthMeters === null && vehicle.heightMeters === null
                          ? null
                          : `${vehicle.lengthMeters ?? '—'} × ${vehicle.widthMeters ?? '—'} × ${vehicle.heightMeters ?? '—'} m`,
                    },
                    { key: 'volume', label: t('vehicles.detail.volume'), value: vehicle.volumeM3 !== null ? `${vehicle.volumeM3} m³` : null },
                    { key: 'ownership', label: t('fleet.form.ownership'), value: t(OWNERSHIP_TYPE_LABELS[vehicle.ownershipType]) },
                    {
                      key: 'equipment',
                      label: t('vehicles.detail.equipment'),
                      value:
                        [
                          vehicle.hasCrane ? t('fleet.common.equipment.crane') : null,
                          vehicle.hasRefrigeration ? t('fleet.common.equipment.refrigeration') : null,
                          vehicle.hasTailLift ? t('fleet.common.equipment.tailLift') : null,
                          vehicle.adrSuitable ? t('fleet.common.equipment.adrShort') : null,
                        ]
                          .filter(Boolean)
                          .join(' · ') || t('vehicles.detail.noEquipment'),
                    },
                  ]}
                />
              </DetailCard>

              {block(
                'toewijzing',
                <DetailCard title={t('vehicles.detail.overview.assignmentBlock')}>
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
                      searchOptions={searchActiveDriverOptions}
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
                      searchOptions={searchActiveDriverOptions}
                      assign={async (driverId, replaceExisting) => {
                        await setVehicleDriver(vehicle.id, 'current-driver', driverId, replaceExisting)
                      }}
                      onChanged={reloadVehicle}
                    />
                  </div>
                </DetailCard>,
              )}
            </TabPanel>
          )}

          {tab === 'documenten' && id && (
            <TabPanel tabId="documenten">
              {hasPermission('fleet_documents.view') && block('documenten', <FleetDocumentsPanel ownerType="vehicle" ownerId={id} />)}
              {hasPermission('inspections.view') && block('keuringen', <InspectionsPanel ownerType="vehicle" ownerId={id} />)}
              {hasPermission('tachograph.view') && block('tachograaf', <TachographPanel vehicleId={id} />)}
            </TabPanel>
          )}

          {tab === 'onderhoud' && id && (
            <TabPanel tabId="onderhoud">
              {block(
                'onderhoud',
                <>
                  <MaintenancePolicySummary assetKind="Vehicle" assetId={id} />
                  {hasPermission('maintenance.view') && <MaintenancePanel ownerType="vehicle" ownerId={id} />}
                </>,
              )}
              {hasPermission('damage_reports.view') && block('schade', <DamagePanel ownerType="vehicle" ownerId={id} />)}
            </TabPanel>
          )}

          {tab === 'kosten' && id && (
            <TabPanel tabId="kosten">
              {block('leasing', <LeasingPanel ownerType="vehicle" ownerId={id} />)}
              {hasFuel && block('brandstof', <FuelPanel vehicleId={id} />)}
              {hasKpi && block('kpi', <FleetKpiPanel ownerType="vehicle" ownerId={id} />)}
            </TabPanel>
          )}

          {tab === 'historiek' && (
            <TabPanel tabId="historiek">
              <FleetHistoryPanel assetKind="vehicle" assetId={vehicle.id} />
              {canViewAudit && (
                <details className="fleet-history-audit">
                  <summary>{t('fleet.history.auditTrail')}</summary>
                  <AuditHistoryPanel entityType="Vehicle" entityId={vehicle.id} />
                </details>
              )}
            </TabPanel>
          )}
        </>
      )}

      {confirmDelete && (
        <AssetDeleteDialog
          kind="vehicle"
          assetId={vehicle.id}
          number={vehicle.internalNumber}
          isActive={vehicle.isActive}
          canDeactivate={canEdit}
          onDelete={handleDelete}
          onDeactivate={() => {
            setConfirmDelete(false)
            setConfirmActive('deactivate')
          }}
          onCancel={() => setConfirmDelete(false)}
        />
      )}

      {confirmActive === 'deactivate' && (
        <AssetDeactivateDialog
          kind="vehicle"
          assetId={vehicle.id}
          message={t('vehicles.detail.deactivateMessage', { number: vehicle.internalNumber })}
          confirmLabel={t('vehicles.detail.deactivate')}
          onConfirm={() => void applyActive(false)}
          onCancel={() => setConfirmActive(null)}
        />
      )}
      {confirmActive === 'activate' && (
        <ConfirmDialog
          title={t('vehicles.detail.reactivateTitle')}
          message={t('vehicles.detail.reactivateMessage', { number: vehicle.internalNumber })}
          confirmLabel={t('vehicles.detail.reactivate')}
          onConfirm={() => void applyActive(true)}
          onCancel={() => setConfirmActive(null)}
        />
      )}
    </div>
  )
}
