import { useEffect, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { BackButton } from '../../../components/ui/BackButton'
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
import { LookupSelect } from '../../master-data/components/LookupSelect'
import { FleetDocumentsPanel } from '../../fleet-documents/components/FleetDocumentsPanel'
import { MaintenancePanel } from '../../maintenance/components/MaintenancePanel'
import { DamagePanel } from '../../damage/components/DamagePanel'
import { InspectionsPanel } from '../../inspections/components/InspectionsPanel'
import { FleetKpiPanel } from '../../fleet-kpi/FleetKpiPanel'
import { LeasingPanel } from '../../fleet-compliance/LeasingPanel'
import { computeVolumeM3 } from '../../../utils/volume'
import { deleteTrailer, getTrailer, setTrailerActive, updateTrailer } from '../api/trailersApi'
import {
  TRAILER_OWNERSHIP_LABELS,
  TRAILER_STATUS_LABELS,
  TRAILER_STATUS_TONES,
  type TrailerDetail,
  type TrailerInput,
  type TrailerOperationalStatus,
  type TrailerOwnershipType,
} from '../types'
import './trailer-form.css'

const TAB_IDS = ['overzicht', 'techniek', 'documenten', 'leasing', 'onderhoud', 'keuringen', 'schade', 'kpi', 'historiek'] as const
type TabId = (typeof TAB_IDS)[number]

function numberOrNull(value: string): number | null {
  if (value.trim() === '') return null
  const parsed = Number(value.replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : null
}

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
  const [dirty, setDirty] = useState(false)
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
    setDirty(false)
    setEditing(true)
  }

  function set<K extends keyof TrailerInput>(key: K, value: TrailerInput[K]) {
    setForm((f) => (f ? { ...f, [key]: value } : f))
    setDirty(true)
  }

  async function saveEdit() {
    if (!id || !form) return
    setSaving(true)
    setDirty(false)
    try {
      const updated = await updateTrailer(id, form)
      setTrailer(updated)
      setEditing(false)
      showSuccess('Oplegger bijgewerkt.')
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
        <form
          className="trailer-form"
          onSubmit={(e) => {
            e.preventDefault()
            void saveEdit()
          }}
        >
          <UnsavedChangesGuard when={dirty && !saving} />

          <FormSection title="Status" columns={3} description="Administratief actief en operationele status zijn twee aparte begrippen.">
            <FormField label="Operationele status" htmlFor="t-status">
              <select id="t-status" value={form.operationalStatus} onChange={(e) => set('operationalStatus', e.target.value as TrailerOperationalStatus)} disabled={saving}>
                {Object.entries(TRAILER_STATUS_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            {form.operationalStatus !== 'Available' && (
              <FormField label="Reden" htmlFor="t-status-reason" hint="Waarom is de oplegger niet beschikbaar?">
                <input id="t-status-reason" value={form.statusReason ?? ''} onChange={(e) => set('statusReason', e.target.value || null)} maxLength={500} disabled={saving} />
              </FormField>
            )}
          </FormSection>

          <FormSection title="Basisgegevens" columns={3}>
            <FormField label="Kenteken" htmlFor="t-plate" required>
              <input id="t-plate" value={form.licensePlate} onChange={(e) => set('licensePlate', e.target.value)} disabled={saving} maxLength={20} />
            </FormField>
            <FormField label="VIN" htmlFor="t-vin">
              <input id="t-vin" value={form.vin ?? ''} onChange={(e) => set('vin', e.target.value || null)} disabled={saving} maxLength={50} />
            </FormField>
            <FormField label="Categorie" htmlFor="t-category">
              <LookupSelect
                id="t-category"
                basePath="/api/trailer-categories"
                managePermission="trailer_categories.manage"
                singular="opleggercategorie"
                value={form.categoryId}
                onChange={(v) => set('categoryId', v)}
                placeholder="— Geen —"
                disabled={saving}
              />
            </FormField>
            <FormField label="Merk" htmlFor="t-brand">
              <input id="t-brand" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value || null)} disabled={saving} maxLength={100} />
            </FormField>
            <FormField label="Model" htmlFor="t-model">
              <input id="t-model" value={form.model ?? ''} onChange={(e) => set('model', e.target.value || null)} disabled={saving} maxLength={100} />
            </FormField>
            <FormField label="Bouwjaar" htmlFor="t-year" hint={`Maximaal ${new Date().getFullYear()}.`}>
              <input
                id="t-year"
                type="number"
                min={1900}
                max={new Date().getFullYear()}
                value={form.year ?? ''}
                onChange={(e) => set('year', e.target.value ? Number(e.target.value) : null)}
                disabled={saving}
              />
            </FormField>
            <FormField label="Eerste inschrijving" htmlFor="t-firstreg">
              <input id="t-firstreg" type="date" value={form.firstRegistrationDate ?? ''} onChange={(e) => set('firstRegistrationDate', e.target.value || null)} disabled={saving} />
            </FormField>
            <FormField label="Eigendomsvorm" htmlFor="t-ownership">
              <select id="t-ownership" value={form.ownershipType} onChange={(e) => set('ownershipType', e.target.value as TrailerOwnershipType)} disabled={saving}>
                {Object.entries(TRAILER_OWNERSHIP_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
          </FormSection>

          <FormSection title="Techniek" columns={3}>
            <FormField label="Aantal assen" htmlFor="t-axles" hint="Voor Maut/tolberekening.">
              <input id="t-axles" type="number" min={0} max={12} value={form.axleCount} onChange={(e) => set('axleCount', Number(e.target.value) || 0)} disabled={saving} />
            </FormField>
            <FormField label="Laadmeters" htmlFor="t-ldm">
              <input id="t-ldm" type="number" min={0} step="0.01" value={form.loadingMeters} onChange={(e) => set('loadingMeters', Number(e.target.value) || 0)} disabled={saving} />
            </FormField>
            <FormField label="Laadvermogen (kg)" htmlFor="t-capacity">
              <input id="t-capacity" inputMode="decimal" value={form.capacityKg ?? ''} onChange={(e) => set('capacityKg', numberOrNull(e.target.value))} disabled={saving} />
            </FormField>
            <FormField label="Lengte (m)" htmlFor="t-length">
              <input id="t-length" inputMode="decimal" value={form.lengthMeters ?? ''} onChange={(e) => set('lengthMeters', numberOrNull(e.target.value))} disabled={saving} />
            </FormField>
            <FormField label="Breedte (m)" htmlFor="t-width">
              <input id="t-width" inputMode="decimal" value={form.widthMeters ?? ''} onChange={(e) => set('widthMeters', numberOrNull(e.target.value))} disabled={saving} />
            </FormField>
            <FormField label="Hoogte (m)" htmlFor="t-height">
              <input id="t-height" inputMode="decimal" value={form.heightMeters ?? ''} onChange={(e) => set('heightMeters', numberOrNull(e.target.value))} disabled={saving} />
            </FormField>
            <FormField
              label="Volume (m³)"
              htmlFor="t-volume"
              hint={form.volumeIsManual ? 'Handmatige waarde; vink uit om opnieuw te berekenen.' : 'Automatisch berekend uit lengte × breedte × hoogte.'}
            >
              <input
                id="t-volume"
                inputMode="decimal"
                value={
                  form.volumeIsManual
                    ? (form.volumeM3 ?? '')
                    : (computeVolumeM3(form.lengthMeters, form.widthMeters, form.heightMeters) ?? form.volumeM3 ?? '')
                }
                onChange={(e) => set('volumeM3', numberOrNull(e.target.value))}
                disabled={saving || !form.volumeIsManual}
              />
              <label className="trailer-checkbox">
                <input
                  type="checkbox"
                  checked={form.volumeIsManual}
                  onChange={(e) => {
                    set('volumeIsManual', e.target.checked)
                    if (e.target.checked) {
                      set('volumeM3', form.volumeM3 ?? computeVolumeM3(form.lengthMeters, form.widthMeters, form.heightMeters))
                    }
                  }}
                  disabled={saving}
                />
                <span>Handmatig invullen</span>
              </label>
            </FormField>
            <div className="vehicle-form-equipment form-span-all">
              <label className="trailer-checkbox">
                <input type="checkbox" checked={form.hasRefrigeration} onChange={(e) => set('hasRefrigeration', e.target.checked)} disabled={saving} />
                <span>Koeling</span>
              </label>
              <label className="trailer-checkbox">
                <input type="checkbox" checked={form.adrSuitable} onChange={(e) => set('adrSuitable', e.target.checked)} disabled={saving} />
                <span>ADR-geschikt</span>
              </label>
            </div>
          </FormSection>

          <FormSection title="Notities" columns={1}>
            <FormField label="Interne notities" htmlFor="t-notes" className="form-span-all">
              <textarea id="t-notes" rows={3} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={saving} maxLength={2000} />
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
