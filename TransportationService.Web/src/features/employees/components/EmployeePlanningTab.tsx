import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '../../../components/ui/Button'
import { useAuth } from '../../auth/authContextValue'
import { ScheduleChip, ScheduleLegend } from '../../employee-planning/components/ScheduleChip'
import { getSchedule } from '../../employee-planning/api/employeePlanningApi'
import { mondayOf, toIsoDate, type ScheduleDay, type ScheduleEntry } from '../../employee-planning/types'
import '../../portal/pages/portal.css'

const DAY_LABELS = ['ma', 'di', 'wo', 'do', 'vr', 'za', 'zo']

/**
 * Four-week personal schedule on the employee detail page. Reuses the schedule read model
 * (shifts + absences + trip-generated entries incl. conflict markers); chips deep-link
 * per source when the viewer holds the target permission.
 */
export function EmployeePlanningTab({ employeeId }: { employeeId: string }) {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const canViewTrips = hasPermission('planning.view')

  const [weekStart, setWeekStart] = useState(() => toIsoDate(mondayOf(new Date())))
  // Request-key idiom: loading derives from whether the loaded data matches the current request.
  const [state, setState] = useState<{ days: ScheduleDay[] | null; loadedKey: string }>({ days: null, loadedKey: '' })
  const [error, setError] = useState<string | null>(null)

  const from = weekStart
  const to = toIsoDate(new Date(new Date(`${weekStart}T00:00:00`).getTime() + 27 * 86_400_000))
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

  function shiftWeeks(delta: number) {
    const date = new Date(`${weekStart}T00:00:00`)
    date.setDate(date.getDate() + delta * 7)
    setWeekStart(toIsoDate(date))
  }

  function chipAction(entry: ScheduleEntry): (() => void) | undefined {
    if (entry.sourceType === 'Trip' && entry.tripId && canViewTrips) {
      return () => navigate(`/planning/${entry.tripId}`)
    }
    if (entry.sourceType === 'Absence' && entry.absenceId) {
      return () => navigate(`/employees/${employeeId}?tab=afwezigheden&absenceId=${entry.absenceId}`)
    }
    return undefined
  }

  if (error) return <p className="placeholder-text">{error}</p>

  const withEntries = (days ?? []).filter((day) => day.entries.length > 0)

  return (
    <div className="employee-planning-tab">
      <div className="ep-toolbar" style={{ marginBottom: 12, display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
        <Button variant="secondary" onClick={() => shiftWeeks(-1)}>
          ‹ Vorige week
        </Button>
        <span>
          {from} – {to}
        </span>
        <Button variant="secondary" onClick={() => shiftWeeks(1)}>
          Volgende week ›
        </Button>
        <ScheduleLegend />
      </div>
      {days === null && <p className="placeholder-text">Planning laden…</p>}
      {days !== null && withEntries.length === 0 && <p className="placeholder-text">Geen planning in deze periode.</p>}
      {withEntries.length > 0 && (
        <ul className="portal-planning">
          {withEntries.map((day) => {
            const date = new Date(`${day.date}T00:00:00`)
            const dayIndex = (date.getDay() + 6) % 7
            return (
              <li key={day.date}>
                <span className="portal-planning-date">
                  {DAY_LABELS[dayIndex]} {day.date}
                </span>
                <span className="portal-planning-entries">
                  {day.entries.map((entry, index) => (
                    <ScheduleChip key={index} entry={entry} onClick={chipAction(entry)} />
                  ))}
                </span>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
