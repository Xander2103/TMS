import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { apiClient } from '../../../api/apiClient'
import { useAuth } from '../../auth/authContextValue'
import { ScheduleChip, ScheduleLegend } from '../../employee-planning/components/ScheduleChip'
import { mondayOf, toIsoDate, type ScheduleDay, type ScheduleEntry } from '../../employee-planning/types'
import './portal.css'

const DAY_NAMES = ['maandag', 'dinsdag', 'woensdag', 'donderdag', 'vrijdag', 'zaterdag', 'zondag']

/** Own schedule (shifts, trips and leave merged), mobile-first list for the coming two weeks. */
export function PortalPlanningPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const [days, setDays] = useState<ScheduleDay[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [weekStart, setWeekStart] = useState(() => toIsoDate(mondayOf(new Date())))

  /** Own trip chips open the driver execution view; absence chips open the leave page. */
  function chipAction(entry: ScheduleEntry): (() => void) | undefined {
    if (entry.sourceType === 'Trip' && entry.tripId && hasPermission('driver_workflow.view')) {
      return () => navigate(`/my-trips/${entry.tripId}`)
    }
    if (entry.sourceType === 'Absence') {
      return () => navigate('/portal/absences')
    }
    return undefined
  }

  useEffect(() => {
    let mounted = true
    const from = weekStart
    const to = toIsoDate(new Date(new Date(`${weekStart}T00:00:00`).getTime() + 13 * 86_400_000))
    apiClient
      .getJson<ScheduleDay[]>(`/api/me/planning?from=${from}&to=${to}`)
      .then((data) => {
        if (!mounted) return
        setDays(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Je planning kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [weekStart])

  function shiftWeeks(delta: number) {
    const date = new Date(`${weekStart}T00:00:00`)
    date.setDate(date.getDate() + delta * 7)
    setWeekStart(toIsoDate(date))
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!days) return <LoadingState message="Planning laden..." />

  const today = toIsoDate(new Date())

  return (
    <div>
      <PageHeader
        title="Mijn planning"
        subtitle="Je shifts, ritten, opleidingen en afwezigheden voor de komende twee weken."
      />
      <div className="portal-week-nav">
        <button type="button" onClick={() => shiftWeeks(-2)} aria-label="Twee weken terug">
          ‹ vorige
        </button>
        <span>vanaf {weekStart}</span>
        <button type="button" onClick={() => shiftWeeks(2)} aria-label="Twee weken verder">
          volgende ›
        </button>
      </div>
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
      <ScheduleLegend />
    </div>
  )
}
