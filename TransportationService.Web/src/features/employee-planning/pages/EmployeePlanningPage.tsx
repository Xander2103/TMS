import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { ApiError } from '../../../api/apiClient'
import { formatDurationMinutes } from '../../../utils/dates'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import { ScheduleChip, ScheduleLegend } from '../components/ScheduleChip'
import {
  changeShiftStatus,
  copyWeek,
  createShift,
  deleteShift,
  getSchedule,
  getShift,
  updateShift,
} from '../api/employeePlanningApi'
import {
  SHIFT_STATUS_LABELS,
  SHIFT_TYPE_LABELS,
  mondayOf,
  toIsoDate,
  type ScheduleEntry,
  type ScheduleGrid,
  type Shift,
  type ShiftStatus,
  type ShiftType,
} from '../types'
import '../components/employee-planning.css'

/** Vertaalsleutels per weekdag (maandag eerst). */
const DAY_KEYS = [
  'employeePlanning.days.mon',
  'employeePlanning.days.tue',
  'employeePlanning.days.wed',
  'employeePlanning.days.thu',
  'employeePlanning.days.fri',
  'employeePlanning.days.sat',
  'employeePlanning.days.sun',
]

interface DialogState {
  mode: 'create' | 'edit'
  employeeId: string
  employeeName: string
  shift?: Shift
  date: string
}

/** Personnel planning grid: employees × days, chips per state, shift dialog, copy week. */
export function EmployeePlanningPage() {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const navigate = useNavigate()
  const canManage = hasPermission('employee_planning.manage')
  const canViewTrips = hasPermission('planning.view')
  const canViewEmployees = hasPermission('employees.view')

  const [weekStart, setWeekStart] = useState(() => toIsoDate(mondayOf(new Date())))
  const [weeksCount, setWeeksCount] = useState(1)
  const [departmentId, setDepartmentId] = useState<string | null>(null)
  const [nameFilter, setNameFilter] = useState('')
  const [listView, setListView] = useState(false)

  // Request-key idiom: loading derives from whether the loaded grid matches the current request.
  const [gridState, setGridState] = useState<{ grid: ScheduleGrid | null; loadedKey: string }>({
    grid: null,
    loadedKey: '',
  })
  const [reloadToken, setReloadToken] = useState(0)

  const [dialog, setDialog] = useState<DialogState | null>(null)
  const [startTime, setStartTime] = useState('08:00')
  const [endTime, setEndTime] = useState('16:30')
  const [breakMinutes, setBreakMinutes] = useState('30')
  const [shiftType, setShiftType] = useState<ShiftType>('Work')
  const [workLocation, setWorkLocation] = useState('')
  const [roleLabel, setRoleLabel] = useState('')
  const [notes, setNotes] = useState('')
  const [dialogDate, setDialogDate] = useState('')
  const [busy, setBusy] = useState(false)
  const [confirmDeleteShift, setConfirmDeleteShift] = useState(false)
  // 409 conflict flow: blocking conflicts from the server + the permission-gated override choice.
  const [conflictInfo, setConflictInfo] = useState<string[] | null>(null)
  const [overrideConflicts, setOverrideConflicts] = useState(false)
  const canOverrideConflicts = hasPermission('employee_planning.conflict_override')

  const [copyOpen, setCopyOpen] = useState(false)
  const [copyTarget, setCopyTarget] = useState('')

  const from = weekStart
  const to = toIsoDate(new Date(new Date(`${weekStart}T00:00:00`).getTime() + (weeksCount * 7 - 1) * 86_400_000))
  const requestKey = JSON.stringify({ from, to, departmentId, reloadToken })

  useEffect(() => {
    let mounted = true
    getSchedule(from, to, departmentId ?? undefined)
      .then((data) => {
        if (mounted) setGridState({ grid: data, loadedKey: requestKey })
      })
      .catch(() => {
        if (!mounted) return
        setGridState((current) => ({ ...current, loadedKey: requestKey }))
        showError(t('employeePlanning.page.loadFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  const grid = gridState.grid
  const loading = gridState.loadedKey !== requestKey

  function shiftWeek(deltaWeeks: number) {
    const date = new Date(`${weekStart}T00:00:00`)
    date.setDate(date.getDate() + deltaWeeks * 7)
    setWeekStart(toIsoDate(date))
  }

  function openCreate(employeeId: string, employeeName: string, date: string) {
    setDialog({ mode: 'create', employeeId, employeeName, date })
    setDialogDate(date)
    setStartTime('08:00')
    setEndTime('16:30')
    setBreakMinutes('30')
    setShiftType('Work')
    setWorkLocation('')
    setRoleLabel('')
    setNotes('')
    setConflictInfo(null)
    setOverrideConflicts(false)
  }

  async function openEdit(shiftId: string, employeeName: string) {
    try {
      const shift = await getShift(shiftId)
      setDialog({ mode: 'edit', employeeId: shift.employeeId, employeeName, shift, date: shift.date })
      setDialogDate(shift.date)
      setStartTime(shift.startTime.slice(0, 5))
      setEndTime(shift.endTime.slice(0, 5))
      setBreakMinutes(String(shift.breakMinutes))
      setShiftType(shift.type)
      setWorkLocation(shift.workLocation ?? '')
      setRoleLabel(shift.roleLabel ?? '')
      setNotes(shift.notes ?? '')
      setConflictInfo(null)
      setOverrideConflicts(false)
    } catch {
      showError(t('employeePlanning.page.shiftLoadFailed'))
    }
  }

  /**
   * Click behaviour per source: trip chips deep-link to the trip, absence chips to the
   * employee's absence tab, shift chips open the dialog (read-only without manage rights).
   */
  function chipAction(entry: ScheduleEntry, employeeName: string, employeeId: string): (() => void) | undefined {
    if (entry.sourceType === 'Trip') {
      return canViewTrips && entry.tripId ? () => navigate(`/planning/${entry.tripId}`) : undefined
    }
    if (entry.sourceType === 'Absence') {
      return canViewEmployees && entry.absenceId
        ? () => navigate(`/employees/${employeeId}?tab=verlof&absenceId=${entry.absenceId}`)
        : undefined
    }
    if (entry.shiftId) {
      return () => void openEdit(entry.shiftId!, employeeName)
    }
    return undefined
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!dialog) return
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
      setDialog(null)
      setReloadToken((token) => token + 1)
    } catch (err) {
      const body = err instanceof ApiError ? (err.body as { conflicts?: unknown } | undefined) : undefined
      if (err instanceof ApiError && err.status === 409 && Array.isArray(body?.conflicts)) {
        // Blocking schedule conflicts: show them in the dialog; overriding stays an explicit choice.
        setConflictInfo(body.conflicts.filter((c): c is string => typeof c === 'string'))
      } else {
        showError(err instanceof ApiError ? err.message : t('employeePlanning.dialog.saveFailed'))
      }
    } finally {
      setBusy(false)
    }
  }

  async function handleStatus(status: ShiftStatus) {
    if (!dialog?.shift) return
    setBusy(true)
    try {
      const updated = await changeShiftStatus(dialog.shift.id, status)
      setDialog({ ...dialog, shift: updated })
      showSuccess(t('employeePlanning.dialog.statusNow', { status: t(SHIFT_STATUS_LABELS[status]) }))
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('employeePlanning.dialog.statusFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!dialog?.shift) return
    setBusy(true)
    try {
      await deleteShift(dialog.shift.id)
      showSuccess(t('employeePlanning.dialog.deleted'))
      setConfirmDeleteShift(false)
      setDialog(null)
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('employeePlanning.dialog.deleteFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function handleCopyWeek(event: FormEvent) {
    event.preventDefault()
    if (!copyTarget) {
      showError(t('employeePlanning.copy.targetRequired'))
      return
    }
    setBusy(true)
    try {
      const result = await copyWeek(weekStart, copyTarget, undefined, departmentId ?? undefined)
      showSuccess(t('employeePlanning.copy.success', { count: result.copiedCount, skipped: result.skippedCount }))
      setCopyOpen(false)
      setWeekStart(copyTarget)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('employeePlanning.copy.failed'))
    } finally {
      setBusy(false)
    }
  }

  const days: string[] = []
  for (let i = 0; i < weeksCount * 7; i += 1) {
    days.push(toIsoDate(new Date(new Date(`${weekStart}T00:00:00`).getTime() + i * 86_400_000)))
  }
  const today = toIsoDate(new Date())
  const rows = (grid?.rows ?? []).filter((row) =>
    row.employeeName.toLowerCase().includes(nameFilter.trim().toLowerCase()),
  )

  return (
    <div>
      <Breadcrumbs items={[{ label: t('employeePlanning.page.breadcrumb') }]} />
      <PageHeader
        title={t('employeePlanning.page.title')}
        subtitle={t('employeePlanning.page.subtitle')}
        action={
          canManage ? (
            <Button
              variant="secondary"
              onClick={() => {
                const next = new Date(`${weekStart}T00:00:00`)
                next.setDate(next.getDate() + 7)
                setCopyTarget(toIsoDate(next))
                setCopyOpen(true)
              }}
            >
              {t('employeePlanning.page.copyWeek')}
            </Button>
          ) : undefined
        }
      />

      <div className="ep-toolbar">
        <span className="ep-week-nav">
          <button type="button" onClick={() => shiftWeek(-1)} aria-label={t('employeePlanning.page.prevWeek')}>
            ‹
          </button>
          <input
            type="date"
            value={weekStart}
            onChange={(e) => e.target.value && setWeekStart(toIsoDate(mondayOf(new Date(`${e.target.value}T00:00:00`))))}
            aria-label={t('employeePlanning.page.weekStart')}
          />
          <button type="button" onClick={() => shiftWeek(1)} aria-label={t('employeePlanning.page.nextWeek')}>
            ›
          </button>
        </span>
        <label>
          {t('employeePlanning.page.period')}{' '}
          <select value={weeksCount} onChange={(e) => setWeeksCount(Number(e.target.value))}>
            <option value={1}>{t('employeePlanning.page.weeks', { count: 1 })}</option>
            <option value={2}>{t('employeePlanning.page.weeks', { count: 2 })}</option>
            <option value={4}>{t('employeePlanning.page.weeks', { count: 4 })}</option>
          </select>
        </label>
        <span style={{ minWidth: 220 }}>
          <LookupSelect
            basePath="/api/departments"
            managePermission="departments.manage"
            singular="afdeling"
            value={departmentId}
            onChange={setDepartmentId}
            placeholder={t('employeePlanning.page.allDepartments')}
          />
        </span>
        <input
          value={nameFilter}
          onChange={(e) => setNameFilter(e.target.value)}
          placeholder={t('employeePlanning.page.nameFilterPlaceholder')}
          aria-label={t('employeePlanning.page.nameFilterAria')}
        />
        <label>
          <input type="checkbox" checked={listView} onChange={(e) => setListView(e.target.checked)} /> {t('employeePlanning.page.listView')}
        </label>
      </div>

      <ScheduleLegend />

      {loading && <p className="portal-empty">{t('employeePlanning.page.loading')}</p>}

      {!listView && (
        <div className="ep-grid-wrap">
          <table className="ep-grid">
            <thead>
              <tr>
                <th className="ep-employee-cell">{t('employeePlanning.page.colEmployee')}</th>
                {days.map((date) => {
                  const dayIndex = (new Date(`${date}T00:00:00`).getDay() + 6) % 7
                  return (
                    <th key={date} className={`${dayIndex >= 5 ? 'ep-weekend' : ''} ${date === today ? 'ep-today' : ''}`}>
                      {t(DAY_KEYS[dayIndex])} {date.slice(8, 10)}/{date.slice(5, 7)}
                    </th>
                  )
                })}
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.employeeId}>
                  <td className="ep-employee-cell">
                    <div className="ep-employee-name">
                      {canViewEmployees ? (
                        <Link to={`/employees/${row.employeeId}`} className="ep-employee-link">
                          {row.employeeName}
                        </Link>
                      ) : (
                        row.employeeName
                      )}
                    </div>
                    <div className="ep-employee-meta">
                      {row.departmentName ?? '—'} ·{' '}
                      {t('employeePlanning.page.plannedSuffix', { duration: formatDurationMinutes(row.plannedMinutes) })}
                    </div>
                  </td>
                  {row.days.map((day) => {
                    const dayIndex = (new Date(`${day.date}T00:00:00`).getDay() + 6) % 7
                    return (
                      <td key={day.date} className={`ep-day-cell ${dayIndex >= 5 ? 'ep-weekend' : ''}`}>
                        <div className="ep-day-cell-inner">
                          {day.entries.map((entry, index) => (
                            <ScheduleChip
                              key={index}
                              entry={entry}
                              onClick={chipAction(entry, row.employeeName, row.employeeId)}
                            />
                          ))}
                          {canManage && (
                            <button
                              type="button"
                              className="ep-add-shift"
                              onClick={() => openCreate(row.employeeId, row.employeeName, day.date)}
                            >
                              {t('employeePlanning.page.addShift')}
                            </button>
                          )}
                        </div>
                      </td>
                    )
                  })}
                </tr>
              ))}
              {rows.length === 0 && !loading && (
                <tr>
                  <td colSpan={days.length + 1}>{t('employeePlanning.page.emptyFiltered')}</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {listView && (
        <table className="to-stops-table">
          <thead>
            <tr>
              <th>{t('employeePlanning.page.colEmployee')}</th>
              <th>{t('employeePlanning.page.colDate')}</th>
              <th>{t('employeePlanning.page.colStatus')}</th>
              <th>{t('employeePlanning.page.colTime')}</th>
              <th>{t('employeePlanning.page.colLocation')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.flatMap((row) =>
              row.days.flatMap((day) =>
                day.entries.map((entry, index) => (
                  <tr key={`${row.employeeId}-${day.date}-${index}`}>
                    <td>
                      {canViewEmployees ? (
                        <Link to={`/employees/${row.employeeId}`} className="ep-employee-link">
                          {row.employeeName}
                        </Link>
                      ) : (
                        row.employeeName
                      )}
                    </td>
                    <td>{day.date}</td>
                    <td>
                      <ScheduleChip entry={entry} onClick={chipAction(entry, row.employeeName, row.employeeId)} />
                    </td>
                    <td>
                      {entry.startTime && entry.endTime
                        ? `${entry.startTime.slice(0, 5)}–${entry.endTime.slice(0, 5)}`
                        : '—'}
                    </td>
                    <td>{entry.workLocation ?? '—'}</td>
                  </tr>
                )),
              ),
            )}
          </tbody>
        </table>
      )}

      {dialog && (
        <Modal
          title={
            dialog.mode === 'create'
              ? t('employeePlanning.dialog.createTitle', { name: dialog.employeeName })
              : t('employeePlanning.dialog.editTitle', { name: dialog.employeeName })
          }
          onClose={() => setDialog(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDialog(null)} disabled={busy}>
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
                  <Button variant="danger" onClick={() => setConfirmDeleteShift(true)} disabled={busy}>
                    {t('ui.actions.delete')}
                  </Button>
                )}
              </div>
            )}
            <FormField label={t('employeePlanning.dialog.date')} htmlFor="ep-date" required>
              <input id="ep-date" type="date" value={dialogDate} onChange={(e) => setDialogDate(e.target.value)} disabled={busy || !canManage} />
            </FormField>
            <div className="ep-form-row">
              <FormField label={t('employeePlanning.dialog.from')} htmlFor="ep-start" required>
                <input id="ep-start" type="time" value={startTime} onChange={(e) => setStartTime(e.target.value)} disabled={busy || !canManage} />
              </FormField>
              <FormField label={t('employeePlanning.dialog.to')} htmlFor="ep-end" required>
                <input id="ep-end" type="time" value={endTime} onChange={(e) => setEndTime(e.target.value)} disabled={busy || !canManage} />
              </FormField>
            </div>
            <div className="ep-form-row">
              <FormField label={t('employeePlanning.dialog.breakMinutes')} htmlFor="ep-break">
                <input id="ep-break" type="number" min={0} value={breakMinutes} onChange={(e) => setBreakMinutes(e.target.value)} disabled={busy || !canManage} />
              </FormField>
              <FormField label={t('employeePlanning.dialog.type')} htmlFor="ep-type">
                <select id="ep-type" value={shiftType} onChange={(e) => setShiftType(e.target.value as ShiftType)} disabled={busy || !canManage}>
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
                <input id="ep-location" value={workLocation} onChange={(e) => setWorkLocation(e.target.value)} disabled={busy || !canManage} maxLength={200} />
              </FormField>
              <FormField label={t('employeePlanning.dialog.role')} htmlFor="ep-role">
                <input id="ep-role" value={roleLabel} onChange={(e) => setRoleLabel(e.target.value)} disabled={busy || !canManage} maxLength={100} />
              </FormField>
            </div>
            <FormField label={t('employeePlanning.dialog.notes')} htmlFor="ep-notes">
              <textarea id="ep-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={busy || !canManage} maxLength={1000} />
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
                    <input
                      type="checkbox"
                      checked={overrideConflicts}
                      onChange={(e) => setOverrideConflicts(e.target.checked)}
                      disabled={busy}
                    />
                    {t('employeePlanning.dialog.override')}
                  </label>
                ) : (
                  <p>{t('employeePlanning.dialog.noOverridePermission')}</p>
                )}
              </div>
            )}
          </form>
        </Modal>
      )}

      {confirmDeleteShift && dialog?.shift && (
        <ConfirmDialog
          title={t('employeePlanning.dialog.deleteTitle')}
          message={t('employeePlanning.dialog.deleteMessage', { name: dialog.employeeName, date: dialog.shift.date })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={busy}
          onConfirm={() => void handleDelete()}
          onCancel={() => setConfirmDeleteShift(false)}
        />
      )}

      {copyOpen && (
        <Modal
          title={t('employeePlanning.copy.title', { week: weekStart })}
          onClose={() => setCopyOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCopyOpen(false)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="ep-copy-form" disabled={busy}>
                {busy ? t('employeePlanning.dialog.busy') : t('employeePlanning.copy.copy')}
              </Button>
            </>
          }
        >
          <form id="ep-copy-form" className="ep-form" onSubmit={handleCopyWeek} noValidate>
            <FormField
              label={t('employeePlanning.copy.targetLabel')}
              htmlFor="ep-copy-target"
              required
              hint={t('employeePlanning.copy.targetHint')}
            >
              <input
                id="ep-copy-target"
                type="date"
                value={copyTarget}
                onChange={(e) => e.target.value && setCopyTarget(toIsoDate(mondayOf(new Date(`${e.target.value}T00:00:00`))))}
                disabled={busy}
              />
            </FormField>
          </form>
        </Modal>
      )}
    </div>
  )
}
