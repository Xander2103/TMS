import { useEffect, useState, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import {
  completeInspection,
  createInspection,
  deleteInspection,
  listInspections,
  updateInspection,
  type InspectionOwnerType,
} from '../api/inspectionsApi'
import {
  INSPECTION_RESULT_LABELS,
  INSPECTION_TYPE_LABELS,
  INSPECTION_TYPES,
  INSPECTION_URGENCY_LABELS,
  inspectionDisplayName,
  type CompleteInspectionInput,
  type Inspection,
  type InspectionInput,
  type InspectionResult,
  type InspectionType,
} from '../types'
import './inspections.css'

const URGENCY_TONE: Record<Inspection['urgency'], BadgeTone> = {
  Ok: 'info',
  DueSoon: 'warning',
  Overdue: 'danger',
  Completed: 'success',
}

const RESULT_TONE: Record<InspectionResult, BadgeTone> = {
  Passed: 'success',
  PassedWithRemarks: 'warning',
  Failed: 'danger',
}

const EMPTY_FORM: InspectionInput = {
  inspectionType: 'VehicleInspection',
  customTypeName: null,
  dueDate: '',
  intervalMonths: null,
  warningDays: null,
  notes: null,
}

interface InspectionsPanelProps {
  ownerType: InspectionOwnerType
  ownerId: string
}

/** Inspections section for a vehicle or trailer detail page: plan, edit, register result, delete. */
export function InspectionsPanel({ ownerType, ownerId }: InspectionsPanelProps) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [inspections, setInspections] = useState<Inspection[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<Inspection | null>(null)
  const [form, setForm] = useState<InspectionInput>(EMPTY_FORM)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const [completeTarget, setCompleteTarget] = useState<Inspection | null>(null)
  const [completeForm, setCompleteForm] = useState<CompleteInspectionInput | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Inspection | null>(null)

  useEffect(() => {
    let mounted = true
    listInspections(ownerType, ownerId)
      .then((data) => {
        if (!mounted) return
        setInspections(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('maintenance.insp.panel.loadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [ownerType, ownerId, reloadToken, t])

  function set<K extends keyof InspectionInput>(key: K, value: InspectionInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function openCreate() {
    setEditing(null)
    setForm({
      ...EMPTY_FORM,
      inspectionType: ownerType === 'trailer' ? 'TrailerInspection' : 'VehicleInspection',
    })
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(inspection: Inspection) {
    setEditing(inspection)
    setForm({
      inspectionType: inspection.inspectionType,
      customTypeName: inspection.customTypeName,
      dueDate: inspection.dueDate,
      intervalMonths: inspection.intervalMonths,
      warningDays: inspection.warningDays,
      notes: inspection.notes,
    })
    setFormError(null)
    setEditorOpen(true)
  }

  function openComplete(inspection: Inspection) {
    setCompleteTarget(inspection)
    setCompleteForm({
      completedDate: new Date().toISOString().slice(0, 10),
      result: 'Passed',
      notes: null,
    })
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.dueDate) {
      setFormError(t('maintenance.insp.panel.dueRequired'))
      return
    }
    if (form.inspectionType === 'Other' && !form.customTypeName?.trim()) {
      setFormError(t('maintenance.insp.panel.customNameRequired'))
      return
    }
    setSaving(true)
    try {
      if (editing) {
        await updateInspection(editing.id, form)
        showSuccess(t('maintenance.insp.panel.updated'))
      } else {
        await createInspection(ownerType, ownerId, form)
        showSuccess(t('maintenance.insp.panel.planned'))
      }
      setEditorOpen(false)
      setReloadToken((token) => token + 1)
    } catch {
      setFormError(t('maintenance.insp.panel.saveFailed'))
    } finally {
      setSaving(false)
    }
  }

  async function handleComplete(event: FormEvent) {
    event.preventDefault()
    if (!completeTarget || !completeForm) return
    setSaving(true)
    try {
      const result = await completeInspection(completeTarget.id, completeForm)
      showSuccess(
        result.followUp
          ? t('maintenance.insp.panel.registeredWithFollowUp', { date: result.followUp.dueDate })
          : t('maintenance.insp.panel.registered'),
      )
      setCompleteTarget(null)
      setCompleteForm(null)
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('maintenance.insp.panel.registerFailed'))
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteInspection(deleteTarget.id)
      showSuccess(t('maintenance.insp.panel.deleted'))
      setDeleteTarget(null)
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('maintenance.insp.panel.deleteFailed'))
      setDeleteTarget(null)
    }
  }

  const canEdit = hasPermission('inspections.edit')

  return (
    <section className="insp">
      <div className="insp-header">
        <h2>{t('maintenance.insp.panel.title')}</h2>
        {hasPermission('inspections.create') && (
          <Button variant="secondary" onClick={openCreate}>
            {t('maintenance.insp.panel.plan')}
          </Button>
        )}
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && inspections === null && <p className="placeholder-text">{t('maintenance.insp.panel.loading')}</p>}
      {!loadError && inspections !== null && inspections.length === 0 && (
        <p className="placeholder-text">{t('maintenance.insp.panel.empty')}</p>
      )}

      {!loadError && inspections !== null && inspections.length > 0 && (
        <table className="insp-table">
          <thead>
            <tr>
              <th>{t('maintenance.insp.panel.colInspection')}</th>
              <th>{t('maintenance.insp.panel.colDue')}</th>
              <th>{t('maintenance.insp.panel.colStatus')}</th>
              <th>{t('maintenance.insp.panel.colCompleted')}</th>
              <th>{t('maintenance.insp.panel.colResult')}</th>
              <th aria-label={t('fleet.common.actions')} />
            </tr>
          </thead>
          <tbody>
            {inspections.map((inspection) => (
              <tr key={inspection.id}>
                <td>{t(inspectionDisplayName(inspection))}</td>
                <td>{inspection.dueDate}</td>
                <td>
                  <Badge tone={URGENCY_TONE[inspection.urgency]}>{t(INSPECTION_URGENCY_LABELS[inspection.urgency])}</Badge>
                </td>
                <td>{inspection.completedDate ?? '—'}</td>
                <td>
                  {inspection.result ? (
                    <Badge tone={RESULT_TONE[inspection.result]}>{t(INSPECTION_RESULT_LABELS[inspection.result])}</Badge>
                  ) : (
                    '—'
                  )}
                </td>
                <td className="insp-actions">
                  {canEdit && inspection.completedDate === null && (
                    <>
                      <button type="button" className="insp-link" onClick={() => openComplete(inspection)}>
                        {t('maintenance.insp.panel.register')}
                      </button>
                      <button type="button" className="insp-link" onClick={() => openEdit(inspection)}>
                        {t('ui.actions.edit')}
                      </button>
                    </>
                  )}
                  {hasPermission('inspections.delete') && (
                    <button type="button" className="insp-link insp-link-danger" onClick={() => setDeleteTarget(inspection)}>
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
          title={editing ? t('maintenance.insp.panel.editTitle') : t('maintenance.insp.panel.planTitle')}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="insp-form" disabled={saving}>
                {saving ? t('fleet.common.saving') : t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="insp-form" className="insp-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="insp-form-error" role="alert">
                {formError}
              </div>
            )}
            <FormField label={t('maintenance.insp.panel.typeField')} htmlFor="in-type" required>
              <select
                id="in-type"
                value={form.inspectionType}
                onChange={(e) => set('inspectionType', e.target.value as InspectionType)}
                disabled={saving}
              >
                {INSPECTION_TYPES.map((type) => (
                  <option key={type} value={type}>
                    {t(INSPECTION_TYPE_LABELS[type])}
                  </option>
                ))}
              </select>
            </FormField>
            {form.inspectionType === 'Other' && (
              <FormField label={t('maintenance.insp.panel.customName')} htmlFor="in-custom" required>
                <input
                  id="in-custom"
                  value={form.customTypeName ?? ''}
                  onChange={(e) => set('customTypeName', e.target.value || null)}
                  disabled={saving}
                  maxLength={100}
                />
              </FormField>
            )}
            <div className="insp-form-row">
              <FormField label={t('maintenance.insp.panel.dueDate')} htmlFor="in-due" required>
                <input
                  id="in-due"
                  type="date"
                  value={form.dueDate}
                  onChange={(e) => set('dueDate', e.target.value)}
                  disabled={saving}
                />
              </FormField>
              <FormField
                label={t('maintenance.insp.panel.repeatMonths')}
                htmlFor="in-interval"
                hint={form.inspectionType === 'CraneInspection' ? t('maintenance.insp.panel.craneHint') : t('maintenance.insp.panel.onceHint')}
              >
                <input
                  id="in-interval"
                  type="number"
                  min={1}
                  max={120}
                  value={form.intervalMonths ?? ''}
                  onChange={(e) => set('intervalMonths', e.target.value === '' ? null : Number(e.target.value))}
                  disabled={saving}
                />
              </FormField>
            </div>
            <FormField label={t('maintenance.insp.panel.warningDays')} htmlFor="in-warning" hint={t('maintenance.insp.panel.warningHint')}>
              <input
                id="in-warning"
                type="number"
                min={0}
                max={365}
                value={form.warningDays ?? ''}
                onChange={(e) => set('warningDays', e.target.value === '' ? null : Number(e.target.value))}
                disabled={saving}
              />
            </FormField>
            <FormField label={t('maintenance.insp.panel.notes')} htmlFor="in-notes">
              <textarea
                id="in-notes"
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
          title={t('maintenance.insp.panel.registerTitle')}
          onClose={() => setCompleteTarget(null)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCompleteTarget(null)} disabled={saving}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="insp-complete-form" disabled={saving}>
                {saving ? t('fleet.common.busy') : t('maintenance.insp.panel.register')}
              </Button>
            </>
          }
        >
          <form id="insp-complete-form" className="insp-form" onSubmit={handleComplete} noValidate>
            <FormField label={t('maintenance.insp.panel.completedDate')} htmlFor="ic-date" required>
              <input
                id="ic-date"
                type="date"
                value={completeForm.completedDate}
                onChange={(e) => setCompleteForm((f) => (f ? { ...f, completedDate: e.target.value } : f))}
                disabled={saving}
              />
            </FormField>
            <FormField label={t('maintenance.insp.panel.result')} htmlFor="ic-result" required>
              <select
                id="ic-result"
                value={completeForm.result}
                onChange={(e) => setCompleteForm((f) => (f ? { ...f, result: e.target.value as InspectionResult } : f))}
                disabled={saving}
              >
                {(Object.keys(INSPECTION_RESULT_LABELS) as InspectionResult[]).map((result) => (
                  <option key={result} value={result}>
                    {t(INSPECTION_RESULT_LABELS[result])}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label={t('maintenance.insp.panel.notes')} htmlFor="ic-notes">
              <textarea
                id="ic-notes"
                rows={2}
                value={completeForm.notes ?? ''}
                onChange={(e) => setCompleteForm((f) => (f ? { ...f, notes: e.target.value || null } : f))}
                disabled={saving}
              />
            </FormField>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('maintenance.insp.panel.deleteTitle')}
          message={t('maintenance.insp.panel.deleteMessage', { name: t(inspectionDisplayName(deleteTarget)), date: deleteTarget.dueDate })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
