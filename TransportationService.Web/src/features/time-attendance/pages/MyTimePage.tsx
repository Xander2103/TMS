import { useEffect, useMemo, useState } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { ApiError } from '../../../api/apiClient'
import {
  formatDate, formatDateLong, formatDurationMinutes, formatSignedDurationMinutes,
} from '../../../utils/dates'
import { useLocale } from '../../../i18n/localeContext'
import { WorkStatusCard } from '../components/WorkStatusCard'
import { SessionTimeline } from '../components/SessionTimeline'
import { getMyAttendanceHistory } from '../api/timeAttendanceApi'
import { periodRange, type Period } from '../utils/periodRange'
import type { AttendanceHistory } from '../types'
import './time-attendance.css'

const PERIOD_LABELS: Record<Period, string> = {
  today: 'attendance.myTime.periodToday',
  week: 'attendance.myTime.periodWeek',
  month: 'attendance.myTime.periodMonth',
}

/** Self-service "Mijn uren": eigen sessies, pauzes, correcties en planning-vergelijking. */
export function MyTimePage() {
  const { t } = useLocale()
  const [period, setPeriod] = useState<Period>('week')
  const [history, setHistory] = useState<AttendanceHistory | null>(null)
  const [loadedKey, setLoadedKey] = useState<string | null>(null)
  // Keyed on the request identity, so a stale error hides itself when the period changes —
  // no synchronous reset inside the effect needed (react-hooks/set-state-in-effect).
  const [errorState, setErrorState] = useState<{ key: string; message: string } | null>(null)

  const range = useMemo(() => periodRange(period), [period])
  const requestKey = `${range.from}:${range.to}`
  const error = errorState?.key === requestKey ? errorState.message : null
  const isLoading = loadedKey !== requestKey && !error

  useEffect(() => {
    let mounted = true
    getMyAttendanceHistory(range.from, range.to)
      .then((data) => {
        if (!mounted) return
        setHistory(data)
        setLoadedKey(requestKey)
      })
      .catch((err) => {
        if (!mounted) return
        setErrorState({
          key: requestKey,
          message: err instanceof ApiError && err.status === 404
            ? 'attendance.myTime.noEmployeeLink'
            : 'attendance.myTime.loadFailed',
        })
      })
    return () => {
      mounted = false
    }
  }, [range.from, range.to, requestKey])

  return (
    <div>
      <PageHeader title={t('attendance.myTime.title')} subtitle={t('attendance.myTime.subtitle')} />
      <WorkStatusCard />

      <div className="ta-period" role="group" aria-label={t('attendance.myTime.periodLabel')}>
        {(Object.keys(PERIOD_LABELS) as Period[]).map((key) => (
          <button
            key={key}
            type="button"
            className={key === period ? 'ta-period-btn ta-period-btn-active' : 'ta-period-btn'}
            onClick={() => setPeriod(key)}
          >
            {t(PERIOD_LABELS[key])}
          </button>
        ))}
      </div>

      {error && <ErrorState message={t(error)} />}
      {!error && isLoading && <LoadingState message={t('attendance.myTime.loading')} />}

      {!error && !isLoading && history && (
        <>
          <dl className="ta-totals">
            <div className="ta-total">
              <dt>{t('attendance.myTime.netWorked')}</dt>
              <dd>{formatDurationMinutes(history.totalNetMinutes)}</dd>
            </div>
            <div className="ta-total">
              <dt>{t('attendance.myTime.break')}</dt>
              <dd>{formatDurationMinutes(history.totalBreakMinutes)}</dd>
            </div>
            {history.totalPlannedMinutes != null && (
              <>
                <div className="ta-total">
                  <dt>{t('attendance.myTime.planned')}</dt>
                  <dd>{formatDurationMinutes(history.totalPlannedMinutes)}</dd>
                </div>
                <div className="ta-total">
                  <dt>{t('attendance.myTime.deviation')}</dt>
                  <dd>{formatSignedDurationMinutes(history.totalNetMinutes - history.totalPlannedMinutes)}</dd>
                </div>
              </>
            )}
          </dl>

          {history.days.length === 0 && (
            <p className="placeholder-text">{t('attendance.myTime.emptyPeriod')}</p>
          )}

          {[...history.days].reverse().map((day) => (
            <section key={day.date} className="ta-day" aria-label={formatDate(day.date)}>
              <header className="ta-day-head">
                <h2>{formatDateLong(day.date)}</h2>
                <div className="ta-day-figures">
                  <span>
                    {t('attendance.myTime.net')} <strong>{formatDurationMinutes(day.netMinutes)}</strong>
                  </span>
                  <span>
                    {t('attendance.myTime.break')} <strong>{formatDurationMinutes(day.breakMinutes)}</strong>
                  </span>
                  {day.plannedMinutes != null && (
                    <span>
                      {t('attendance.myTime.planned')} <strong>{formatDurationMinutes(day.plannedMinutes)}</strong>
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
                  {day.plannedMinutes != null
                    ? t('attendance.myTime.plannedNoRegistrationWith', { duration: formatDurationMinutes(day.plannedMinutes) })
                    : t('attendance.myTime.plannedNoRegistration')}
                </p>
              )}
            </section>
          ))}
        </>
      )}
    </div>
  )
}
