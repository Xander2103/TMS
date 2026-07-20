import { useEffect, useState } from 'react'
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
import { ScheduleChip, ScheduleLegend } from '../../employee-planning/components/ScheduleChip'
import { mondayOf, toIsoDate, type ScheduleDay, type ScheduleEntry } from '../../employee-planning/types'
import './portal.css'

const DAY_NAMES = ['ma', 'di', 'wo', 'do', 'vr', 'za', 'zo']
const MONTH_NAMES = [
  'januari', 'februari', 'maart', 'april', 'mei', 'juni',
  'juli', 'augustus', 'september', 'oktober', 'november', 'december',
]

type ViewMode = 'week' | 'month' | 'list'

function addDays(iso: string, days: number): string {
  return toIsoDate(new Date(new Date(`${iso}T00:00:00`).getTime() + days * 86_400_000))
}

/**
 * Personal planning as a real calendar: week view (days side by side), compact month view
 * and the original agenda list — plus an iCalendar export for Google/Outlook import.
 */
export function PortalPlanningPage() {
  const navigate = useNavigate()
  const toast = useToast()
  const { hasPermission } = useAuth()

  const [view, setView] = useState<ViewMode>('week')
  const [anchor, setAnchor] = useState(() => toIsoDate(mondayOf(new Date())))
  const [loadedDays, setLoadedDays] = useState<{ key: string; days: ScheduleDay[] } | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  // Visible range per view: week = 7 days from the anchor Monday; month = the anchor's
  // calendar month; list = 14 days.
  const anchorDate = new Date(`${anchor}T00:00:00`)
  const monthStart = toIsoDate(new Date(anchorDate.getFullYear(), anchorDate.getMonth(), 1))
  const monthEnd = toIsoDate(new Date(anchorDate.getFullYear(), anchorDate.getMonth() + 1, 0))
  const from = view === 'month' ? monthStart : anchor
  const to = view === 'month' ? monthEnd : addDays(anchor, view === 'week' ? 6 : 13)

  // Loading is derived from a request key so no setState runs synchronously in the effect.
  const requestKey = `${from}|${to}`
  const days = loadedDays?.key === requestKey ? loadedDays.days : null

  useEffect(() => {
    let mounted = true
    apiClient
      .getJson<ScheduleDay[]>(`/api/me/planning?from=${from}&to=${to}`)
      .then((data) => {
        if (mounted) setLoadedDays({ key: `${from}|${to}`, days: data })
      })
      .catch(() => {
        if (mounted) setLoadError('Je planning kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [from, to])

  function shift(direction: -1 | 1) {
    if (view === 'month') {
      const next = new Date(anchorDate.getFullYear(), anchorDate.getMonth() + direction, 1)
      setAnchor(toIsoDate(mondayOf(next)) <= toIsoDate(next) ? toIsoDate(next) : toIsoDate(next))
    } else {
      setAnchor(addDays(anchor, direction * (view === 'week' ? 7 : 14)))
    }
  }

  function chipAction(entry: ScheduleEntry): (() => void) | undefined {
    if (entry.tripId && hasPermission('driver_workflow.view')) {
      return () => navigate(`/my-trips/${entry.tripId}`)
    }
    return undefined
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
  const periodLabel =
    view === 'month'
      ? `${MONTH_NAMES[anchorDate.getMonth()]} ${anchorDate.getFullYear()}`
      : `${from} – ${to}`

  return (
    <div>
      <PageHeader
        title="Mijn planning"
        subtitle="Je shifts, ritten, opleidingen en afwezigheden."
        action={
          <Button variant="secondary" onClick={() => void downloadIcs()}>
            Agenda-export (.ics)
          </Button>
        }
      />

      <div className="portal-week-nav">
        <button type="button" onClick={() => shift(-1)} aria-label="Vorige periode">
          ‹ vorige
        </button>
        <span>{periodLabel}</span>
        <button type="button" onClick={() => shift(1)} aria-label="Volgende periode">
          volgende ›
        </button>
        <span className="portal-view-switch" role="group" aria-label="Weergave">
          {(['week', 'month', 'list'] as const).map((mode) => (
            <button
              key={mode}
              type="button"
              className={view === mode ? 'portal-view-active' : undefined}
              onClick={() => setView(mode)}
            >
              {mode === 'week' ? 'Week' : mode === 'month' ? 'Maand' : 'Lijst'}
            </button>
          ))}
        </span>
      </div>

      {!days && <LoadingState message="Planning laden..." />}

      {days && view === 'week' && (
        <div className="portal-calendar-week">
          {days.map((day) => {
            const dayIndex = (new Date(`${day.date}T00:00:00`).getDay() + 6) % 7
            return (
              <div key={day.date} className={`portal-calendar-day ${day.date === today ? 'portal-planning-today' : ''}`}>
                <div className="portal-planning-date">
                  {DAY_NAMES[dayIndex]} {day.date.slice(8, 10)}/{day.date.slice(5, 7)}
                </div>
                <div className="portal-calendar-entries">
                  {day.entries.length === 0 && <span className="portal-planning-free">vrij</span>}
                  {day.entries.map((entry, index) => (
                    <ScheduleChip key={index} entry={entry} onClick={chipAction(entry)} />
                  ))}
                </div>
              </div>
            )
          })}
        </div>
      )}

      {days && view === 'month' && (
        <div className="portal-calendar-month">
          {DAY_NAMES.map((name) => (
            <div key={name} className="portal-month-header">
              {name}
            </div>
          ))}
          {/* Leading offset so the 1st lands on its weekday column. */}
          {Array.from({ length: (new Date(`${monthStart}T00:00:00`).getDay() + 6) % 7 }).map((_, index) => (
            <div key={`pad-${index}`} />
          ))}
          {days.map((day) => (
            <button
              key={day.date}
              type="button"
              className={`portal-month-cell ${day.date === today ? 'portal-planning-today' : ''}`}
              onClick={() => {
                setView('week')
                setAnchor(toIsoDate(mondayOf(new Date(`${day.date}T00:00:00`))))
              }}
              title={day.entries.map((entry) => entry.label).join(', ') || 'vrij'}
            >
              <span className="portal-month-daynr">{Number(day.date.slice(8, 10))}</span>
              {day.entries.slice(0, 2).map((entry, index) => (
                <span key={index} className="portal-month-entry">
                  {entry.label}
                </span>
              ))}
              {day.entries.length > 2 && <span className="portal-month-more">+{day.entries.length - 2}</span>}
            </button>
          ))}
        </div>
      )}

      {days && view === 'list' && (
        <ul className="portal-planning">
          {days.map((day) => {
            const dayIndex = (new Date(`${day.date}T00:00:00`).getDay() + 6) % 7
            return (
              <li key={day.date} className={`portal-planning-day ${day.date === today ? 'portal-planning-today' : ''}`}>
                <div className="portal-planning-date">
                  {DAY_NAMES[dayIndex]} {day.date.slice(8, 10)}/{day.date.slice(5, 7)}
                  {day.date === today && ' · vandaag'}
                </div>
                <div className="portal-planning-entries">
                  {day.entries.length === 0 && <span className="portal-planning-free">vrij</span>}
                  {day.entries.map((entry, index) => (
                    <ScheduleChip key={index} entry={entry} onClick={chipAction(entry)} />
                  ))}
                </div>
              </li>
            )
          })}
        </ul>
      )}

      {days && <ScheduleLegend />}
    </div>
  )
}
