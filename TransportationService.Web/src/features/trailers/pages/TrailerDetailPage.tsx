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
        if (mounted) setLoadError('Oplegger kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

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
      showSuccess('Oplegger bijgewerkt.')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'Wijzigingen konden niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!id) return
    try {
      await deleteTrailer(id)
      showSuccess('Oplegger verwijderd.')
      navigate('/trailers')
    } catch {
      showError('Oplegger kon niet worden verwijderd.')
      setConfirmDelete(false)
    }
  }

  if (loading) return <p className="placeholder-text">Oplegger laden…</p>
  if (loadError || !trailer) return <p className="placeholder-text">{loadError ?? 'Niet gevonden.'}</p>

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Opleggers', to: '/trailers' }, { label: trailer.internalNumber }]} />
      <BackButton to="/trailers" label="Terug naar opleggers" />
      <PageHeader
        title={`${trailer.brand ?? ''} ${trailer.model ?? ''}`.trim() || trailer.internalNumber}
        subtitle={`${trailer.internalNumber} · ${trailer.licensePlate}`}
        action={
          <div className="trailer-detail-actions">
            {canEdit && !editing && <Button variant="secondary" onClick={startEdit}>Bewerken</Button>}
            {canEdit && !editing && (
              <Button variant="secondary" onClick={() => setConfirmActive(trailer.isActive ? 'deactivate' : 'activate')}>
                {trailer.isActive ? 'Deactiveren' : 'Heractiveren'}
              </Button>
            )}
            {hasPermission('trailers.delete') && !editing && (
              <Button variant="danger" onClick={() => setConfirmDelete(true)}>Verwijderen</Button>
            )}
          </div>
        }
      />

      <div className="trailer-detail-badges">
        <StatusBadges
          active={trailer.isActive}
          operational={{
            label: TRAILER_STATUS_LABELS[trailer.operationalStatus],
            tone: TRAILER_STATUS_TONES[trailer.operationalStatus],
            reason: trailer.statusReason,
          }}
        />
        {trailer.adrSuitable && <Badge tone="warning">ADR</Badge>}
        {trailer.hasRefrigeration && <Badge tone="info">Koeling</Badge>}
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
              { id: 'overzicht', label: 'Overzicht' },
              { id: 'techniek', label: 'Techniek' },
              { id: 'documenten', label: 'Documenten' },
              { id: 'leasing', label: 'Leasing' },
              { id: 'onderhoud', label: 'Onderhoud' },
              { id: 'keuringen', label: 'Keuringen' },
              { id: 'schade', label: 'Schade' },
              { id: 'kpi', label: 'KPI' },
              { id: 'historiek', label: 'Historiek' },
            ]}
            activeId={tab}
            onChange={setTab}
          />

          {tab === 'overzicht' && (
            <TabPanel tabId="overzicht">
              <div className="trailer-detail-grid">
                <FormField label="Kenteken"><span>{trailer.licensePlate}</span></FormField>
                <FormField label="VIN"><span>{trailer.vin ?? '—'}</span></FormField>
                <FormField label="Categorie"><span>{trailer.categoryName ?? '—'}</span></FormField>
                <FormField label="Bouwjaar"><span>{trailer.year ?? '—'}</span></FormField>
                <FormField label="Eerste inschrijving"><span>{trailer.firstRegistrationDate ?? '—'}</span></FormField>
                <FormField label="Eigendomsvorm"><span>{TRAILER_OWNERSHIP_LABELS[trailer.ownershipType]}</span></FormField>
                <FormField label="Notities" className="trailer-detail-full"><span>{trailer.notes ?? '—'}</span></FormField>
              </div>
            </TabPanel>
          )}

          {tab === 'techniek' && (
            <TabPanel tabId="techniek">
              <div className="trailer-detail-grid">
                <FormField label="Aantal assen" hint="Voor Maut/tolberekening."><span>{trailer.axleCount || '—'}</span></FormField>
                <FormField label="Laadmeters"><span>{trailer.loadingMeters ? `${trailer.loadingMeters} ldm` : '—'}</span></FormField>
                <FormField label="Laadvermogen"><span>{trailer.capacityKg !== null ? `${trailer.capacityKg} kg` : '—'}</span></FormField>
                <FormField label="Afmetingen (L×B×H)">
                  <span>
                    {trailer.lengthMeters ?? '—'} × {trailer.widthMeters ?? '—'} × {trailer.heightMeters ?? '—'} m
                  </span>
                </FormField>
                <FormField label="Volume"><span>{trailer.volumeM3 !== null ? `${trailer.volumeM3} m³` : '—'}</span></FormField>
                <FormField label="Uitrusting" className="trailer-detail-full">
                  <span>
                    {[trailer.hasRefrigeration ? 'Koeling' : null, trailer.adrSuitable ? 'ADR' : null]
                      .filter(Boolean)
                      .join(' · ') || 'Geen bijzondere uitrusting'}
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
          title="Oplegger verwijderen"
          message={`Weet je zeker dat je oplegger ${trailer.internalNumber} wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}

      {confirmActive && (
        <ConfirmDialog
          title={confirmActive === 'deactivate' ? 'Oplegger deactiveren' : 'Oplegger heractiveren'}
          message={
            confirmActive === 'deactivate'
              ? `${trailer.internalNumber} deactiveren? Historiek blijft behouden, maar de oplegger is niet meer inzetbaar voor nieuwe ritten.`
              : `${trailer.internalNumber} heractiveren? De oplegger is daarna weer inzetbaar.`
          }
          confirmLabel={confirmActive === 'deactivate' ? 'Deactiveren' : 'Heractiveren'}
          onConfirm={async () => {
            if (!id) return
            try {
              await setTrailerActive(id, confirmActive === 'activate')
              showSuccess(confirmActive === 'activate' ? 'Oplegger geheractiveerd.' : 'Oplegger gedeactiveerd.')
              setConfirmActive(null)
              const refreshed = await getTrailer(id)
              setTrailer(refreshed)
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
