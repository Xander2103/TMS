import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { useAuth } from '../../auth/authContextValue'
import { deleteDriver, getDriver, setDriverBlocked, updateDriver } from '../api/driversApi'
import { AVAILABILITY_LABELS, type DriverAvailabilityStatus, type DriverDetail } from '../types'
import './driver-detail.css'

const READINESS_TONE: Record<DriverDetail['readiness']['status'], BadgeTone> = {
  Ready: 'success',
  Warning: 'warning',
  NotReady: 'danger',
  Blocked: 'danger',
}
const READINESS_LABEL: Record<DriverDetail['readiness']['status'], string> = {
  Ready: 'Inzetbaar',
  Warning: 'Let op',
  NotReady: 'Niet inzetbaar',
  Blocked: 'Geblokkeerd',
}
const QUAL_TONE: Record<string, BadgeTone> = {
  Valid: 'success',
  ExpiringSoon: 'warning',
  Expired: 'danger',
  Suspended: 'danger',
  Rejected: 'danger',
  Pending: 'neutral',
}

export function DriverDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const { options: categories } = useLookupOptions('/api/driver-categories')

  const [driver, setDriver] = useState<DriverDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<Partial<DriverDetail>>({})
  const [saving, setSaving] = useState(false)

  const [blocking, setBlocking] = useState(false)
  const [blockReason, setBlockReason] = useState('')
  const [confirmDelete, setConfirmDelete] = useState(false)

  useEffect(() => {
    if (!id) return
    let mounted = true
    getDriver(id)
      .then((result) => {
        if (!mounted) return
        setDriver(result)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Chauffeur kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

  // Derived so no setState runs synchronously inside the effect body.
  const loading = driver === null && loadError === null

  function startEdit() {
    if (!driver) return
    setForm(driver)
    setEditing(true)
  }

  async function saveEdit() {
    if (!id || !driver) return
    setSaving(true)
    try {
      const updated = await updateDriver(id, {
        driverCategoryId: form.categoryId ?? null,
        availabilityStatus: (form.availabilityStatus as DriverAvailabilityStatus) ?? driver.availabilityStatus,
        isActive: form.isActive ?? driver.isActive,
        fixedVehiclePreference: form.fixedVehiclePreference ?? driver.fixedVehiclePreference,
        defaultVehicleId: driver.defaultVehicleId,
        preferredVehicleId: driver.preferredVehicleId,
        defaultTrailerId: driver.defaultTrailerId,
        notes: form.notes ?? driver.notes ?? null,
      })
      setDriver(updated)
      setEditing(false)
      showSuccess('Chauffeur bijgewerkt.')
    } catch {
      showError('Wijzigingen konden niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function toggleBlock() {
    if (!id || !driver) return
    setBlocking(true)
    try {
      const updated = await setDriverBlocked(id, !driver.isBlocked, driver.isBlocked ? null : blockReason.trim() || null)
      setDriver(updated)
      setBlockReason('')
      showSuccess(updated.isBlocked ? 'Chauffeur geblokkeerd.' : 'Blokkering opgeheven.')
    } catch {
      showError('De blokkeerstatus kon niet worden gewijzigd.')
    } finally {
      setBlocking(false)
    }
  }

  async function handleDelete() {
    if (!id) return
    try {
      await deleteDriver(id)
      showSuccess('Chauffeur verwijderd.')
      navigate('/drivers')
    } catch {
      showError('Chauffeur kon niet worden verwijderd.')
      setConfirmDelete(false)
    }
  }

  if (loading) return <p className="placeholder-text">Chauffeur laden…</p>
  if (loadError || !driver) return <p className="placeholder-text">{loadError ?? 'Niet gevonden.'}</p>

  const canEdit = hasPermission('drivers.edit')

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Chauffeurs', to: '/drivers' }, { label: driver.driverNumber }]} />
      <PageHeader
        title={driver.fullName}
        subtitle={`Chauffeur ${driver.driverNumber} · Pers.nr ${driver.employeeNumber}`}
        action={
          <div className="driver-actions">
            {canEdit && !editing && <Button variant="secondary" onClick={startEdit}>Bewerken</Button>}
            {hasPermission('drivers.block') && (
              <Button variant={driver.isBlocked ? 'secondary' : 'danger'} onClick={toggleBlock} disabled={blocking}>
                {driver.isBlocked ? 'Deblokkeren' : 'Blokkeren'}
              </Button>
            )}
            {hasPermission('drivers.delete') && <Button variant="danger" onClick={() => setConfirmDelete(true)}>Verwijderen</Button>}
          </div>
        }
      />

      <section className="driver-readiness">
        <div className="driver-readiness-header">
          <span>Inzetbaarheid</span>
          <Badge tone={READINESS_TONE[driver.readiness.status]}>{READINESS_LABEL[driver.readiness.status]}</Badge>
        </div>
        {driver.readiness.blockingReasons.length > 0 && (
          <ul className="driver-reasons driver-reasons-blocking">
            {driver.readiness.blockingReasons.map((r) => (
              <li key={r}>{r}</li>
            ))}
          </ul>
        )}
        {driver.readiness.warnings.length > 0 && (
          <ul className="driver-reasons driver-reasons-warning">
            {driver.readiness.warnings.map((r) => (
              <li key={r}>{r}</li>
            ))}
          </ul>
        )}
        {driver.readiness.blockingReasons.length === 0 && driver.readiness.warnings.length === 0 && (
          <p className="driver-reasons-ok">Geen belemmeringen. Chauffeur is inzetbaar.</p>
        )}
      </section>

      {!driver.isBlocked && hasPermission('drivers.block') && (
        <div className="driver-block-reason">
          <input
            type="text"
            placeholder="Reden voor blokkade (optioneel)"
            value={blockReason}
            onChange={(e) => setBlockReason(e.target.value)}
          />
        </div>
      )}

      <section className="driver-grid">
        <FormField label="Categorie">
          {editing ? (
            <select value={form.categoryId ?? ''} onChange={(e) => setForm((f) => ({ ...f, categoryId: e.target.value || null }))}>
              <option value="">— Geen —</option>
              {categories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          ) : (
            <span>{driver.categoryName ?? '—'}</span>
          )}
        </FormField>

        <FormField label="Beschikbaarheid">
          {editing ? (
            <select
              value={form.availabilityStatus ?? driver.availabilityStatus}
              onChange={(e) => setForm((f) => ({ ...f, availabilityStatus: e.target.value as DriverAvailabilityStatus }))}
            >
              {(Object.keys(AVAILABILITY_LABELS) as DriverAvailabilityStatus[]).map((s) => (
                <option key={s} value={s}>
                  {AVAILABILITY_LABELS[s]}
                </option>
              ))}
            </select>
          ) : (
            <span>{AVAILABILITY_LABELS[driver.availabilityStatus]}</span>
          )}
        </FormField>

        <FormField label="Actief">
          {editing ? (
            <label className="driver-checkbox">
              <input type="checkbox" checked={form.isActive ?? driver.isActive} onChange={(e) => setForm((f) => ({ ...f, isActive: e.target.checked }))} />
              <span>Chauffeur is actief</span>
            </label>
          ) : (
            <span>{driver.isActive ? 'Ja' : 'Nee'}</span>
          )}
        </FormField>

        <FormField label="Vaste voertuigvoorkeur">
          {editing ? (
            <label className="driver-checkbox">
              <input
                type="checkbox"
                checked={form.fixedVehiclePreference ?? driver.fixedVehiclePreference}
                onChange={(e) => setForm((f) => ({ ...f, fixedVehiclePreference: e.target.checked }))}
              />
              <span>Altijd hetzelfde voertuig</span>
            </label>
          ) : (
            <span>{driver.fixedVehiclePreference ? 'Ja' : 'Nee'}</span>
          )}
        </FormField>

        <FormField label="Notities" className="driver-grid-full">
          {editing ? (
            <textarea rows={3} value={form.notes ?? driver.notes ?? ''} onChange={(e) => setForm((f) => ({ ...f, notes: e.target.value }))} />
          ) : (
            <span>{driver.notes ?? '—'}</span>
          )}
        </FormField>
      </section>

      {editing && (
        <div className="driver-form-actions">
          <Button variant="secondary" onClick={() => setEditing(false)} disabled={saving}>
            Annuleren
          </Button>
          <Button onClick={saveEdit} disabled={saving}>
            {saving ? 'Opslaan…' : 'Opslaan'}
          </Button>
        </div>
      )}

      <section className="driver-qualifications">
        <h2>Kwalificaties</h2>
        {driver.qualifications.length === 0 ? (
          <p className="placeholder-text">Nog geen kwalificaties geregistreerd.</p>
        ) : (
          <table className="driver-qual-table">
            <thead>
              <tr>
                <th>Kwalificatie</th>
                <th>Status</th>
                <th>Vervaldatum</th>
              </tr>
            </thead>
            <tbody>
              {driver.qualifications.map((q) => (
                <tr key={q.typeCode}>
                  <td>{q.typeName}</td>
                  <td>
                    <Badge tone={QUAL_TONE[q.status] ?? 'neutral'}>{q.status}</Badge>
                  </td>
                  <td>{q.expiryDate ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      {confirmDelete && (
        <ConfirmDialog
          title="Chauffeur verwijderen"
          message={`Weet je zeker dat je chauffeur ${driver.driverNumber} wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}
