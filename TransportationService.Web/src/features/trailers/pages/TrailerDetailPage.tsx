import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { ApiError } from '../../../api/apiClient'
import { FleetDocumentsPanel } from '../../fleet-documents/components/FleetDocumentsPanel'
import { deleteTrailer, getTrailer, updateTrailer } from '../api/trailersApi'
import {
  TRAILER_OWNERSHIP_LABELS,
  TRAILER_STATUS_LABELS,
  type TrailerDetail,
  type TrailerInput,
  type TrailerOperationalStatus,
} from '../types'
import './trailer-form.css'

const STATUS_TONE: Record<TrailerOperationalStatus, BadgeTone> = {
  Active: 'success',
  InMaintenance: 'warning',
  OutOfService: 'danger',
  Decommissioned: 'neutral',
}

export function TrailerDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [trailer, setTrailer] = useState<TrailerDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<TrailerInput | null>(null)
  const [saving, setSaving] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

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
      hasRefrigeration: trailer.hasRefrigeration,
      adrSuitable: trailer.adrSuitable,
      ownershipType: trailer.ownershipType,
      operationalStatus: trailer.operationalStatus,
      isActive: trailer.isActive,
      notes: trailer.notes,
    })
    setEditing(true)
  }

  function set<K extends keyof TrailerInput>(key: K, value: TrailerInput[K]) {
    setForm((f) => (f ? { ...f, [key]: value } : f))
  }

  async function saveEdit() {
    if (!id || !form) return
    setSaving(true)
    try {
      const updated = await updateTrailer(id, form)
      setTrailer(updated)
      setEditing(false)
      showSuccess('Oplegger bijgewerkt.')
    } catch (err) {
      showError(err instanceof ApiError && err.status === 409 ? 'Er bestaat al een oplegger met dit kenteken.' : 'Wijzigingen konden niet worden opgeslagen.')
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
      <PageHeader
        title={`${trailer.brand ?? ''} ${trailer.model ?? ''}`.trim() || trailer.internalNumber}
        subtitle={`${trailer.internalNumber} · ${trailer.licensePlate}`}
        action={
          <div className="trailer-detail-actions">
            {canEdit && !editing && <Button variant="secondary" onClick={startEdit}>Bewerken</Button>}
            {hasPermission('trailers.delete') && <Button variant="danger" onClick={() => setConfirmDelete(true)}>Verwijderen</Button>}
          </div>
        }
      />

      <div className="trailer-detail-badges">
        <Badge tone={STATUS_TONE[trailer.operationalStatus]}>{TRAILER_STATUS_LABELS[trailer.operationalStatus]}</Badge>
        {trailer.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>}
        {trailer.adrSuitable && <Badge tone="warning">ADR</Badge>}
        {trailer.hasRefrigeration && <Badge tone="info">Koeling</Badge>}
      </div>

      {!editing ? (
        <div className="trailer-detail-grid">
          <FormField label="Categorie">
            <span>{trailer.categoryName ?? '—'}</span>
          </FormField>
          <FormField label="Eigendomsvorm">
            <span>{TRAILER_OWNERSHIP_LABELS[trailer.ownershipType]}</span>
          </FormField>
          <FormField label="VIN">
            <span>{trailer.vin ?? '—'}</span>
          </FormField>
          <FormField label="Bouwjaar">
            <span>{trailer.year ?? '—'}</span>
          </FormField>
          <FormField label="Laadvermogen">
            <span>{trailer.capacityKg != null ? `${trailer.capacityKg.toLocaleString('nl-BE')} kg` : '—'}</span>
          </FormField>
          <FormField label="Volume">
            <span>{trailer.volumeM3 != null ? `${trailer.volumeM3} m³` : '—'}</span>
          </FormField>
          <FormField label="Afmetingen (L × B × H)">
            <span>
              {trailer.lengthMeters != null || trailer.widthMeters != null || trailer.heightMeters != null
                ? `${trailer.lengthMeters ?? '?'} × ${trailer.widthMeters ?? '?'} × ${trailer.heightMeters ?? '?'} m`
                : '—'}
            </span>
          </FormField>
          <FormField label="Eerste registratie">
            <span>{trailer.firstRegistrationDate ?? '—'}</span>
          </FormField>
          <FormField label="Notities" className="trailer-detail-full">
            <span>{trailer.notes ?? '—'}</span>
          </FormField>
        </div>
      ) : (
        form && (
          <form
            className="trailer-form"
            onSubmit={(e) => {
              e.preventDefault()
              void saveEdit()
            }}
          >
            <section className="trailer-form-card">
              <h2>Basisgegevens</h2>
              <div className="trailer-form-grid">
                <FormField label="Kenteken" htmlFor="e-plate" required>
                  <input id="e-plate" value={form.licensePlate} onChange={(e) => set('licensePlate', e.target.value)} disabled={saving} />
                </FormField>
                <FormField label="Status" htmlFor="e-status">
                  <select id="e-status" value={form.operationalStatus} onChange={(e) => set('operationalStatus', e.target.value as TrailerOperationalStatus)} disabled={saving}>
                    {Object.entries(TRAILER_STATUS_LABELS).map(([value, label]) => (
                      <option key={value} value={value}>
                        {label}
                      </option>
                    ))}
                  </select>
                </FormField>
                <FormField label="Merk" htmlFor="e-brand">
                  <input id="e-brand" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value || null)} disabled={saving} />
                </FormField>
                <FormField label="Model" htmlFor="e-model">
                  <input id="e-model" value={form.model ?? ''} onChange={(e) => set('model', e.target.value || null)} disabled={saving} />
                </FormField>
                <FormField label="Actief" htmlFor="e-active">
                  <label className="trailer-checkbox">
                    <input id="e-active" type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} disabled={saving} />
                    <span>Oplegger is actief</span>
                  </label>
                </FormField>
              </div>
              <div className="trailer-form-checkboxes">
                <label className="trailer-checkbox">
                  <input type="checkbox" checked={form.hasRefrigeration} onChange={(e) => set('hasRefrigeration', e.target.checked)} disabled={saving} />
                  <span>Koeling</span>
                </label>
                <label className="trailer-checkbox">
                  <input type="checkbox" checked={form.adrSuitable} onChange={(e) => set('adrSuitable', e.target.checked)} disabled={saving} />
                  <span>ADR-geschikt</span>
                </label>
              </div>
            </section>

            <div className="trailer-form-actions">
              <Button type="button" variant="secondary" onClick={() => setEditing(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </div>
          </form>
        )
      )}

      {!editing && id && <FleetDocumentsPanel ownerType="trailer" ownerId={id} />}

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
    </div>
  )
}
