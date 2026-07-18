import { useEffect, useState, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { searchDrivers } from '../../drivers/api/driversApi'
import type { DriverListItem } from '../../drivers/types'
import {
  createDamageReport,
  deleteDamageReport,
  listDamageReports,
  updateDamageReport,
  type DamageOwnerType,
} from '../api/damageApi'
import {
  DAMAGE_SEVERITY_LABELS,
  DAMAGE_STATUS_LABELS,
  type DamageReport,
  type DamageSeverity,
  type DamageStatus,
  type UpdateDamageInput,
} from '../types'
import './damage.css'

const SEVERITY_TONE: Record<DamageSeverity, BadgeTone> = {
  Minor: 'info',
  Moderate: 'warning',
  Severe: 'danger',
  TotalLoss: 'danger',
}

const STATUS_TONE: Record<DamageStatus, BadgeTone> = {
  Reported: 'warning',
  UnderAssessment: 'info',
  InRepair: 'info',
  Repaired: 'success',
  Closed: 'neutral',
}

interface DamageForm {
  driverId: string
  incidentDate: string
  location: string
  description: string
  severity: DamageSeverity
  status: DamageStatus
  insuranceReference: string
  repairCost: string
  downtimeDays: string
  notes: string
}

const EMPTY_FORM: DamageForm = {
  driverId: '',
  incidentDate: '',
  location: '',
  description: '',
  severity: 'Minor',
  status: 'Reported',
  insuranceReference: '',
  repairCost: '',
  downtimeDays: '',
  notes: '',
}

function euro(value: number | null): string {
  if (value === null) return '—'
  return value.toLocaleString('nl-BE', { style: 'currency', currency: 'EUR' })
}

interface DamagePanelProps {
  ownerType: DamageOwnerType
  ownerId: string
}

/** Damage reports section for a vehicle or trailer detail page: report, track status, delete. */
export function DamagePanel({ ownerType, ownerId }: DamagePanelProps) {
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [reports, setReports] = useState<DamageReport[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [drivers, setDrivers] = useState<DriverListItem[]>([])

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<DamageReport | null>(null)
  const [form, setForm] = useState<DamageForm>(EMPTY_FORM)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const [deleteTarget, setDeleteTarget] = useState<DamageReport | null>(null)

  useEffect(() => {
    let mounted = true
    listDamageReports(ownerType, ownerId)
      .then((data) => {
        if (!mounted) return
        setReports(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Schademeldingen konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [ownerType, ownerId, reloadToken])

  // Active drivers for the "betrokken chauffeur" selector; failure keeps the field usable as "Geen".
  useEffect(() => {
    let mounted = true
    searchDrivers({ isActive: true, page: 1, pageSize: 200 })
      .then((page) => {
        if (mounted) setDrivers(page.items)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  function set<K extends keyof DamageForm>(key: K, value: DamageForm[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function openCreate() {
    setEditing(null)
    setForm({ ...EMPTY_FORM, incidentDate: new Date().toISOString().slice(0, 10) })
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(report: DamageReport) {
    setEditing(report)
    setForm({
      driverId: report.driverId ?? '',
      incidentDate: report.incidentDate,
      location: report.location ?? '',
      description: report.description,
      severity: report.severity,
      status: report.status,
      insuranceReference: report.insuranceReference ?? '',
      repairCost: report.repairCost === null ? '' : String(report.repairCost),
      downtimeDays: report.downtimeDays === null ? '' : String(report.downtimeDays),
      notes: report.notes ?? '',
    })
    setFormError(null)
    setEditorOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.incidentDate) {
      setFormError('De datum van het incident is verplicht.')
      return
    }
    if (!form.description.trim()) {
      setFormError('Een omschrijving van de schade is verplicht.')
      return
    }
    const base = {
      driverId: form.driverId || null,
      incidentDate: form.incidentDate,
      location: form.location.trim() || null,
      description: form.description.trim(),
      severity: form.severity,
      insuranceReference: form.insuranceReference.trim() || null,
      notes: form.notes.trim() || null,
    }
    setSaving(true)
    try {
      if (editing) {
        const input: UpdateDamageInput = {
          ...base,
          status: form.status,
          repairCost: form.repairCost === '' ? null : Number(form.repairCost),
          downtimeDays: form.downtimeDays === '' ? null : Number(form.downtimeDays),
        }
        await updateDamageReport(editing.id, input)
        showSuccess('Schademelding bijgewerkt.')
      } else {
        await createDamageReport(ownerType, ownerId, base)
        showSuccess('Schade gemeld.')
      }
      setEditorOpen(false)
      setReloadToken((t) => t + 1)
    } catch {
      setFormError('De schademelding kon niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteDamageReport(deleteTarget.id)
      showSuccess('Schademelding verwijderd.')
      setDeleteTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('De schademelding kon niet worden verwijderd.')
      setDeleteTarget(null)
    }
  }

  return (
    <section className="dmg">
      <div className="dmg-header">
        <h2>Schade</h2>
        {hasPermission('damage_reports.create') && (
          <Button variant="secondary" onClick={openCreate}>
            Schade melden
          </Button>
        )}
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && reports === null && <p className="placeholder-text">Schademeldingen laden…</p>}
      {!loadError && reports !== null && reports.length === 0 && (
        <p className="placeholder-text">Geen schademeldingen geregistreerd.</p>
      )}

      {!loadError && reports !== null && reports.length > 0 && (
        <table className="dmg-table">
          <thead>
            <tr>
              <th>Datum</th>
              <th>Omschrijving</th>
              <th>Ernst</th>
              <th>Status</th>
              <th>Chauffeur</th>
              <th>Herstelkost</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {reports.map((report) => (
              <tr key={report.id}>
                <td>{report.incidentDate}</td>
                <td className="dmg-description" title={report.location ?? undefined}>
                  {report.description}
                </td>
                <td>
                  <Badge tone={SEVERITY_TONE[report.severity]}>{DAMAGE_SEVERITY_LABELS[report.severity]}</Badge>
                </td>
                <td>
                  <Badge tone={STATUS_TONE[report.status]}>{DAMAGE_STATUS_LABELS[report.status]}</Badge>
                </td>
                <td>{report.driverName ?? '—'}</td>
                <td>{euro(report.repairCost)}</td>
                <td className="dmg-actions">
                  {hasPermission('damage_reports.edit') && (
                    <button type="button" className="dmg-link" onClick={() => openEdit(report)}>
                      Bewerken
                    </button>
                  )}
                  {hasPermission('damage_reports.delete') && (
                    <button type="button" className="dmg-link dmg-link-danger" onClick={() => setDeleteTarget(report)}>
                      Verwijderen
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {editorOpen && (
        <Modal
          title={editing ? 'Schademelding bewerken' : 'Schade melden'}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="dmg-form" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="dmg-form" className="dmg-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="dmg-form-error" role="alert">
                {formError}
              </div>
            )}
            <div className="dmg-form-row">
              <FormField label="Datum incident" htmlFor="dm-date" required>
                <input
                  id="dm-date"
                  type="date"
                  value={form.incidentDate}
                  onChange={(e) => set('incidentDate', e.target.value)}
                  disabled={saving}
                />
              </FormField>
              <FormField label="Ernst" htmlFor="dm-severity" required>
                <select
                  id="dm-severity"
                  value={form.severity}
                  onChange={(e) => set('severity', e.target.value as DamageSeverity)}
                  disabled={saving}
                >
                  {(Object.keys(DAMAGE_SEVERITY_LABELS) as DamageSeverity[]).map((severity) => (
                    <option key={severity} value={severity}>
                      {DAMAGE_SEVERITY_LABELS[severity]}
                    </option>
                  ))}
                </select>
              </FormField>
            </div>
            <FormField label="Omschrijving" htmlFor="dm-description" required>
              <textarea
                id="dm-description"
                rows={3}
                value={form.description}
                onChange={(e) => set('description', e.target.value)}
                disabled={saving}
                maxLength={2000}
              />
            </FormField>
            <div className="dmg-form-row">
              <FormField label="Locatie" htmlFor="dm-location">
                <input
                  id="dm-location"
                  value={form.location}
                  onChange={(e) => set('location', e.target.value)}
                  disabled={saving}
                  maxLength={200}
                  placeholder="bv. E313 Antwerpen-Oost"
                />
              </FormField>
              <FormField label="Betrokken chauffeur" htmlFor="dm-driver">
                <select
                  id="dm-driver"
                  value={form.driverId}
                  onChange={(e) => set('driverId', e.target.value)}
                  disabled={saving}
                >
                  <option value="">Geen</option>
                  {drivers.map((driver) => (
                    <option key={driver.id} value={driver.id}>
                      {driver.fullName} ({driver.driverNumber})
                    </option>
                  ))}
                </select>
              </FormField>
            </div>
            {editing && (
              <div className="dmg-form-row dmg-form-row-3">
                <FormField label="Status" htmlFor="dm-status" required>
                  <select
                    id="dm-status"
                    value={form.status}
                    onChange={(e) => set('status', e.target.value as DamageStatus)}
                    disabled={saving}
                  >
                    {(Object.keys(DAMAGE_STATUS_LABELS) as DamageStatus[]).map((status) => (
                      <option key={status} value={status}>
                        {DAMAGE_STATUS_LABELS[status]}
                      </option>
                    ))}
                  </select>
                </FormField>
                <FormField label="Herstelkost (€)" htmlFor="dm-cost">
                  <input
                    id="dm-cost"
                    type="number"
                    min={0}
                    step="0.01"
                    value={form.repairCost}
                    onChange={(e) => set('repairCost', e.target.value)}
                    disabled={saving}
                  />
                </FormField>
                <FormField label="Stilstand (dagen)" htmlFor="dm-downtime">
                  <input
                    id="dm-downtime"
                    type="number"
                    min={0}
                    max={3650}
                    value={form.downtimeDays}
                    onChange={(e) => set('downtimeDays', e.target.value)}
                    disabled={saving}
                  />
                </FormField>
              </div>
            )}
            <FormField label="Verzekeringsreferentie" htmlFor="dm-insurance">
              <input
                id="dm-insurance"
                value={form.insuranceReference}
                onChange={(e) => set('insuranceReference', e.target.value)}
                disabled={saving}
                maxLength={100}
              />
            </FormField>
            <FormField label="Notities" htmlFor="dm-notes">
              <textarea
                id="dm-notes"
                rows={2}
                value={form.notes}
                onChange={(e) => set('notes', e.target.value)}
                disabled={saving}
              />
            </FormField>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Schademelding verwijderen"
          message={`Weet je zeker dat je de schademelding van ${deleteTarget.incidentDate} wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
