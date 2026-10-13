import { useCallback, useEffect, useRef, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { ApiError } from '../../../api/apiClient'
import { formatDurationMinutes, formatTime } from '../../../utils/dates'
import { useLocale } from '../../../i18n/localeContext'
import { clockIn, clockOut, endBreak, getMyDriverDay, startBreak } from '../api/timeAttendanceApi'
import { ATTENDANCE_LIVE_STATUS_LABELS, ATTENDANCE_LIVE_STATUS_TONE } from '../types'
import type { DriverDaySummary } from '../types'
import './driver-activity-card.css'

const POLL_INTERVAL_MS = 60_000

/**
 * Driver Activity Card (spec §30/§70): attendance-acties + dagoverzicht voor de
 * chauffeur, met planning (verwacht) en attendance (werkelijk) als gescheiden blokken.
 * De tachograafsectie toont in v1 expliciet "Tachograafdata niet gekoppeld" — er wordt
 * NOOIT rijtijd uit attendance afgeleid of gefaked; de latere DriverActivity-module
 * vult dit blok met echte tachograafdata.
 */
export function DriverActivityCard() {
  const { t } = useLocale()
  const { showError, showSuccess } = useToast()
  const [summary, setSummary] = useState<DriverDaySummary | null>(null)
  const [hidden, setHidden] = useState(false)
  const [busy, setBusy] = useState(false)
  const [tick, setTick] = useState(0)
  const mounted = useRef(true)
  // Zelfde stale-poll-bescherming als WorkStatusCard.
  const statusSeq = useRef(0)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  useEffect(() => {
    const handle = setInterval(() => setTick((t) => t + 1), POLL_INTERVAL_MS)
    return () => clearInterval(handle)
  }, [])

  useEffect(() => {
    const seq = statusSeq.current
    getMyDriverDay()
      .then((data) => {
        if (!mounted.current || statusSeq.current !== seq) return
        setSummary(data)
        setHidden(false)
      })
      .catch((err) => {
        if (mounted.current && err instanceof ApiError && (err.status === 404 || err.status === 403)) {
          setHidden(true)
        }
      })
  }, [tick])

  const refresh = useCallback(async () => {
    try {
      const data = await getMyDriverDay()
      if (mounted.current) {
        statusSeq.current += 1
        setSummary(data)
      }
    } catch {
      /* volgende poll herstelt */
    }
  }, [])

  const runAction = useCallback(
    async (action: () => Promise<unknown>, successMessage: string) => {
      setBusy(true)
      try {
        await action()
        showSuccess(successMessage)
      } catch (err) {
        showError(describeApiError(err, t('attendance.card.actionFailed')).message)
      } finally {
        await refresh()
        if (mounted.current) setBusy(false)
      }
    },
    [refresh, showError, showSuccess, t],
  )

  if (hidden || !summary) return null

  const { attendance } = summary

  return (
    <section className="dac drv-card" aria-label={t('attendance.driver.title')}>
      <div className="dac-head">
        <h2>{t('attendance.driver.title')}</h2>
        <Badge tone={ATTENDANCE_LIVE_STATUS_TONE[attendance.status]}>
          {t(ATTENDANCE_LIVE_STATUS_LABELS[attendance.status])}
        </Badge>
      </div>

      {attendance.clockInAt && (
        <p className="dac-line">{t('attendance.driver.startedAt', { time: formatTime(attendance.clockInAt) })}</p>
      )}
      {attendance.status === 'OnBreak' && attendance.breakStartedAt && (
        <p className="dac-line">{t('attendance.driver.breakSince', { time: formatTime(attendance.breakStartedAt) })}</p>
      )}

      <div className="dac-actions">
        {attendance.canClockIn && (
          <button type="button" className="dac-btn dac-btn-primary" disabled={busy} onClick={() => runAction(clockIn, t('attendance.driver.toastClockedIn'))}>
            {t('attendance.actions.clockIn')}
          </button>
        )}
        {attendance.canStartBreak && (
          <button type="button" className="dac-btn" disabled={busy} onClick={() => runAction(startBreak, t('attendance.card.toastBreakStarted'))}>
            {t('attendance.actions.startBreak')}
          </button>
        )}
        {attendance.canEndBreak && (
          <button type="button" className="dac-btn dac-btn-primary" disabled={busy} onClick={() => runAction(endBreak, t('attendance.card.toastBreakEnded'))}>
            {t('attendance.actions.endBreak')}
          </button>
        )}
        {attendance.canClockOut && (
          <button type="button" className="dac-btn dac-btn-out" disabled={busy} onClick={() => runAction(clockOut, t('attendance.driver.toastClockedOut'))}>
            {t('attendance.actions.clockOut')}
          </button>
        )}
      </div>

      <dl className="dac-grid">
        <div>
          <dt>{t('attendance.driver.dutyTime')}</dt>
          <dd>{formatDurationMinutes(attendance.workedMinutesToday + attendance.breakMinutesToday)}</dd>
        </div>
        <div>
          <dt>{t('attendance.driver.work')}</dt>
          <dd>{formatDurationMinutes(attendance.workedMinutesToday)}</dd>
        </div>
        <div>
          <dt>{t('attendance.driver.break')}</dt>
          <dd>{formatDurationMinutes(attendance.breakMinutesToday)}</dd>
        </div>
      </dl>

      {summary.plannedToday.length > 0 && (
        <p className="dac-line dac-planned">
          {t('attendance.driver.plannedToday', {
            slots: summary.plannedToday
              .map((plan) => `${plan.startTime.slice(0, 5)}–${plan.endTime.slice(0, 5)}`)
              .join(', '),
          })}
        </p>
      )}

      <div className="dac-tacho">
        <h3>{t('attendance.driver.tachograph')}</h3>
        {summary.tachographConnected ? (
          <p className="dac-line">{t('attendance.driver.tachographConnected')}</p>
        ) : (
          <p className="dac-tacho-off">
            {t('attendance.driver.tachographNotConnected')}
          </p>
        )}
      </div>
    </section>
  )
}
