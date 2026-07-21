import { useEffect, useState } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { useAuth } from '../../auth/authContextValue'
import { AssignmentSlot } from '../../fleet-assignment/AssignmentSlot'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { Link } from 'react-router-dom'
import { getTrailerOptions } from '../../trailers/api/trailersApi'
import { getVehicleOptions } from '../../vehicles/api/vehiclesApi'
import type { TrailerOption } from '../../trailers/types'
import { deleteDriver, getDriver, setDriverBlocked, setDriverVehicle, updateDriver } from '../api/driversApi'
import { AVAILABILITY_LABELS, type DriverAvailabilityStatus, type DriverDetail } from '../types'
import '../pages/driver-detail.css'

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

interface DriverProfilePanelProps {
  driverId: string
  /** Called after edits that may affect the surrounding employee view. */
  onChanged?: () => void
  /** Called after the driver profile is deleted (host navigates away). */
  onDeleted?: () => void
}

/**
 * Reusable driver profile: readiness, inline edit, block/unblock, assignment slots, fixed
 * trailer, qualifications and delete. Consumes driversApi; the driver↔vehicle relationship
 * stays on the vehicle side (single source of truth). Rendered inside the employee detail
 * "chauffeursprofiel" tab — the standalone driver page redirects here.
 */
export function DriverProfilePanel({ driverId, onChanged, onDeleted }: DriverProfilePanelProps) {
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const { options: categories } = useLookupOptions('/api/driver-categories')

  const [driver, setDriver] = useState<DriverDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<Partial<DriverDetail>>({})
  const [saving, setSaving] = useState(false)
  const [trailerOptions, setTrailerOptions] = useState<TrailerOption[]>([])

  const [blocking, setBlocking] = useState(false)
  const [blockReason, setBlockReason] = useState('')
  const [confirmDelete, setConfirmDelete] = useState(false)

  useEffect(() => {
    let mounted = true
    getDriver(driverId)
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
  }, [driverId])

  const loading = driver === null && loadError === null

  function startEdit() {
    if (!driver) return
    setForm(driver)
    setEditing(true)
    if (trailerOptions.length === 0) {
      getTrailerOptions()
        .then(setTrailerOptions)
        .catch(() => {})
    }
  }

  function reloadDriver() {
    getDriver(driverId)
      .then(setDriver)
      .catch(() => showError('Chauffeur kon niet opnieuw worden geladen.'))
  }

  async function saveEdit() {
    if (!driver) return
    setSaving(true)
    try {
      const updated = await updateDriver(driverId, {
        driverCategoryId: form.categoryId ?? null,
        availabilityStatus: (form.availabilityStatus as DriverAvailabilityStatus) ?? driver.availabilityStatus,
        isActive: form.isActive ?? driver.isActive,
        fixedTrailerId: form.fixedTrailerId !== undefined ? form.fixedTrailerId : driver.fixedTrailerId,
        notes: form.notes ?? driver.notes ?? null,
      })
      setDriver(updated)
      setEditing(false)
      showSuccess('Chauffeur bijgewerkt.')
      onChanged?.()
    } catch {
      showError('Wijzigingen konden niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function toggleBlock() {
    if (!driver) return
    setBlocking(true)
    try {
      const updated = await setDriverBlocked(driverId, !driver.isBlocked, driver.isBlocked ? null : blockReason.trim() || null)
      setDriver(updated)
      setBlockReason('')
      showSuccess(updated.isBlocked ? 'Chauffeur geblokkeerd.' : 'Blokkering opgeheven.')
      onChanged?.()
    } catch {
      showError('De blokkeerstatus kon niet worden gewijzigd.')
    } finally {
      setBlocking(false)
    }
  }

  async function handleDelete() {
    try {
      await deleteDriver(driverId)
      showSuccess('Chauffeur verwijderd.')
      onDeleted?.()
    } catch {
      showError('Chauffeur kon niet worden verwijderd.')
      setConfirmDelete(false)
    }
  }

  if (loading) return <p className="placeholder-text">Chauffeur laden…</p>
  if (loadError || !driver) return <p className="placeholder-text">{loadError ?? 'Niet gevonden.'}</p>

  const canEdit = hasPermission('drivers.edit')

  return (
    <div className="driver-profile-panel">
      <div className="driver-panel-header">
        <div className="driver-panel-title">
          <span className="driver-panel-number">
            <code>{driver.driverNumber}</code>
          </span>
        </div>
        <div className="driver-actions">
          {canEdit && !editing && (
            <Button variant="secondary" onClick={startEdit}>
              Bewerken
            </Button>
          )}
          {hasPermission('drivers.block') && (
            <Button variant={driver.isBlocked ? 'secondary' : 'danger'} onClick={toggleBlock} disabled={blocking}>
              {driver.isBlocked ? 'Deblokkeren' : 'Blokkeren'}
            </Button>
          )}
          {hasPermission('drivers.delete') && (
            <Button variant="danger" onClick={() => setConfirmDelete(true)}>
              Verwijderen
            </Button>
          )}
        </div>
      </div>

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

        <FormField label="Vaste oplegger" hint="Langetermijnvoorkeur; geen operationele koppeling.">
          {editing ? (
            <SearchableSelect
              value={form.fixedTrailerId ?? null}
              onChange={(v) => setForm((f) => ({ ...f, fixedTrailerId: v }))}
              options={trailerOptions.map((t) => ({
                value: t.id,
                label: `${t.internalNumber} · ${t.licensePlate}`,
                keywords: t.licensePlate,
              }))}
              placeholder="— Geen —"
            />
          ) : (
            <span>{driver.fixedTrailerLabel ?? '—'}</span>
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

      <section className="driver-vehicles">
        <h2>Voertuigen</h2>
        <p className="assignment-slots-note">
          <strong>Vast voertuig</strong> is een langetermijnvoorkeur; <strong>actueel voertuig</strong> is de tijdelijke
          operationele toewijzing. De rittoewijzing voor een specifieke dag gebeurt in de planning. Wijzigingen hier
          worden ook op de voertuigpagina zichtbaar — het is dezelfde koppeling.
        </p>
        <div className="assignment-slots">
          <AssignmentSlot
            title="Vast voertuig"
            description="Voorkeursvoertuig op lange termijn."
            assigned={driver.fixedVehicle ? { label: driver.fixedVehicle.label, linkTo: `/vehicles/${driver.fixedVehicle.id}` } : null}
            canEdit={canEdit}
            pickerLabel="Voertuig"
            loadOptions={async () =>
              (await getVehicleOptions()).map((v) => ({
                value: v.id,
                label: `${v.internalNumber} · ${v.licensePlate}`,
                description: [v.brand, v.model].filter(Boolean).join(' ') || undefined,
                keywords: v.licensePlate,
              }))
            }
            assign={async (vehicleId, replaceExisting) => {
              await setDriverVehicle(driver.id, 'fixed-vehicle', vehicleId, replaceExisting)
            }}
            onChanged={reloadDriver}
          />
          <AssignmentSlot
            title="Actueel voertuig"
            description="Tijdelijke operationele toewijzing (bv. tijdens vervanging)."
            assigned={driver.currentVehicle ? { label: driver.currentVehicle.label, linkTo: `/vehicles/${driver.currentVehicle.id}` } : null}
            canEdit={canEdit}
            pickerLabel="Voertuig"
            loadOptions={async () =>
              (await getVehicleOptions()).map((v) => ({
                value: v.id,
                label: `${v.internalNumber} · ${v.licensePlate}`,
                description: [v.brand, v.model].filter(Boolean).join(' ') || undefined,
                keywords: v.licensePlate,
              }))
            }
            assign={async (vehicleId, replaceExisting) => {
              await setDriverVehicle(driver.id, 'current-vehicle', vehicleId, replaceExisting)
            }}
            onChanged={reloadDriver}
          />
        </div>
      </section>

      <section className="driver-qualifications">
        <h2>Kwalificaties</h2>
        <p className="assignment-slots-note">
          Rijbewijs- en vakbekwaamheidsgegevens worden beheerd via het tabblad{' '}
          <Link to={`?tab=kwalificaties`}>Kwalificaties</Link>.
        </p>
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
          message={`Weet je zeker dat je chauffeur ${driver.driverNumber} wilt verwijderen? Het personeelsdossier blijft behouden.`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}
