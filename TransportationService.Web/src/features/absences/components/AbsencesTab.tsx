import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import {
  cancelAbsence,
  createAbsence,
  decideAbsence,
  deleteAbsence,
  listEmployeeAbsences,
  updateAbsence,
} from '../api/absencesApi'
import { getLeaveTypes } from '../../leave-balance/api/leaveBalanceApi'
import type { LeaveType } from '../../leave-balance/types'
import {
  ABSENCE_STATUS_TONE,
  type Absence,
  type AbsenceInput,
  type AbsenceType,
} from '../types'
import './absences.css'

interface AbsenceForm {
  type: AbsenceType
  /** Master-data leave category; '' only for legacy absences that predate leave types. */
  leaveTypeId: string
  startDate: string
  endDate: string
  reason: string
}

const EMPTY_FORM: AbsenceForm = {
  type: 'Vacation',
  leaveTypeId: '',
  startDate: '',
  endDate: '',
  reason: '',
}

interface AbsencesTabProps {
  employeeId: string
  /** Deep-link target: this absence row is highlighted and scrolled into view. */
  highlightAbsenceId?: string | null
}

/** Absences for one employee: request, edit while requested, approve/reject, cancel, delete. */
export function AbsencesTab({ employeeId, highlightAbsenceId }: AbsencesTabProps) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [absences, setAbsences] = useState<Absence[] | null>(null)
  const [leaveTypes, setLeaveTypes] = useState<LeaveType[]>([])
  // Vertaalsleutels in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<Absence | null>(null)
  const [form, setForm] = useState<AbsenceForm>(EMPTY_FORM)
  const [formErrorKey, setFormErrorKey] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const [decideTarget, setDecideTarget] = useState<{ absence: Absence; approve: boolean } | null>(null)
  const [decisionNote, setDecisionNote] = useState('')
  const [cancelTarget, setCancelTarget] = useState<Absence | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Absence | null>(null)

  useEffect(() => {
    let mounted = true
    listEmployeeAbsences(employeeId)
      .then((data) => {
        if (!mounted) return
        setAbsences(data)
        setLoadErrorKey(null)
      })
      .catch(() => {
        if (mounted) setLoadErrorKey('absences.tab.loadFailed')
      })
    return () => {
      mounted = false
    }
  }, [employeeId, reloadToken])

  useEffect(() => {
    let mounted = true
    // Only ACTIVE leave categories are selectable for new registrations; historical rows keep
    // rendering their stored type (corrections wave §5).
    getLeaveTypes({ activeOnly: true })
      .then((data) => {
        if (mounted) setLeaveTypes(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  function set<K extends keyof AbsenceForm>(key: K, value: AbsenceForm[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function openCreate() {
    setEditing(null)
    setForm({ ...EMPTY_FORM, leaveTypeId: leaveTypes[0]?.id ?? '' })
    setFormErrorKey(null)
    setEditorOpen(true)
  }

  function openEdit(absence: Absence) {
    setEditing(absence)
    setForm({
      type: absence.type,
      leaveTypeId: absence.leaveTypeId ?? '',
      startDate: absence.startDate,
      endDate: absence.endDate,
      reason: absence.reason ?? '',
    })
    setFormErrorKey(null)
    setEditorOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormErrorKey(null)
    if (!form.startDate || !form.endDate) {
      setFormErrorKey('absences.tab.datesRequired')
      return
    }
    if (form.endDate < form.startDate) {
      setFormErrorKey('absences.tab.endBeforeStart')
      return
    }
    const selectedLeaveType = leaveTypes.find((t) => t.id === form.leaveTypeId)
    const input: AbsenceInput = {
      // The leave category is the source of truth; the enum type rides along for legacy rows.
      type: (selectedLeaveType?.absenceType as AbsenceType | undefined) ?? form.type,
      leaveTypeId: form.leaveTypeId || null,
      startDate: form.startDate,
      endDate: form.endDate,
      reason: form.reason.trim() || null,
    }
    setSaving(true)
    try {
      if (editing) {
        await updateAbsence(editing.id, input)
        showSuccess(t('absences.tab.updated'))
      } else {
        await createAbsence(employeeId, input)
        showSuccess(t('absences.tab.created'))
      }
      setEditorOpen(false)
      setReloadToken((t) => t + 1)
    } catch (err) {
      setFormErrorKey(
        err instanceof ApiError && err.status === 409 ? 'absences.tab.overlap' : 'absences.tab.saveFailed',
      )
    } finally {
      setSaving(false)
    }
  }

  async function handleDecide(event: FormEvent) {
    event.preventDefault()
    if (!decideTarget) return
    setSaving(true)
    try {
      await decideAbsence(decideTarget.absence.id, decideTarget.approve, decisionNote.trim() || null)
      showSuccess(decideTarget.approve ? t('absences.tab.approved') : t('absences.tab.rejected'))
      setDecideTarget(null)
      setDecisionNote('')
      setReloadToken((t) => t + 1)
    } catch {
      showError(t('absences.tab.decideFailed'))
    } finally {
      setSaving(false)
    }
  }

  async function handleCancel() {
    if (!cancelTarget) return
    try {
      await cancelAbsence(cancelTarget.id)
      showSuccess(t('absences.tab.cancelled'))
      setCancelTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError(t('absences.tab.cancelFailed'))
      setCancelTarget(null)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteAbsence(deleteTarget.id)
      showSuccess(t('absences.tab.deleted'))
      setDeleteTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError(t('absences.tab.deleteFailed'))
      setDeleteTarget(null)
    }
  }

  const canEdit = hasPermission('absences.edit')
  const canApprove = hasPermission('absences.approve')

  return (
    <section className="abs">
      <div className="abs-header">
        <h2>{t('absences.tab.title')}</h2>
        {hasPermission('absences.create') && (
          <Button variant="secondary" onClick={openCreate}>
            {t('absences.tab.request')}
          </Button>
        )}
      </div>

      {loadErrorKey && <p className="placeholder-text">{t(loadErrorKey)}</p>}
      {!loadErrorKey && absences === null && <p className="placeholder-text">{t('absences.tab.loading')}</p>}
      {!loadErrorKey && absences !== null && absences.length === 0 && (
        <p className="placeholder-text">{t('absences.tab.empty')}</p>
      )}

      {!loadErrorKey && absences !== null && absences.length > 0 && (
        <table className="abs-table">
          <thead>
            <tr>
              <th>{t('absences.tab.colType')}</th>
              <th>{t('absences.tab.colFrom')}</th>
              <th>{t('absences.tab.colTo')}</th>
              <th>{t('absences.tab.colStatus')}</th>
              <th>{t('absences.tab.colReason')}</th>
              <th aria-label={t('absences.tab.colActions')} />
            </tr>
          </thead>
          <tbody>
            {absences.map((absence) => (
              <tr
                key={absence.id}
                id={`absence-${absence.id}`}
                className={absence.id === highlightAbsenceId ? 'absence-row-highlight' : undefined}
                ref={
                  absence.id === highlightAbsenceId
                    ? (row) => row?.scrollIntoView({ block: 'center', behavior: 'smooth' })
                    : undefined
                }
              >
                <td>{t(`absences.type.${absence.type}`)}</td>
                <td>{absence.startDate}</td>
                <td>{absence.endDate}</td>
                <td>
                  <span title={absence.decisionNote ?? undefined}>
                    <Badge tone={ABSENCE_STATUS_TONE[absence.status]}>{t(`absences.status.${absence.status}`)}</Badge>
                  </span>
                </td>
                <td className="abs-reason" title={absence.reason ?? undefined}>
                  {absence.reason ?? '—'}
                </td>
                <td className="abs-actions">
                  {canApprove && absence.status === 'Requested' && (
                    <>
                      <button
                        type="button"
                        className="abs-link"
                        onClick={() => {
                          setDecideTarget({ absence, approve: true })
                          setDecisionNote('')
                        }}
                      >
                        {t('absences.tab.approve')}
                      </button>
                      <button
                        type="button"
                        className="abs-link abs-link-danger"
                        onClick={() => {
                          setDecideTarget({ absence, approve: false })
                          setDecisionNote('')
                        }}
                      >
                        {t('absences.tab.reject')}
                      </button>
                    </>
                  )}
                  {canEdit && absence.status === 'Requested' && (
                    <button type="button" className="abs-link" onClick={() => openEdit(absence)}>
                      {t('ui.actions.edit')}
                    </button>
                  )}
                  {canEdit && (absence.status === 'Requested' || absence.status === 'Approved') && (
                    <button type="button" className="abs-link" onClick={() => setCancelTarget(absence)}>
                      {t('ui.actions.cancel')}
                    </button>
                  )}
                  {hasPermission('absences.delete') && (
                    <button type="button" className="abs-link abs-link-danger" onClick={() => setDeleteTarget(absence)}>
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
          title={editing ? t('absences.tab.editTitle') : t('absences.tab.createTitle')}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="abs-form" disabled={saving}>
                {saving ? t('absences.tab.saving') : t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="abs-form" className="abs-form" onSubmit={handleSubmit} noValidate>
            {formErrorKey && (
              <div className="abs-form-error" role="alert">
                {t(formErrorKey)}
              </div>
            )}
            <FormField label={t('absences.tab.category')} htmlFor="ab-type" required>
              <select id="ab-type" value={form.leaveTypeId} onChange={(e) => set('leaveTypeId', e.target.value)} disabled={saving}>
                {editing && !editing.leaveTypeId && (
                  <option value="">{t('absences.tab.legacyOption', { type: t(`absences.type.${editing.type}`) })}</option>
                )}
                {leaveTypes.map((leaveType) => (
                  <option key={leaveType.id} value={leaveType.id}>
                    {leaveType.name}
                  </option>
                ))}
              </select>
            </FormField>
            <div className="abs-form-row">
              <FormField label={t('absences.tab.colFrom')} htmlFor="ab-start" required>
                <input id="ab-start" type="date" value={form.startDate} onChange={(e) => set('startDate', e.target.value)} disabled={saving} />
              </FormField>
              <FormField label={t('absences.tab.colTo')} htmlFor="ab-end" required>
                <input id="ab-end" type="date" value={form.endDate} onChange={(e) => set('endDate', e.target.value)} disabled={saving} />
              </FormField>
            </div>
            <FormField label={t('absences.tab.reason')} htmlFor="ab-reason">
              <textarea id="ab-reason" rows={2} value={form.reason} onChange={(e) => set('reason', e.target.value)} disabled={saving} maxLength={1000} />
            </FormField>
          </form>
        </Modal>
      )}

      {decideTarget && (
        <Modal
          title={decideTarget.approve ? t('absences.tab.decideApproveTitle') : t('absences.tab.decideRejectTitle')}
          onClose={() => setDecideTarget(null)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDecideTarget(null)} disabled={saving}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="abs-decide-form" disabled={saving}>
                {saving ? t('absences.tab.busy') : decideTarget.approve ? t('absences.tab.approve') : t('absences.tab.reject')}
              </Button>
            </>
          }
        >
          <form id="abs-decide-form" className="abs-form" onSubmit={handleDecide} noValidate>
            <p className="abs-decide-text">
              {t('absences.tab.decideSummary', {
                type: t(`absences.type.${decideTarget.absence.type}`),
                from: decideTarget.absence.startDate,
                to: decideTarget.absence.endDate,
              })}
            </p>
            <FormField label={t('absences.tab.note')} htmlFor="ab-note">
              <input id="ab-note" value={decisionNote} onChange={(e) => setDecisionNote(e.target.value)} disabled={saving} maxLength={1000} />
            </FormField>
          </form>
        </Modal>
      )}

      {cancelTarget && (
        <ConfirmDialog
          title={t('absences.tab.cancelTitle')}
          message={t('absences.tab.cancelMessage', {
            type: t(`absences.type.${cancelTarget.type}`),
            from: cancelTarget.startDate,
            to: cancelTarget.endDate,
          })}
          confirmLabel={t('absences.tab.cancelConfirm')}
          destructive
          onConfirm={handleCancel}
          onCancel={() => setCancelTarget(null)}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('absences.tab.deleteTitle')}
          message={t('absences.tab.deleteMessage', {
            type: t(`absences.type.${deleteTarget.type}`),
            from: deleteTarget.startDate,
            to: deleteTarget.endDate,
          })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
