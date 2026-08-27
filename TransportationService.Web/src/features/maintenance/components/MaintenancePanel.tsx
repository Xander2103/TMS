import { useEffect, useState, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { formatDate } from '../../../utils/dates'
import { formatCurrency, formatInteger } from '../../../utils/numbers'
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
  const { t } = useLocale()
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
        if (mounted) setLoadError(t('maintenance.panel.loadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [ownerType, ownerId, reloadToken, t])

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
      setFormError(t('maintenance.panel.descriptionRequired'))
      return
    }
    if (form.maintenanceType === 'Other' && !form.customTypeName?.trim()) {
      setFormError(t('maintenance.panel.customNameRequired'))
      return
    }
    setSaving(true)
    try {
      if (editing) {
        await updateMaintenance(editing.id, { ...form, status: editing.status })
        showSuccess(t('maintenance.panel.updated'))
      } else {
        await createMaintenance(ownerType, ownerId, form)
        showSuccess(t('maintenance.panel.planned'))
      }
      setEditorOpen(false)
      setReloadToken((token) => token + 1)
    } catch {
      setFormError(t('maintenance.panel.saveFailed'))
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
          ? t('maintenance.panel.completedWithFollowUp', {
              date: result.followUp.scheduledDate ?? t('maintenance.panel.kmTriggerFallback'),
            })
          : t('maintenance.panel.completed'),
      )
      setCompleteTarget(null)
      setCompleteForm(null)
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('maintenance.panel.completeFailed'))
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteMaintenance(deleteTarget.id)
      showSuccess(t('maintenance.panel.deleted'))
      setDeleteTarget(null)
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('maintenance.panel.deleteFailed'))
      setDeleteTarget(null)
    }
  }

  const canEdit = hasPermission('maintenance.edit')

  return (
    <section className="maint">
      <div className="maint-header">
        <h2>{t('maintenance.panel.title')}</h2>
        {hasPermission('maintenance.create') && (
          <Button variant="secondary" onClick={openCreate}>
            {t('maintenance.panel.plan')}
          </Button>
        )}
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && records === null && <p className="placeholder-text">{t('maintenance.panel.loading')}</p>}
      {!loadError && records !== null && records.length === 0 && (
        <p className="placeholder-text">{t('maintenance.panel.empty')}</p>
      )}

      {!loadError && records !== null && records.length > 0 && (
        <table className="maint-table">
          <thead>
            <tr>
              <th>{t('maintenance.panel.colType')}</th>
              <th>{t('maintenance.panel.colDescription')}</th>
              <th>{t('maintenance.panel.colPlanned')}</th>
              <th>{t('maintenance.panel.colStatus')}</th>
              <th>{t('maintenance.panel.colCompleted')}</th>
              <th>{t('maintenance.panel.colCost')}</th>
              <th aria-label={t('fleet.common.actions')} />
            </tr>
          </thead>
          <tbody>
            {records.map((record) => (
              <tr key={record.id}>
                <td>{t(maintenanceDisplayName(record))}</td>
                <td className="maint-description">{record.description}</td>
                <td>
                  {formatDate(record.scheduledDate) || (record.odometerTriggerKm != null ? `${formatInteger(record.odometerTriggerKm)} km` : '—')}
                </td>
                <td>
                  <span className="maint-badges">
                    <Badge tone={STATUS_TONE[record.status]}>{t(MAINTENANCE_STATUS_LABELS[record.status])}</Badge>
                    {record.isOverdue && <Badge tone="danger">{t('maintenance.overdue')}</Badge>}
                  </span>
                </td>
                <td>{formatDate(record.completedDate) || '—'}</td>
                <td>{record.cost != null ? formatCurrency(record.cost) : '—'}</td>
                <td className="maint-actions">
                  {canEdit && (record.status === 'Planned' || record.status === 'InProgress') && (
                    <>
                      <button type="button" className="maint-link" onClick={() => openComplete(record)}>
                        {t('maintenance.panel.complete')}
                      </button>
                      <button type="button" className="maint-link" onClick={() => openEdit(record)}>
                        {t('ui.actions.edit')}
                      </button>
                    </>
                  )}
                  {hasPermission('maintenance.delete') && (
                    <button type="button" className="maint-link maint-link-danger" onClick={() => setDeleteTarget(record)}>
                      {t('ui.actions.delete')}
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
          title={editing ? t('maintenance.panel.editTitle') : t('maintenance.panel.planTitle')}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="maint-form" disabled={saving}>
                {saving ? t('fleet.common.saving') : t('ui.actions.save')}
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
            <FormField label={t('maintenance.panel.type')} htmlFor="mt-type" required>
              <select
                id="mt-type"
                value={form.maintenanceType}
                onChange={(e) => set('maintenanceType', e.target.value as MaintenanceType)}
                disabled={saving}
              >
                {MAINTENANCE_TYPES.map((type) => (
                  <option key={type} value={type}>
                    {t(MAINTENANCE_TYPE_LABELS[type])}
                  </option>
                ))}
              </select>
            </FormField>
            {form.maintenanceType === 'Other' && (
              <FormField label={t('maintenance.panel.customName')} htmlFor="mt-custom" required>
                <input
                  id="mt-custom"
                  value={form.customTypeName ?? ''}
                  onChange={(e) => set('customTypeName', e.target.value || null)}
                  disabled={saving}
                  maxLength={100}
                />
              </FormField>
            )}
            <FormField label={t('maintenance.panel.description')} htmlFor="mt-desc" required>
              <input
                id="mt-desc"
                value={form.description}
                onChange={(e) => set('description', e.target.value)}
                disabled={saving}
                maxLength={500}
              />
            </FormField>
            <div className="maint-form-row">
              <FormField label={t('maintenance.panel.scheduledDate')} htmlFor="mt-scheduled">
                <input
                  id="mt-scheduled"
                  type="date"
                  value={form.scheduledDate ?? ''}
                  onChange={(e) => set('scheduledDate', e.target.value || null)}
                  disabled={saving}
                />
              </FormField>
              {ownerType === 'vehicle' && (
                <FormField label={t('maintenance.panel.odometerTrigger')} htmlFor="mt-odo">
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
            <FormField label={t('maintenance.panel.provider')} htmlFor="mt-provider">
              <input
                id="mt-provider"
                value={form.provider ?? ''}
                onChange={(e) => set('provider', e.target.value || null)}
                disabled={saving}
                maxLength={200}
              />
            </FormField>
            <div className="maint-form-row">
              <FormField label={t('maintenance.panel.repeatMonths')} htmlFor="mt-interval-m" hint={t('maintenance.panel.repeatMonthsHint')}>
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
                <FormField label={t('maintenance.panel.repeatKm')} htmlFor="mt-interval-km">
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
            <FormField label={t('maintenance.panel.notes')} htmlFor="mt-notes">
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
          title={t('maintenance.panel.completeTitle')}
          onClose={() => setCompleteTarget(null)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCompleteTarget(null)} disabled={saving}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="maint-complete-form" disabled={saving}>
                {saving ? t('fleet.common.busy') : t('maintenance.panel.complete')}
              </Button>
            </>
          }
        >
          <form id="maint-complete-form" className="maint-form" onSubmit={handleComplete} noValidate>
            <FormField label={t('maintenance.panel.completedDate')} htmlFor="mc-date" required>
              <input
                id="mc-date"
                type="date"
                value={completeForm.completedDate}
                onChange={(e) => setCompleteForm((f) => (f ? { ...f, completedDate: e.target.value } : f))}
                disabled={saving}
              />
            </FormField>
            {ownerType === 'vehicle' && (
              <FormField label={t('maintenance.panel.completedOdometer')} htmlFor="mc-odo">
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
            <FormField label={t('maintenance.panel.workPerformed')} htmlFor="mc-work">
              <textarea
                id="mc-work"
                rows={2}
                value={completeForm.workPerformed ?? ''}
                onChange={(e) => setCompleteForm((f) => (f ? { ...f, workPerformed: e.target.value || null } : f))}
                disabled={saving}
              />
            </FormField>
            <div className="maint-form-row">
              <FormField label={t('maintenance.panel.provider')} htmlFor="mc-provider">
                <input
                  id="mc-provider"
                  value={completeForm.provider ?? ''}
                  onChange={(e) => setCompleteForm((f) => (f ? { ...f, provider: e.target.value || null } : f))}
                  disabled={saving}
                />
              </FormField>
              <FormField label={t('maintenance.panel.cost')} htmlFor="mc-cost">
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
          title={t('maintenance.panel.deleteTitle')}
          message={t('maintenance.panel.deleteMessage', { name: t(maintenanceDisplayName(deleteTarget)), description: deleteTarget.description })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
