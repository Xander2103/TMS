import { useEffect, useState } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
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
/** i18n-keys (driversAdmin.readiness.*) — render via t(READINESS_LABEL[s]). */
const READINESS_LABEL: Record<DriverDetail['readiness']['status'], string> = {
  Ready: 'driversAdmin.readiness.Ready',
  Warning: 'driversAdmin.readiness.Warning',
  NotReady: 'driversAdmin.readiness.NotReady',
  Blocked: 'driversAdmin.readiness.Blocked',
}
const QUAL_TONE: Record<string, BadgeTone> = {
  Valid: 'success',
  ExpiringSoon: 'warning',
  Expired: 'danger',
  Suspended: 'danger',
  Rejected: 'danger',
  Pending: 'neutral',
}
/** i18n-keys (driversAdmin.qualStatus.*); unknown backend statuses render as their raw code. */
const QUAL_STATUS_LABELS: Record<string, string> = {
  Valid: 'driversAdmin.qualStatus.Valid',
  ExpiringSoon: 'driversAdmin.qualStatus.ExpiringSoon',
  Expired: 'driversAdmin.qualStatus.Expired',
  Suspended: 'driversAdmin.qualStatus.Suspended',
  Rejected: 'driversAdmin.qualStatus.Rejected',
  Pending: 'driversAdmin.qualStatus.Pending',
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
 * stays on the vehicle side (single source of truth). Rendered on the employee detail page's
 * "Chauffeursgegevens" block (read-only) / section (edit) — the standalone driver page redirects here.
 */
export function DriverProfilePanel({ driverId, onChanged, onDeleted }: DriverProfilePanelProps) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const { options: categories } = useLookupOptions('/api/driver-categories')

  const [driver, setDriver] = useState<DriverDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<Partial<DriverDetail>>({})
  /** Ordered selection: the first ticked category is the primary (mirrors create). */
  const [formCategoryIds, setFormCategoryIds] = useState<string[]>([])
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
        if (mounted) setLoadError(t('driversAdmin.panel.loadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [driverId, t])

  const loading = driver === null && loadError === null

  function startEdit() {
    if (!driver) return
    setForm(driver)
    setFormCategoryIds(driver.categoryIds ?? [])
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
      .catch(() => showError(t('driversAdmin.panel.reloadFailed')))
  }

  async function saveEdit() {
    if (!driver) return
    setSaving(true)
    try {
      const updated = await updateDriver(driverId, {
        // The ordered multi-select is authoritative; the first entry is the primary.
        driverCategoryId: formCategoryIds[0] ?? null,
        driverCategoryIds: formCategoryIds,
        availabilityStatus: (form.availabilityStatus as DriverAvailabilityStatus) ?? driver.availabilityStatus,
        isActive: form.isActive ?? driver.isActive,
        fixedTrailerId: form.fixedTrailerId !== undefined ? form.fixedTrailerId : driver.fixedTrailerId,
        notes: form.notes ?? driver.notes ?? null,
      })
      setDriver(updated)
      setEditing(false)
      showSuccess(t('driversAdmin.panel.updated'))
      onChanged?.()
    } catch {
      showError(t('fleet.common.saveChangesFailed'))
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
      showSuccess(updated.isBlocked ? t('driversAdmin.panel.blocked') : t('driversAdmin.panel.unblocked'))
      onChanged?.()
    } catch {
      showError(t('driversAdmin.panel.blockFailed'))
    } finally {
      setBlocking(false)
    }
  }

  async function handleDelete() {
    try {
      await deleteDriver(driverId)
      showSuccess(t('driversAdmin.panel.deleted'))
      onDeleted?.()
    } catch {
      showError(t('driversAdmin.panel.deleteFailed'))
      setConfirmDelete(false)
    }
  }

  if (loading) return <p className="placeholder-text">{t('driversAdmin.panel.loading')}</p>
  if (loadError || !driver) return <p className="placeholder-text">{loadError ?? t('fleet.common.notFound')}</p>

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
              {t('ui.actions.edit')}
            </Button>
          )}
          {hasPermission('drivers.block') && (
            <Button variant={driver.isBlocked ? 'secondary' : 'danger'} onClick={toggleBlock} disabled={blocking}>
              {driver.isBlocked ? t('driversAdmin.panel.unblock') : t('driversAdmin.panel.block')}
            </Button>
          )}
          {hasPermission('drivers.delete') && (
            <Button variant="danger" onClick={() => setConfirmDelete(true)}>
              {t('ui.actions.delete')}
            </Button>
          )}
        </div>
      </div>

      <section className="driver-readiness">
        <div className="driver-readiness-header">
          <span>{t('driversAdmin.panel.readinessTitle')}</span>
          <Badge tone={READINESS_TONE[driver.readiness.status]}>{t(READINESS_LABEL[driver.readiness.status])}</Badge>
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
          <p className="driver-reasons-ok">{t('driversAdmin.panel.readinessOk')}</p>
        )}
      </section>

      {!driver.isBlocked && hasPermission('drivers.block') && (
        <div className="driver-block-reason">
          <input
            type="text"
            placeholder={t('driversAdmin.panel.blockReasonPlaceholder')}
            value={blockReason}
            onChange={(e) => setBlockReason(e.target.value)}
          />
        </div>
      )}

      <section className="driver-grid">
        <FormField label={t('driversAdmin.panel.categories')} hint={editing ? t('driversAdmin.panel.categoriesHint') : undefined}>
          {editing ? (
            <div className="driver-category-options">
              {categories.length === 0 && <span className="placeholder-text">{t('driversAdmin.panel.noCategories')}</span>}
              {categories.map((c) => {
                const position = formCategoryIds.indexOf(c.id)
                return (
                  <label key={c.id} className="driver-checkbox">
                    <input
                      type="checkbox"
                      checked={position >= 0}
                      onChange={(e) =>
                        setFormCategoryIds((ids) =>
                          e.target.checked ? [...ids, c.id] : ids.filter((id) => id !== c.id),
                        )
                      }
                    />
                    <span>
                      {c.name}
                      {position === 0 ? ` ${t('driversAdmin.panel.primary')}` : ''}
                    </span>
                  </label>
                )
              })}
            </div>
          ) : (
            <span>
              {driver.categoryNames && driver.categoryNames.length > 0 ? driver.categoryNames.join(', ') : '—'}
            </span>
          )}
        </FormField>

        <FormField label={t('driversAdmin.fields.availability')}>
          {editing ? (
            <select
              value={form.availabilityStatus ?? driver.availabilityStatus}
              onChange={(e) => setForm((f) => ({ ...f, availabilityStatus: e.target.value as DriverAvailabilityStatus }))}
            >
              {(Object.keys(AVAILABILITY_LABELS) as DriverAvailabilityStatus[]).map((s) => (
                <option key={s} value={s}>
                  {t(AVAILABILITY_LABELS[s])}
                </option>
              ))}
            </select>
          ) : (
            <span>{t(AVAILABILITY_LABELS[driver.availabilityStatus])}</span>
          )}
        </FormField>

        <FormField label={t('driversAdmin.panel.active')}>
          {editing ? (
            <label className="driver-checkbox">
              <input type="checkbox" checked={form.isActive ?? driver.isActive} onChange={(e) => setForm((f) => ({ ...f, isActive: e.target.checked }))} />
              <span>{t('driversAdmin.panel.activeCheckbox')}</span>
            </label>
          ) : (
            <span>{driver.isActive ? t('fleet.common.yes') : t('fleet.common.no')}</span>
          )}
        </FormField>

        <FormField label={t('driversAdmin.panel.fixedTrailer')} hint={t('driversAdmin.panel.fixedTrailerHint')}>
          {editing ? (
            <SearchableSelect
              value={form.fixedTrailerId ?? null}
              onChange={(v) => setForm((f) => ({ ...f, fixedTrailerId: v }))}
              options={trailerOptions.map((tr) => ({
                value: tr.id,
                label: `${tr.internalNumber} · ${tr.licensePlate}`,
                keywords: tr.licensePlate,
              }))}
              placeholder={t('fleet.form.none')}
            />
          ) : (
            <span>{driver.fixedTrailerLabel ?? '—'}</span>
          )}
        </FormField>

        <FormField label={t('driversAdmin.fields.notes')} className="driver-grid-full">
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
            {t('ui.actions.cancel')}
          </Button>
          <Button onClick={saveEdit} disabled={saving}>
            {saving ? t('fleet.common.saving') : t('ui.actions.save')}
          </Button>
        </div>
      )}

      <section className="driver-vehicles">
        <h2>{t('navigation.menu.vehicles')}</h2>
        <p className="assignment-slots-note">
          <strong>{t('driversAdmin.panel.noteFixedLead')}</strong> {t('driversAdmin.panel.noteFixedText')}{' '}
          <strong>{t('driversAdmin.panel.noteCurrentLead')}</strong> {t('driversAdmin.panel.noteCurrentText')}
        </p>
        <div className="assignment-slots">
          <AssignmentSlot
            title={t('driversAdmin.panel.fixedVehicle')}
            description={t('driversAdmin.panel.fixedVehicleDesc')}
            assigned={driver.fixedVehicle ? { label: driver.fixedVehicle.label, linkTo: `/vehicles/${driver.fixedVehicle.id}` } : null}
            canEdit={canEdit}
            pickerLabel={t('driversAdmin.panel.pickerVehicle')}
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
            title={t('driversAdmin.panel.currentVehicle')}
            description={t('driversAdmin.panel.currentVehicleDesc')}
            assigned={driver.currentVehicle ? { label: driver.currentVehicle.label, linkTo: `/vehicles/${driver.currentVehicle.id}` } : null}
            canEdit={canEdit}
            pickerLabel={t('driversAdmin.panel.pickerVehicle')}
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
        <h2>{t('navigation.menu.qualifications')}</h2>
        <p className="assignment-slots-note">
          {t('driversAdmin.panel.qualNote')}{' '}
          <Link to={`?tab=kwalificaties`}>{t('navigation.menu.qualifications')}</Link>.
        </p>
        {driver.qualifications.length === 0 ? (
          <p className="placeholder-text">{t('driversAdmin.panel.qualEmpty')}</p>
        ) : (
          <table className="driver-qual-table">
            <thead>
              <tr>
                <th>{t('driversAdmin.panel.colQualification')}</th>
                <th>{t('driversAdmin.panel.colStatus')}</th>
                <th>{t('driversAdmin.panel.colExpiry')}</th>
              </tr>
            </thead>
            <tbody>
              {driver.qualifications.map((q) => (
                <tr key={q.typeCode}>
                  <td>{q.typeName}</td>
                  <td>
                    <Badge tone={QUAL_TONE[q.status] ?? 'neutral'}>
                      {QUAL_STATUS_LABELS[q.status] ? t(QUAL_STATUS_LABELS[q.status]) : q.status}
                    </Badge>
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
          title={t('driversAdmin.panel.deleteTitle')}
          message={t('driversAdmin.panel.deleteMessage', { number: driver.driverNumber })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}
