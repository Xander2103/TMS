import { useEffect, useState, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { formatDate } from '../../../utils/dates'
import {
  completeMaintenance,
  createMaintenance,
  deleteMaintenance,
  listMaintenance,
  updateMaintenance,
  type MaintenanceOwnerType,
} from '../api/maintenanceApi'
import {
  MAINTENANCE_STATUS_LABELS,
  MAINTENANCE_TYPE_LABELS,
  MAINTENANCE_TYPES,
  maintenanceDisplayName,
  type CompleteMaintenanceInput,
  type MaintenanceInput,
  type MaintenanceRecord,
  type MaintenanceType,
} from '../types'
import './maintenance.css'

const STATUS_TONE: Record<MaintenanceRecord['status'], BadgeTone> = {
  Planned: 'info',
  InProgress: 'warning',
  Completed: 'success',
  Cancelled: 'neutral',
}

const EMPTY_FORM: MaintenanceInput = {
  maintenanceType: 'PeriodicService',
  customTypeName: null,
  description: '',
  scheduledDate: null,
  odometerTriggerKm: null,
  provider: null,
  intervalMonths: null,
  intervalKm: null,
  notes: null,
}

interface MaintenancePanelProps {
  ownerType: MaintenanceOwnerType
  ownerId: string
}

/** Maintenance section for a vehicle or trailer detail page: plan, edit, complete, delete. */
export function MaintenancePanel({ ownerType, ownerId }: MaintenancePanelProps) {
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [records, setRecords] = useState<MaintenanceRecord[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<MaintenanceRecord | null>(null)
  const [form, setForm] = useState<MaintenanceInput>(EMPTY_FORM)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const [completeTarget, setCompleteTarget] = useState<MaintenanceRecord | null>(null)
  const [completeForm, setCompleteForm] = useState<CompleteMaintenanceInput | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<MaintenanceRecord | null>(null)

  useEffect(() => {
    let mounted = true
    listMaintenance(ownerType, ownerId)
      .then((data) => {
        if (!mounted) return
        setRecords(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Onderhoud kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [ownerType, ownerId, reloadToken])

  function set<K extends keyof MaintenanceInput>(key: K, value: MaintenanceInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function openCreate() {
    setEditing(null)
    setForm(EMPTY_FORM)
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(record: MaintenanceRecord) {
    setEditing(record)
    setForm({
      maintenanceType: record.maintenanceType,
      customTypeName: record.customTypeName,
      description: record.description,
      scheduledDate: record.scheduledDate,
      odometerTriggerKm: record.odometerTriggerKm,
      provider: record.provider,
      intervalMonths: record.intervalMonths,
      intervalKm: record.intervalKm,
      notes: record.notes,
    })
    setFormError(null)
    setEditorOpen(true)
  }

  function openComplete(record: MaintenanceRecord) {
    setCompleteTarget(record)
    setCompleteForm({
      completedDate: new Date().toISOString().slice(0, 10),
      completedOdometerKm: null,
      workPerformed: null,
      provider: record.provider,
      cost: null,
      notes: null,
    })
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.description.trim()) {
      setFormError('Een omschrijving is verplicht.')
      return
    }
    if (form.maintenanceType === 'Other' && !form.customTypeName?.trim()) {
      setFormError('Geef een naam op voor het aangepaste onderhoudstype.')
      return
    }
    setSaving(true)
    try {
      if (editing) {
        await updateMaintenance(editing.id, { ...form, status: editing.status })
        showSuccess('Onderhoud bijgewerkt.')
      } else {
        await createMaintenance(ownerType, ownerId, form)
        showSuccess('Onderhoud gepland.')
      }
      setEditorOpen(false)
      setReloadToken((t) => t + 1)
    } catch {
      setFormError('Het onderhoud kon niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleComplete(event: FormEvent) {
    event.preventDefault()
    if (!completeTarget || !completeForm) return
    setSaving(true)
    try {
      const result = await completeMaintenance(completeTarget.id, completeForm)
      showSuccess(
        result.followUp
          ? `Onderhoud afgerond — vervolgonderhoud gepland op ${result.followUp.scheduledDate ?? 'kilometertrigger'}.`
          : 'Onderhoud afgerond.',
      )
      setCompleteTarget(null)
      setCompleteForm(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('Het onderhoud kon niet worden afgerond.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteMaintenance(deleteTarget.id)
      showSuccess('Onderhoud verwijderd.')
      setDeleteTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('Het onderhoud kon niet worden verwijderd.')
      setDeleteTarget(null)
    }
  }

  const canEdit = hasPermission('maintenance.edit')

  return (
    <section className="maint">
      <div className="maint-header">
        <h2>Onderhoud</h2>
        {hasPermission('maintenance.create') && (
          <Button variant="secondary" onClick={openCreate}>
            Onderhoud plannen
          </Button>
        )}
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && records === null && <p className="placeholder-text">Onderhoud laden…</p>}
      {!loadError && records !== null && records.length === 0 && (
        <p className="placeholder-text">Nog geen onderhoud geregistreerd.</p>
      )}

      {!loadError && records !== null && records.length > 0 && (
        <table className="maint-table">
          <thead>
            <tr>
              <th>Type</th>
              <th>Omschrijving</th>
              <th>Gepland</th>
              <th>Status</th>
              <th>Afgerond</th>
              <th>Kosten</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {records.map((record) => (
              <tr key={record.id}>
                <td>{maintenanceDisplayName(record)}</td>
                <td className="maint-description">{record.description}</td>
                <td>
                  {formatDate(record.scheduledDate) || (record.odometerTriggerKm != null ? `${record.odometerTriggerKm.toLocaleString('nl-BE')} km` : '—')}
                </td>
                <td>
                  <span className="maint-badges">
                    <Badge tone={STATUS_TONE[record.status]}>{MAINTENANCE_STATUS_LABELS[record.status]}</Badge>
                    {record.isOverdue && <Badge tone="danger">Te laat</Badge>}
                  </span>
                </td>
                <td>{formatDate(record.completedDate) || '—'}</td>
                <td>{record.cost != null ? `€ ${record.cost.toLocaleString('nl-BE')}` : '—'}</td>
                <td className="maint-actions">
                  {canEdit && (record.status === 'Planned' || record.status === 'InProgress') && (
                    <>
                      <button type="button" className="maint-link" onClick={() => openComplete(record)}>
                        Afronden
                      </button>
                      <button type="button" className="maint-link" onClick={() => openEdit(record)}>
                        Bewerken
                      </button>
                    </>
                  )}
                  {hasPermission('maintenance.delete') && (
                    <button type="button" className="maint-link maint-link-danger" onClick={() => setDeleteTarget(record)}>
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
          title={editing ? 'Onderhoud bewerken' : 'Onderhoud plannen'}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="maint-form" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="maint-form" className="maint-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="maint-form-error" role="alert">
                {formError}
              </div>
            )}
            <FormField label="Type" htmlFor="mt-type" required>
              <select
                id="mt-type"
                value={form.maintenanceType}
                onChange={(e) => set('maintenanceType', e.target.value as MaintenanceType)}
                disabled={saving}
              >
                {MAINTENANCE_TYPES.map((type) => (
                  <option key={type} value={type}>
                    {MAINTENANCE_TYPE_LABELS[type]}
                  </option>
                ))}
              </select>
            </FormField>
            {form.maintenanceType === 'Other' && (
              <FormField label="Naam onderhoudstype" htmlFor="mt-custom" required>
                <input
                  id="mt-custom"
                  value={form.customTypeName ?? ''}
                  onChange={(e) => set('customTypeName', e.target.value || null)}
                  disabled={saving}
                  maxLength={100}
                />
              </FormField>
            )}
            <FormField label="Omschrijving" htmlFor="mt-desc" required>
              <input
                id="mt-desc"
                value={form.description}
                onChange={(e) => set('description', e.target.value)}
                disabled={saving}
                maxLength={500}
              />
            </FormField>
            <div className="maint-form-row">
              <FormField label="Geplande datum" htmlFor="mt-scheduled">
                <input
                  id="mt-scheduled"
                  type="date"
                  value={form.scheduledDate ?? ''}
                  onChange={(e) => set('scheduledDate', e.target.value || null)}
                  disabled={saving}
                />
              </FormField>
              {ownerType === 'vehicle' && (
                <FormField label="Kilometertrigger" htmlFor="mt-odo">
                  <input
                    id="mt-odo"
                    type="number"
                    min={0}
                    value={form.odometerTriggerKm ?? ''}
                    onChange={(e) => set('odometerTriggerKm', e.target.value === '' ? null : Number(e.target.value))}
                    disabled={saving}
                  />
                </FormField>
              )}
            </div>
            <FormField label="Leverancier / garage" htmlFor="mt-provider">
              <input
                id="mt-provider"
                value={form.provider ?? ''}
                onChange={(e) => set('provider', e.target.value || null)}
                disabled={saving}
                maxLength={200}
              />
            </FormField>
            <div className="maint-form-row">
              <FormField label="Herhaal elke (maanden)" htmlFor="mt-interval-m" hint="Leeg = eenmalig">
                <input
                  id="mt-interval-m"
                  type="number"
                  min={1}
                  max={120}
                  value={form.intervalMonths ?? ''}
                  onChange={(e) => set('intervalMonths', e.target.value === '' ? null : Number(e.target.value))}
                  disabled={saving}
                />
              </FormField>
              {ownerType === 'vehicle' && (
                <FormField label="Herhaal elke (km)" htmlFor="mt-interval-km">
                  <input
                    id="mt-interval-km"
                    type="number"
                    min={1}
                    value={form.intervalKm ?? ''}
                    onChange={(e) => set('intervalKm', e.target.value === '' ? null : Number(e.target.value))}
                    disabled={saving}
                  />
                </FormField>
              )}
            </div>
            <FormField label="Notities" htmlFor="mt-notes">
              <textarea
                id="mt-notes"
                rows={2}
                value={form.notes ?? ''}
                onChange={(e) => set('notes', e.target.value || null)}
                disabled={saving}
              />
            </FormField>
          </form>
        </Modal>
      )}

      {completeTarget && completeForm && (
        <Modal
          title="Onderhoud afronden"
          onClose={() => setCompleteTarget(null)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCompleteTarget(null)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="maint-complete-form" disabled={saving}>
                {saving ? 'Bezig…' : 'Afronden'}
              </Button>
            </>
          }
        >
          <form id="maint-complete-form" className="maint-form" onSubmit={handleComplete} noValidate>
            <FormField label="Datum afgerond" htmlFor="mc-date" required>
              <input
                id="mc-date"
                type="date"
                value={completeForm.completedDate}
                onChange={(e) => setCompleteForm((f) => (f ? { ...f, completedDate: e.target.value } : f))}
                disabled={saving}
              />
            </FormField>
            {ownerType === 'vehicle' && (
              <FormField label="Kilometerstand bij afronding" htmlFor="mc-odo">
                <input
                  id="mc-odo"
                  type="number"
                  min={0}
                  value={completeForm.completedOdometerKm ?? ''}
                  onChange={(e) =>
                    setCompleteForm((f) => (f ? { ...f, completedOdometerKm: e.target.value === '' ? null : Number(e.target.value) } : f))
                  }
                  disabled={saving}
                />
              </FormField>
            )}
            <FormField label="Uitgevoerde werkzaamheden" htmlFor="mc-work">
              <textarea
                id="mc-work"
                rows={2}
                value={completeForm.workPerformed ?? ''}
                onChange={(e) => setCompleteForm((f) => (f ? { ...f, workPerformed: e.target.value || null } : f))}
                disabled={saving}
              />
            </FormField>
            <div className="maint-form-row">
              <FormField label="Leverancier / garage" htmlFor="mc-provider">
                <input
                  id="mc-provider"
                  value={completeForm.provider ?? ''}
                  onChange={(e) => setCompleteForm((f) => (f ? { ...f, provider: e.target.value || null } : f))}
                  disabled={saving}
                />
              </FormField>
              <FormField label="Kosten (€)" htmlFor="mc-cost">
                <input
                  id="mc-cost"
                  type="number"
                  min={0}
                  step="0.01"
                  value={completeForm.cost ?? ''}
                  onChange={(e) => setCompleteForm((f) => (f ? { ...f, cost: e.target.value === '' ? null : Number(e.target.value) } : f))}
                  disabled={saving}
                />
              </FormField>
            </div>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Onderhoud verwijderen"
          message={`Weet je zeker dat je "${maintenanceDisplayName(deleteTarget)} — ${deleteTarget.description}" wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
