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
import { AuditHistoryPanel } from '../../auditing/components/AuditHistoryPanel'
import { FleetDocumentsPanel } from '../../fleet-documents/components/FleetDocumentsPanel'
import { MaintenancePanel } from '../../maintenance/components/MaintenancePanel'
import { MaintenancePolicySummary } from '../../maintenance-policies/components/MaintenancePolicySummary'
import { DamagePanel } from '../../damage/components/DamagePanel'
import { InspectionsPanel } from '../../inspections/components/InspectionsPanel'
import { FleetKpiPanel } from '../../fleet-kpi/FleetKpiPanel'
import { LeasingPanel } from '../../fleet-compliance/LeasingPanel'
import { deleteTrailer, getTrailer, setTrailerActive, updateTrailer } from '../api/trailersApi'
import { TrailerForm } from '../components/TrailerForm'
import {
  TRAILER_OWNERSHIP_LABELS,
  TRAILER_STATUS_LABELS,
  TRAILER_STATUS_TONES,
  type TrailerDetail,
  type TrailerInput,
} from '../types'
import './trailer-form.css'

const TAB_IDS = ['overzicht', 'techniek', 'documenten', 'leasing', 'onderhoud', 'keuringen', 'schade', 'kpi', 'historiek'] as const
type TabId = (typeof TAB_IDS)[number]

export function TrailerDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()

  const [trailer, setTrailer] = useState<TrailerDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<TrailerInput | null>(null)
  const [saving, setSaving] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [confirmActive, setConfirmActive] = useState<null | 'activate' | 'deactivate'>(null)

  const requestedTab = searchParams.get('tab')
  const tab: TabId = TAB_IDS.includes(requestedTab as TabId) ? (requestedTab as TabId) : 'overzicht'

  useEffect(() => {
    if (!id) return
    let mounted = true
    getTrailer(id)
      .then((result) => {
        if (!mounted) return
        setTrailer(result)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('trailers.detail.loadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [id, t])

  const loading = trailer === null && loadError === null
  const canEdit = hasPermission('trailers.edit')

  function setTab(next: string) {
    setSearchParams(next === 'overzicht' ? {} : { tab: next }, { replace: true })
  }

  function startEdit() {
    if (!trailer) return
    setForm({
      licensePlate: trailer.licensePlate,
      vin: trailer.vin,
      categoryId: trailer.categoryId,
      brand: trailer.brand,
      model: trailer.model,
      year: trailer.year,
      firstRegistrationDate: trailer.firstRegistrationDate,
      capacityKg: trailer.capacityKg,
      lengthMeters: trailer.lengthMeters,
      widthMeters: trailer.widthMeters,
      heightMeters: trailer.heightMeters,
      volumeM3: trailer.volumeM3,
      axleCount: trailer.axleCount,
      loadingMeters: trailer.loadingMeters,
      hasRefrigeration: trailer.hasRefrigeration,
      adrSuitable: trailer.adrSuitable,
      ownershipType: trailer.ownershipType,
      operationalStatus: trailer.operationalStatus,
      statusReason: trailer.statusReason,
      isActive: trailer.isActive,
      notes: trailer.notes,
      volumeIsManual: trailer.volumeIsManual,
    })
    setEditing(true)
  }

  async function saveEdit(values: TrailerInput) {
    if (!id) return
    setSaving(true)
    try {
      const updated = await updateTrailer(id, values)
      setTrailer(updated)
      setEditing(false)
      showSuccess(t('trailers.detail.updated'))
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('fleet.common.saveChangesFailed'))
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!id) return
    try {
      await deleteTrailer(id)
      showSuccess(t('trailers.detail.deleted'))
      navigate('/trailers')
    } catch {
      showError(t('trailers.detail.deleteFailed'))
      setConfirmDelete(false)
    }
  }

  if (loading) return <p className="placeholder-text">{t('trailers.detail.loading')}</p>
  if (loadError || !trailer) return <p className="placeholder-text">{loadError ?? t('fleet.common.notFound')}</p>

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.trailers'), to: '/trailers' }, { label: trailer.internalNumber }]} />
      <BackButton to="/trailers" label={t('trailers.detail.back')} />
      <PageHeader
        title={`${trailer.brand ?? ''} ${trailer.model ?? ''}`.trim() || trailer.internalNumber}
        subtitle={`${trailer.internalNumber} · ${trailer.licensePlate}`}
        action={
          <div className="trailer-detail-actions">
            {canEdit && !editing && <Button variant="secondary" onClick={startEdit}>{t('ui.actions.edit')}</Button>}
            {canEdit && !editing && (
              <Button variant="secondary" onClick={() => setConfirmActive(trailer.isActive ? 'deactivate' : 'activate')}>
                {trailer.isActive ? t('vehicles.detail.deactivate') : t('vehicles.detail.reactivate')}
              </Button>
            )}
            {hasPermission('trailers.delete') && !editing && (
              <Button variant="danger" onClick={() => setConfirmDelete(true)}>{t('ui.actions.delete')}</Button>
            )}
          </div>
        }
      />

      <div className="trailer-detail-badges">
        <StatusBadges
          active={trailer.isActive}
          operational={{
            label: t(TRAILER_STATUS_LABELS[trailer.operationalStatus]),
            tone: TRAILER_STATUS_TONES[trailer.operationalStatus],
            reason: trailer.statusReason,
          }}
        />
        {trailer.adrSuitable && <Badge tone="warning">{t('fleet.common.equipment.adrShort')}</Badge>}
        {trailer.hasRefrigeration && <Badge tone="info">{t('fleet.common.equipment.refrigeration')}</Badge>}
      </div>

      {editing && form ? (
        <TrailerForm
          mode="edit"
          initial={form}
          isSubmitting={saving}
          onSubmit={saveEdit}
          onCancel={() => setEditing(false)}
          documentsSection={<FleetDocumentsPanel ownerType="trailer" ownerId={trailer.id} />}
          maintenanceSection={<MaintenancePolicySummary assetKind="Trailer" assetId={trailer.id} />}
        />
      ) : (
        <>
          <Tabs
            tabs={[
              { id: 'overzicht', label: t('fleet.tabs.overview') },
              { id: 'techniek', label: t('fleet.tabs.technical') },
              { id: 'documenten', label: t('fleet.tabs.documents') },
              { id: 'leasing', label: t('fleet.tabs.leasing') },
              { id: 'onderhoud', label: t('fleet.tabs.maintenance') },
              { id: 'keuringen', label: t('fleet.tabs.inspections') },
              { id: 'schade', label: t('fleet.tabs.damage') },
              { id: 'kpi', label: t('fleet.tabs.kpi') },
              { id: 'historiek', label: t('fleet.tabs.history') },
            ]}
            activeId={tab}
            onChange={setTab}
          />

          {tab === 'overzicht' && (
            <TabPanel tabId="overzicht">
              <div className="trailer-detail-grid">
                <FormField label={t('fleet.form.plate')}><span>{trailer.licensePlate}</span></FormField>
                <FormField label={t('vehicles.detail.vin')}><span>{trailer.vin ?? '—'}</span></FormField>
                <FormField label={t('fleet.form.category')}><span>{trailer.categoryName ?? '—'}</span></FormField>
                <FormField label={t('fleet.form.year')}><span>{trailer.year ?? '—'}</span></FormField>
                <FormField label={t('vehicles.form.firstRegistration')}><span>{trailer.firstRegistrationDate ?? '—'}</span></FormField>
                <FormField label={t('fleet.form.ownership')}><span>{t(TRAILER_OWNERSHIP_LABELS[trailer.ownershipType])}</span></FormField>
                <FormField label={t('fleet.sections.notes')} className="trailer-detail-full"><span>{trailer.notes ?? '—'}</span></FormField>
              </div>
            </TabPanel>
          )}

          {tab === 'techniek' && (
            <TabPanel tabId="techniek">
              <div className="trailer-detail-grid">
                <FormField label={t('fleet.form.axles')} hint={t('fleet.form.axlesHint')}><span>{trailer.axleCount || '—'}</span></FormField>
                <FormField label={t('fleet.form.loadingMeters')}><span>{trailer.loadingMeters ? `${trailer.loadingMeters} ldm` : '—'}</span></FormField>
                <FormField label={t('trailers.detail.payload')}><span>{trailer.capacityKg !== null ? `${trailer.capacityKg} kg` : '—'}</span></FormField>
                <FormField label={t('vehicles.detail.dimensions')}>
                  <span>
                    {trailer.lengthMeters ?? '—'} × {trailer.widthMeters ?? '—'} × {trailer.heightMeters ?? '—'} m
                  </span>
                </FormField>
                <FormField label={t('vehicles.detail.volume')}><span>{trailer.volumeM3 !== null ? `${trailer.volumeM3} m³` : '—'}</span></FormField>
                <FormField label={t('vehicles.detail.equipment')} className="trailer-detail-full">
                  <span>
                    {[trailer.hasRefrigeration ? t('fleet.common.equipment.refrigeration') : null, trailer.adrSuitable ? t('fleet.common.equipment.adrShort') : null]
                      .filter(Boolean)
                      .join(' · ') || t('vehicles.detail.noEquipment')}
                  </span>
                </FormField>
              </div>
            </TabPanel>
          )}

          {tab === 'documenten' && id && (
            <TabPanel tabId="documenten">
              <FleetDocumentsPanel ownerType="trailer" ownerId={id} />
            </TabPanel>
          )}
          {tab === 'leasing' && id && (
            <TabPanel tabId="leasing">
              <LeasingPanel ownerType="trailer" ownerId={id} />
            </TabPanel>
          )}
          {tab === 'onderhoud' && id && (
            <TabPanel tabId="onderhoud">
              <MaintenancePolicySummary assetKind="Trailer" assetId={id} />
              <MaintenancePanel ownerType="trailer" ownerId={id} />
            </TabPanel>
          )}
          {tab === 'keuringen' && id && (
            <TabPanel tabId="keuringen">
              <InspectionsPanel ownerType="trailer" ownerId={id} />
            </TabPanel>
          )}
          {tab === 'schade' && id && (
            <TabPanel tabId="schade">
              <DamagePanel ownerType="trailer" ownerId={id} />
            </TabPanel>
          )}
          {tab === 'kpi' && id && (
            <TabPanel tabId="kpi">
              <FleetKpiPanel ownerType="trailer" ownerId={id} />
            </TabPanel>
          )}
          {tab === 'historiek' && (
            <TabPanel tabId="historiek">
              <AuditHistoryPanel entityType="Trailer" entityId={trailer.id} />
            </TabPanel>
          )}
        </>
      )}

      {confirmDelete && (
        <ConfirmDialog
          title={t('trailers.detail.confirmDeleteTitle')}
          message={t('trailers.detail.confirmDeleteMessage', { number: trailer.internalNumber })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}

      {confirmActive && (
        <ConfirmDialog
          title={confirmActive === 'deactivate' ? t('trailers.detail.deactivateTitle') : t('trailers.detail.reactivateTitle')}
          message={
            confirmActive === 'deactivate'
              ? t('trailers.detail.deactivateMessage', { number: trailer.internalNumber })
              : t('trailers.detail.reactivateMessage', { number: trailer.internalNumber })
          }
          confirmLabel={confirmActive === 'deactivate' ? t('vehicles.detail.deactivate') : t('vehicles.detail.reactivate')}
          onConfirm={async () => {
            if (!id) return
            try {
              await setTrailerActive(id, confirmActive === 'activate')
              showSuccess(confirmActive === 'activate' ? t('trailers.detail.reactivated') : t('trailers.detail.deactivated'))
              setConfirmActive(null)
              const refreshed = await getTrailer(id)
              setTrailer(refreshed)
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
