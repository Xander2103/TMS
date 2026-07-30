import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Modal } from '../../../components/ui/Modal'
import { MonthGrid } from '../../../components/calendar/MonthGrid'
import { WeekGrid } from '../../../components/calendar/WeekGrid'
import { CalendarToolbar, type CalendarViewMode } from '../../../components/calendar/CalendarToolbar'
import { DAY_NAMES, addDays, dayIndexMonday, monthGridRange, startOfMonth, toIsoDate } from '../../../components/calendar/dateUtils'
import '../../../components/calendar/calendar.css'
import { useAuth } from '../../auth/authContextValue'
import { ScheduleChip, ScheduleLegend } from '../../employee-planning/components/ScheduleChip'
import { getSchedule } from '../../employee-planning/api/employeePlanningApi'
import { SCHEDULE_STATE_LABELS, chipDescription, mondayOf, type ScheduleDay, type ScheduleEntry } from '../../employee-planning/types'

const VIEW_STORAGE_KEY = 'ts.employeePlanning.view'
const VALID_VIEWS: CalendarViewMode[] = ['month', 'week', 'list']
const LIST_WINDOW_DAYS = 28

function readStoredView(): CalendarViewMode {
  try {
    const raw = window.localStorage.getItem(VIEW_STORAGE_KEY)
    if (raw && (VALID_VIEWS as string[]).includes(raw)) return raw as CalendarViewMode
  } catch {
    /* localStorage unavailable (private mode, disabled, ...) — fall back to the default view */
  }
  return 'month'
}

function storeView(view: CalendarViewMode) {
  try {
    window.localStorage.setItem(VIEW_STORAGE_KEY, view)
  } catch {
    /* storage full/unavailable — view choice just won't persist */
  }
}

/** Absences carry no start/end time; treat those (and any other untimed entry) as all-day. */
function isAllDay(entry: ScheduleEntry): boolean {
  return !entry.startTime && !entry.endTime
}

/**
 * Employee schedule as a real calendar: month/week grids share the primitives from
 * `components/calendar`, plus the original agenda list. Reuses the schedule read model
 * (shifts + absences + trip-generated entries incl. conflict markers). Clicking an entry opens
 * a detail popover with contextual actions (view trip / open the absence in the Verlof tab, and
 * a review hint when the viewer can approve leave).
 */
export function EmployeePlanningTab({ employeeId }: { employeeId: string }) {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const canViewTrips = hasPermission('planning.view')
  const canApprove = hasPermission('absences.approve')

  const [view, setViewState] = useState<CalendarViewMode>(() => readStoredView())
  // Seeded per the resolved initial view: month view should open on the current calendar month
  // even when today falls before that month's first Monday (e.g. today = Sat 1 Aug -> Mon 27
  // Jul would otherwise land the initial anchor in July).
  const [anchor, setAnchor] = useState(() =>
    toIsoDate(view === 'month' ? startOfMonth(new Date()) : mondayOf(new Date())),
  )
  const [state, setState] = useState<{ days: ScheduleDay[] | null; loadedKey: string }>({ days: null, loadedKey: '' })
  const [error, setError] = useState<string | null>(null)
  const [selected, setSelected] = useState<{ entry: ScheduleEntry; date: string } | null>(null)

  function setView(next: CalendarViewMode) {
    setViewState(next)
    storeView(next)
  }

  const anchorDate = new Date(`${anchor}T00:00:00`)
  const monthRange = view === 'month' ? monthGridRange(anchorDate) : null
  // Week view always fetches/renders the Monday..Sunday week *containing* the anchor, never the
  // raw anchor..anchor+6 range — `anchor` isn't guaranteed Monday-aligned (e.g. after paging
  // months in month view and then switching to week view), and WeekGrid itself always renders
  // `mondayOf(anchor)`, so the fetch must match that or days silently render empty.
  const weekStart = view === 'week' ? mondayOf(anchorDate) : null
  const from = monthRange ? toIsoDate(monthRange.start) : weekStart ? toIsoDate(weekStart) : anchor
  const to = monthRange
    ? toIsoDate(monthRange.end)
    : weekStart
      ? toIsoDate(addDays(weekStart, 6))
      : toIsoDate(addDays(anchorDate, LIST_WINDOW_DAYS - 1))
  const requestKey = `${from}|${to}|${employeeId}`

  useEffect(() => {
    let mounted = true
    getSchedule(from, to, undefined, employeeId)
      .then((grid) => {
        if (!mounted) return
        setState({ days: grid.rows[0]?.days ?? [], loadedKey: requestKey })
        setError(null)
      })
      .catch(() => {
        if (mounted) setError('De planning kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  const days = state.loadedKey === requestKey ? state.days : null
  const entriesByDate = useMemo(() => new Map((days ?? []).map((day) => [day.date, day.entries])), [days])

  if (error) return <p className="placeholder-text">{error}</p>

  const withEntries = (days ?? []).filter((day) => day.entries.length > 0)

  return (
    <div className="employee-planning-tab">
      <CalendarToolbar
        anchor={anchorDate}
        view={view}
        onViewChange={setView}
        onNavigate={(next) => setAnchor(toIsoDate(next))}
        listStepDays={LIST_WINDOW_DAYS}
        actions={<ScheduleLegend />}
      />

      {days === null && <p className="placeholder-text">Planning laden…</p>}

      {days !== null && view === 'month' && (
        <MonthGrid
          anchor={anchorDate}
          entriesByDate={entriesByDate}
          onSelectDate={(iso) => {
            setView('week')
            setAnchor(toIsoDate(mondayOf(new Date(`${iso}T00:00:00`))))
          }}
          renderEntry={(entry) => <ScheduleChip entry={entry} compact />}
        />
      )}

      {days !== null && view === 'week' && (
        <WeekGrid
          anchor={anchorDate}
          entriesByDate={entriesByDate}
          renderEntry={(entry, ctx) => (
            <ScheduleChip
              entry={entry}
              fullWidth={isAllDay(entry)}
              onClick={() => setSelected({ entry, date: ctx.date })}
            />
          )}
        />
      )}

      {days !== null && view === 'list' && withEntries.length === 0 && (
        <p className="placeholder-text">Geen planning in deze periode.</p>
      )}
      {days !== null && view === 'list' && withEntries.length > 0 && (
        <ul className="cal-list">
          {withEntries.map((day) => {
            const dayIndex = dayIndexMonday(new Date(`${day.date}T00:00:00`))
            return (
              <li key={day.date} className="cal-list-day">
                <div className="cal-list-date">
                  {DAY_NAMES[dayIndex]} {day.date}
                </div>
                <div className="cal-list-entries">
                  {day.entries.map((entry, index) => (
                    <ScheduleChip
                      key={index}
                      entry={entry}
                      fullWidth={isAllDay(entry)}
                      onClick={() => setSelected({ entry, date: day.date })}
                    />
                  ))}
                </div>
              </li>
            )
          })}
        </ul>
      )}

      {selected && (
        <EntryDetailModal
          entry={selected.entry}
          date={selected.date}
          employeeId={employeeId}
          canApprove={canApprove}
          canViewTrips={canViewTrips}
          onNavigateToTrip={(tripId) => navigate(`/planning/${tripId}`)}
          onNavigateToAbsence={(absenceId) => navigate(`/employees/${employeeId}?tab=verlof&absenceId=${absenceId}`)}
          onClose={() => setSelected(null)}
        />
      )}
    </div>
  )
}

function formatTimeRange(entry: ScheduleEntry): string | null {
  if (!entry.startTime || !entry.endTime) return null
  return `${entry.startTime.slice(0, 5)}–${entry.endTime.slice(0, 5)}`
}

interface EntryDetailModalProps {
  entry: ScheduleEntry
  date: string
  employeeId: string
  canApprove: boolean
  canViewTrips: boolean
  onNavigateToTrip: (tripId: string) => void
  onNavigateToAbsence: (absenceId: string) => void
  onClose: () => void
}

/** Entry detail popover: state, time range, linked context and conflicts, plus the deep links
 * an entry supports (a trip, or an absence — with a review hint for approvers). */
function EntryDetailModal({
  entry,
  date,
  canApprove,
  canViewTrips,
  onNavigateToTrip,
  onNavigateToAbsence,
  onClose,
}: EntryDetailModalProps) {
  const timeRange = formatTimeRange(entry)
  const isPendingApproval = entry.sourceType === 'Absence' && entry.state === 'LeaveRequested'

  return (
    <Modal title={entry.sourceType === 'Trip' ? `Rit ${entry.label}` : SCHEDULE_STATE_LABELS[entry.state]} onClose={onClose}>
      <dl className="ep-entry-detail">
        <dt>Datum</dt>
        <dd>{date}</dd>
        <dt>Status</dt>
        <dd>{SCHEDULE_STATE_LABELS[entry.state]}</dd>
        {timeRange && (
          <>
            <dt>Tijdstip</dt>
            <dd>{timeRange}</dd>
          </>
        )}
        {!timeRange && (
          <>
            <dt>Tijdstip</dt>
            <dd>Hele dag</dd>
          </>
        )}
        {entry.statusLabel && (
          <>
            <dt>Toelichting</dt>
            <dd>{entry.statusLabel}</dd>
          </>
        )}
        {entry.workLocation && (
          <>
            <dt>Locatie</dt>
            <dd>{entry.workLocation}</dd>
          </>
        )}
        {entry.vehicleSummary && (
          <>
            <dt>Voertuig</dt>
            <dd>{entry.vehicleSummary}</dd>
          </>
        )}
        {entry.conflictSeverity && (
          <>
            <dt>Conflict</dt>
            <dd>{chipDescription(entry)}</dd>
          </>
        )}
      </dl>

      {entry.sourceType === 'Trip' && entry.tripId && canViewTrips && (
        <button type="button" className="ep-entry-link" onClick={() => onNavigateToTrip(entry.tripId!)}>
          Bekijk rit →
        </button>
      )}

      {entry.sourceType === 'Absence' && entry.absenceId && (
        <>
          <button type="button" className="ep-entry-link" onClick={() => onNavigateToAbsence(entry.absenceId!)}>
            Naar verlof &amp; afwezigheden →
          </button>
          {isPendingApproval && canApprove && (
            <p className="ep-entry-approve-hint">
              Deze verlofaanvraag staat nog open ter goedkeuring. Open het verlofoverzicht hierboven om ze te
              beoordelen.
            </p>
          )}
        </>
      )}
    </Modal>
  )
}
