import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import { useAuth } from '../../auth/authContextValue'
import { MonthGrid } from '../../../components/calendar/MonthGrid'
import { WeekGrid } from '../../../components/calendar/WeekGrid'
import { CalendarToolbar, type CalendarViewMode } from '../../../components/calendar/CalendarToolbar'
import { DAY_NAMES, dayIndexMonday, monthGridRange } from '../../../components/calendar/dateUtils'
import '../../../components/calendar/calendar.css'
import { ScheduleChip, ScheduleLegend } from '../../employee-planning/components/ScheduleChip'
import { mondayOf, toIsoDate, type ScheduleDay, type ScheduleEntry } from '../../employee-planning/types'
import { PersonalNoteDialog } from '../components/PersonalNoteDialog'
import type { PersonalCalendarNote } from '../api/calendarNotesApi'
import './portal.css'

function addDays(iso: string, days: number): string {
  return toIsoDate(new Date(new Date(`${iso}T00:00:00`).getTime() + days * 86_400_000))
}

/**
 * Personal planning as a real calendar: week view (days side by side), month view (shared
 * calendar grid) and the original agenda list — plus an iCalendar export for Google/Outlook
 * import.
 */
export function PortalPlanningPage() {
  const navigate = useNavigate()
  const toast = useToast()
  const { hasPermission } = useAuth()

  const [view, setView] = useState<CalendarViewMode>('week')
  const [anchor, setAnchor] = useState(() => toIsoDate(mondayOf(new Date())))
  const [loadedDays, setLoadedDays] = useState<{ key: string; days: ScheduleDay[] } | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)
  const [noteDialog, setNoteDialog] = useState<{ note: PersonalCalendarNote | null; date: string } | null>(null)

  // Visible range per view: week = 7 days from the anchor's Monday; month = the full padded
  // grid MonthGrid renders (leading/trailing weeks from neighbouring months included, so their
  // dimmed pad cells aren't silently empty); list = 14 days.
  // `anchor` isn't guaranteed Monday-aligned for week view (e.g. after paging months while in
  // month view and then switching to week), and WeekGrid always renders `mondayOf(anchor)`, so
  // the fetch must be derived the same way or days would render empty.
  const anchorDate = new Date(`${anchor}T00:00:00`)
  const monthRange = view === 'month' ? monthGridRange(anchorDate) : null
  const weekStart = view === 'week' ? mondayOf(anchorDate) : null
  const from = monthRange ? toIsoDate(monthRange.start) : weekStart ? toIsoDate(weekStart) : anchor
  const to = monthRange
    ? toIsoDate(monthRange.end)
    : weekStart
      ? addDays(toIsoDate(weekStart), 6)
      : addDays(anchor, 13)

  // Loading is derived from a request key so no setState runs synchronously in the effect.
  const requestKey = `${from}|${to}|${reloadToken}`
  const days = loadedDays?.key === requestKey ? loadedDays.days : null

  useEffect(() => {
    let mounted = true
    apiClient
      .getJson<ScheduleDay[]>(`/api/me/planning?from=${from}&to=${to}`)
      .then((data) => {
        if (mounted) setLoadedDays({ key: `${from}|${to}|${reloadToken}`, days: data })
      })
      .catch(() => {
        if (mounted) setLoadError('Je planning kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [from, to, reloadToken])

  const entriesByDate = useMemo(() => new Map((days ?? []).map((day) => [day.date, day.entries])), [days])

  function chipAction(entry: ScheduleEntry, date: string): (() => void) | undefined {
    if (entry.noteId) {
      return () =>
        setNoteDialog({
          date,
          note: {
            id: entry.noteId!,
            title: entry.label,
            description: entry.statusLabel,
            date,
            startTime: entry.startTime,
            endTime: entry.endTime,
            allDay: !entry.startTime,
            colour: entry.colour ?? '#2563eb',
          },
        })
    }
    if (entry.tripId && hasPermission('driver_workflow.view')) {
      return () => navigate(`/my-trips/${entry.tripId}`)
    }
    return undefined
  }

  function selectMonthDate(iso: string) {
    setView('week')
    setAnchor(toIsoDate(mondayOf(new Date(`${iso}T00:00:00`))))
  }

  async function downloadIcs() {
    try {
      const response = await fetch(`${apiBaseUrl}/api/me/planning/ics?from=${from}&to=${to}`, {
        headers: { Authorization: `Bearer ${getAccessToken()}` },
      })
      if (!response.ok) throw new Error()
      const blob = await response.blob()
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = 'planning.ics'
      link.click()
      URL.revokeObjectURL(url)
    } catch {
      toast.showError('De agenda-export kon niet worden gedownload.')
    }
  }

  if (loadError) return <ErrorState message={loadError} />

  const today = toIsoDate(new Date())

  return (
    <div>
      <PageHeader
        title="Mijn planning"
        subtitle="Je shifts, ritten, opleidingen en afwezigheden."
        action={
          <div className="portal-planning-actions">
            <Button onClick={() => setNoteDialog({ note: null, date: today })}>+ Notitie</Button>
            <Button variant="secondary" onClick={() => void downloadIcs()}>
              Agenda-export (.ics)
            </Button>
          </div>
        }
      />

      <CalendarToolbar
        anchor={anchorDate}
        view={view}
        onViewChange={setView}
        onNavigate={(next) => setAnchor(toIsoDate(next))}
      />

      {!days && <LoadingState message="Planning laden..." />}

      {days && view === 'week' && (
        <WeekGrid
          anchor={anchorDate}
          entriesByDate={entriesByDate}
          renderEntry={(entry, ctx) => <ScheduleChip entry={entry} onClick={chipAction(entry, ctx.date)} />}
        />
      )}

      {days && view === 'month' && (
        <MonthGrid
          anchor={anchorDate}
          entriesByDate={entriesByDate}
          onSelectDate={selectMonthDate}
          renderEntry={(entry) => <ScheduleChip entry={entry} compact />}
        />
      )}

      {days && view === 'list' && (
        <ul className="cal-list">
          {days.map((day) => {
            const dayIndex = dayIndexMonday(new Date(`${day.date}T00:00:00`))
            return (
              <li key={day.date} className={`cal-list-day ${day.date === today ? 'cal-today' : ''}`}>
                <div className="cal-list-date">
                  {DAY_NAMES[dayIndex]} {day.date.slice(8, 10)}/{day.date.slice(5, 7)}
                  {day.date === today && ' · vandaag'}
                </div>
                <div className="cal-list-entries">
                  {day.entries.length === 0 && <span className="cal-list-free">vrij</span>}
                  {day.entries.map((entry, index) => (
                    <ScheduleChip key={index} entry={entry} onClick={chipAction(entry, day.date)} />
                  ))}
                </div>
              </li>
            )
          })}
        </ul>
      )}

      {days && <ScheduleLegend />}

      {noteDialog && (
        <PersonalNoteDialog
          note={noteDialog.note}
          initialDate={noteDialog.date}
          onClose={() => setNoteDialog(null)}
          onSaved={() => {
            setNoteDialog(null)
            setReloadToken((t) => t + 1)
          }}
        />
      )}
    </div>
  )
}
