import { useEffect, useMemo, useState } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { ApiError } from '../../../api/apiClient'
import {
  formatDate, formatDateLong, formatDurationMinutes, formatSignedDurationMinutes,
} from '../../../utils/dates'
import { WorkStatusCard } from '../components/WorkStatusCard'
import { SessionTimeline } from '../components/SessionTimeline'
import { getMyAttendanceHistory } from '../api/timeAttendanceApi'
import type { AttendanceHistory } from '../types'
import './time-attendance.css'

type Period = 'today' | 'week' | 'month'

const PERIOD_LABELS: Record<Period, string> = {
  today: 'Vandaag',
  week: 'Deze week',
  month: 'Deze maand',
}

function toIso(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

export function periodRange(period: Period, now = new Date()): { from: string; to: string } {
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  if (period === 'today') {
    return { from: toIso(today), to: toIso(today) }
  }
  if (period === 'week') {
    const monday = new Date(today)
    monday.setDate(today.getDate() - ((today.getDay() + 6) % 7))
    return { from: toIso(monday), to: toIso(today) }
  }
  return { from: toIso(new Date(today.getFullYear(), today.getMonth(), 1)), to: toIso(today) }
}

/** Self-service "Mijn uren": eigen sessies, pauzes, correcties en planning-vergelijking. */
export function MyTimePage() {
  const [period, setPeriod] = useState<Period>('week')
  const [history, setHistory] = useState<AttendanceHistory | null>(null)
  const [loadedKey, setLoadedKey] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const range = useMemo(() => periodRange(period), [period])
  const requestKey = `${range.from}:${range.to}`
  const isLoading = loadedKey !== requestKey && !error

  useEffect(() => {
    let mounted = true
    setError(null)
    getMyAttendanceHistory(range.from, range.to)
      .then((data) => {
        if (!mounted) return
        setHistory(data)
        setLoadedKey(requestKey)
      })
      .catch((err) => {
        if (!mounted) return
        setError(err instanceof ApiError && err.status === 404
          ? 'Er is geen personeelsdossier gekoppeld aan dit account.'
          : 'Je uren konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [range.from, range.to, requestKey])

  return (
    <div>
      <PageHeader title="Mijn uren" subtitle="Jouw geregistreerde werktijd, pauzes en planning." />
      <WorkStatusCard />

      <div className="ta-period" role="group" aria-label="Periode">
        {(Object.keys(PERIOD_LABELS) as Period[]).map((key) => (
          <button
            key={key}
            type="button"
            className={key === period ? 'ta-period-btn ta-period-btn-active' : 'ta-period-btn'}
            onClick={() => setPeriod(key)}
          >
            {PERIOD_LABELS[key]}
          </button>
        ))}
      </div>

      {error && <ErrorState message={error} />}
      {!error && isLoading && <LoadingState message="Uren laden..." />}

      {!error && !isLoading && history && (
        <>
          <dl className="ta-totals">
            <div className="ta-total">
              <dt>Netto gewerkt</dt>
              <dd>{formatDurationMinutes(history.totalNetMinutes)}</dd>
            </div>
            <div className="ta-total">
              <dt>Pauze</dt>
              <dd>{formatDurationMinutes(history.totalBreakMinutes)}</dd>
            </div>
            {history.totalPlannedMinutes != null && (
              <>
                <div className="ta-total">
                  <dt>Gepland</dt>
                  <dd>{formatDurationMinutes(history.totalPlannedMinutes)}</dd>
                </div>
                <div className="ta-total">
                  <dt>Afwijking</dt>
                  <dd>{formatSignedDurationMinutes(history.totalNetMinutes - history.totalPlannedMinutes)}</dd>
                </div>
              </>
            )}
          </dl>

          {history.days.length === 0 && (
            <p className="placeholder-text">Geen registraties in deze periode.</p>
          )}

          {[...history.days].reverse().map((day) => (
            <section key={day.date} className="ta-day" aria-label={formatDate(day.date)}>
              <header className="ta-day-head">
                <h2>{formatDateLong(day.date)}</h2>
                <div className="ta-day-figures">
                  <span>
                    Netto <strong>{formatDurationMinutes(day.netMinutes)}</strong>
                  </span>
                  <span>
                    Pauze <strong>{formatDurationMinutes(day.breakMinutes)}</strong>
                  </span>
                  {day.plannedMinutes != null && (
                    <span>
                      Gepland <strong>{formatDurationMinutes(day.plannedMinutes)}</strong>
                      {day.deviationMinutes != null && day.deviationMinutes !== 0 && (
                        <em className={day.deviationMinutes > 0 ? 'ta-dev-plus' : 'ta-dev-min'}>
                          {' '}({formatSignedDurationMinutes(day.deviationMinutes)})
                        </em>
                      )}
                    </span>
                  )}
                </div>
              </header>
              {day.sessions.map((session) => (
                <SessionTimeline key={`${day.date}-${session.id}`} session={session} />
              ))}
              {day.sessions.length === 0 && (
                <p className="placeholder-text">
                  Gepland maar geen registratie{day.plannedMinutes != null ? ` (${formatDurationMinutes(day.plannedMinutes)} gepland)` : ''}.
                </p>
              )}
            </section>
          ))}
        </>
      )}
    </div>
  )
}
