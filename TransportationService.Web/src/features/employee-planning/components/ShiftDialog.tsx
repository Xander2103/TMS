import { useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { useLocale } from '../../../i18n/localeContext'
import { changeShiftStatus, createShift, deleteShift, updateShift } from '../api/employeePlanningApi'
import { SHIFT_STATUS_LABELS, SHIFT_TYPE_LABELS, type Shift, type ShiftStatus, type ShiftType } from '../types'

export interface ShiftDialogState {
  mode: 'create' | 'edit'
  employeeId: string
  employeeName: string
  shift?: Shift
  date: string
}

interface ShiftDialogProps {
  dialog: ShiftDialogState
  canManage: boolean
  canOverrideConflicts: boolean
  onClose: () => void
  /** Called after any successful mutation so the grid reloads. */
  onChanged: () => void
}

/**
 * Create/edit dialog of one shift, including the status flow, delete and the 409 conflict
 * override. Behaviour is unchanged from the pre-wave page; it only moved into its own component.
 */
export function ShiftDialog({ dialog: initial, canManage, canOverrideConflicts, onClose, onChanged }: ShiftDialogProps) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [dialog, setDialog] = useState(initial)
  const shift = dialog.shift
  const [dialogDate, setDialogDate] = useState(initial.date)
  const [startTime, setStartTime] = useState(shift ? shift.startTime.slice(0, 5) : '08:00')
  const [endTime, setEndTime] = useState(shift ? shift.endTime.slice(0, 5) : '16:30')
  const [breakMinutes, setBreakMinutes] = useState(shift ? String(shift.breakMinutes) : '30')
  const [shiftType, setShiftType] = useState<ShiftType>(shift?.type ?? 'Work')
  const [workLocation, setWorkLocation] = useState(shift?.workLocation ?? '')
  const [roleLabel, setRoleLabel] = useState(shift?.roleLabel ?? '')
  const [notes, setNotes] = useState(shift?.notes ?? '')
  const [busy, setBusy] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  // 409 conflict flow: blocking conflicts from the server + the permission-gated override choice.
  const [conflictInfo, setConflictInfo] = useState<string[] | null>(null)
  const [overrideConflicts, setOverrideConflicts] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!dialogDate || !startTime || !endTime) {
      showError(t('employeePlanning.dialog.requiredFields'))
      return
    }
    setBusy(true)
    try {
      const payload = {
        date: dialogDate,
        startTime: `${startTime}:00`,
        endTime: `${endTime}:00`,
        breakMinutes: Number(breakMinutes || '0'),
        type: shiftType,
        workLocation: workLocation.trim() || null,
        roleLabel: roleLabel.trim() || null,
        notes: notes.trim() || null,
        override: overrideConflicts,
      }
      if (dialog.mode === 'create') {
        await createShift({ employeeId: dialog.employeeId, ...payload })
        showSuccess(t('employeePlanning.dialog.created'))
      } else {
        await updateShift(dialog.shift!.id, payload)
        showSuccess(t('employeePlanning.dialog.updated'))
      }
      onChanged()
      onClose()
    } catch (err) {
      const body = err instanceof ApiError ? (err.body as { conflicts?: unknown } | undefined) : undefined
      if (err instanceof ApiError && err.status === 409 && Array.isArray(body?.conflicts)) {
        setConflictInfo(body.conflicts.filter((c): c is string => typeof c === 'string'))
      } else {
        showError(err instanceof ApiError ? err.message : t('employeePlanning.dialog.saveFailed'))
      }
    } finally {
      setBusy(false)
    }
  }

  async function handleStatus(status: ShiftStatus) {
    if (!dialog.shift) return
    setBusy(true)
    try {
      const updated = await changeShiftStatus(dialog.shift.id, status)
      setDialog({ ...dialog, shift: updated })
      showSuccess(t('employeePlanning.dialog.statusNow', { status: t(SHIFT_STATUS_LABELS[status]) }))
      onChanged()
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('employeePlanning.dialog.statusFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!dialog.shift) return
    setBusy(true)
    try {
      await deleteShift(dialog.shift.id)
      showSuccess(t('employeePlanning.dialog.deleted'))
      setConfirmDelete(false)
      onChanged()
      onClose()
    } catch {
      showError(t('employeePlanning.dialog.deleteFailed'))
    } finally {
      setBusy(false)
    }
  }

  const disabled = busy || !canManage

  return (
    <>
      <Modal
        title={
          dialog.mode === 'create'
            ? t('employeePlanning.dialog.createTitle', { name: dialog.employeeName })
            : t('employeePlanning.dialog.editTitle', { name: dialog.employeeName })
        }
        onClose={onClose}
        busy={busy}
        footer={
          <>
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              {canManage ? t('ui.actions.cancel') : t('ui.actions.close')}
            </Button>
            {canManage && (
              <Button type="submit" form="ep-shift-form" disabled={busy}>
                {busy ? t('employeePlanning.dialog.busy') : t('ui.actions.save')}
              </Button>
            )}
          </>
        }
      >
        <form id="ep-shift-form" className="ep-form" onSubmit={handleSubmit} noValidate>
          {dialog.mode === 'edit' && dialog.shift && (
            <div className="ep-form-actions-extra">
              <Badge tone={dialog.shift.status === 'Confirmed' ? 'success' : dialog.shift.status === 'Planned' ? 'info' : 'neutral'}>
                {t(SHIFT_STATUS_LABELS[dialog.shift.status])}
              </Badge>
              {canManage && dialog.shift.status === 'Draft' && (
                <Button variant="secondary" onClick={() => void handleStatus('Planned')} disabled={busy}>
                  {t('employeePlanning.dialog.toPlanned')}
                </Button>
              )}
              {canManage && dialog.shift.status === 'Planned' && (
                <>
                  <Button variant="secondary" onClick={() => void handleStatus('Confirmed')} disabled={busy}>
                    {t('employeePlanning.dialog.toConfirmed')}
                  </Button>
                  <Button variant="ghost" onClick={() => void handleStatus('Draft')} disabled={busy}>
                    {t('employeePlanning.dialog.toDraft')}
                  </Button>
                </>
              )}
              {canManage && dialog.shift.status === 'Confirmed' && (
                <Button variant="ghost" onClick={() => void handleStatus('Planned')} disabled={busy}>
                  {t('employeePlanning.dialog.backToPlanned')}
                </Button>
              )}
              {canManage && (
                <Button variant="danger" onClick={() => setConfirmDelete(true)} disabled={busy}>
                  {t('ui.actions.delete')}
                </Button>
              )}
            </div>
          )}
          <FormField label={t('employeePlanning.dialog.date')} htmlFor="ep-date" required>
            <input id="ep-date" type="date" value={dialogDate} onChange={(e) => setDialogDate(e.target.value)} disabled={disabled} />
          </FormField>
          <div className="ep-form-row">
            <FormField label={t('employeePlanning.dialog.from')} htmlFor="ep-start" required>
              <input id="ep-start" type="time" value={startTime} onChange={(e) => setStartTime(e.target.value)} disabled={disabled} />
            </FormField>
            <FormField label={t('employeePlanning.dialog.to')} htmlFor="ep-end" required>
              <input id="ep-end" type="time" value={endTime} onChange={(e) => setEndTime(e.target.value)} disabled={disabled} />
            </FormField>
          </div>
          <div className="ep-form-row">
            <FormField label={t('employeePlanning.dialog.breakMinutes')} htmlFor="ep-break">
              <input id="ep-break" type="number" min={0} value={breakMinutes} onChange={(e) => setBreakMinutes(e.target.value)} disabled={disabled} />
            </FormField>
            <FormField label={t('employeePlanning.dialog.type')} htmlFor="ep-type">
              <select id="ep-type" value={shiftType} onChange={(e) => setShiftType(e.target.value as ShiftType)} disabled={disabled}>
                {(Object.keys(SHIFT_TYPE_LABELS) as ShiftType[]).map((type) => (
                  <option key={type} value={type}>
                    {t(SHIFT_TYPE_LABELS[type])}
                  </option>
                ))}
              </select>
            </FormField>
          </div>
          <div className="ep-form-row">
            <FormField label={t('employeePlanning.dialog.workLocation')} htmlFor="ep-location">
              <input id="ep-location" value={workLocation} onChange={(e) => setWorkLocation(e.target.value)} disabled={disabled} maxLength={200} />
            </FormField>
            <FormField label={t('employeePlanning.dialog.role')} htmlFor="ep-role">
              <input id="ep-role" value={roleLabel} onChange={(e) => setRoleLabel(e.target.value)} disabled={disabled} maxLength={100} />
            </FormField>
          </div>
          <FormField label={t('employeePlanning.dialog.notes')} htmlFor="ep-notes">
            <textarea id="ep-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={disabled} maxLength={1000} />
          </FormField>
          {conflictInfo && (
            <div className="ep-conflict-panel" role="alert">
              <strong>{t('employeePlanning.dialog.conflictsTitle')}</strong>
              <ul>
                {conflictInfo.map((conflict, index) => (
                  <li key={index}>{conflict}</li>
                ))}
              </ul>
              {canOverrideConflicts ? (
                <label className="ep-conflict-override">
                  <input type="checkbox" checked={overrideConflicts} onChange={(e) => setOverrideConflicts(e.target.checked)} disabled={busy} />
                  {t('employeePlanning.dialog.override')}
                </label>
              ) : (
                <p>{t('employeePlanning.dialog.noOverridePermission')}</p>
              )}
            </div>
          )}
        </form>
      </Modal>

      {confirmDelete && dialog.shift && (
        <ConfirmDialog
          title={t('employeePlanning.dialog.deleteTitle')}
          message={t('employeePlanning.dialog.deleteMessage', { name: dialog.employeeName, date: dialog.shift.date })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={busy}
          onConfirm={() => void handleDelete()}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </>
  )
}
