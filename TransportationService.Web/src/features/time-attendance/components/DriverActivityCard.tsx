import { useCallback, useEffect, useRef, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { ApiError } from '../../../api/apiClient'
import { formatDurationMinutes, formatTime } from '../../../utils/dates'
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
  const { showError, showSuccess } = useToast()
  const [summary, setSummary] = useState<DriverDaySummary | null>(null)
  const [hidden, setHidden] = useState(false)
  const [busy, setBusy] = useState(false)
  const [tick, setTick] = useState(0)
  const mounted = useRef(true)

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
    getMyDriverDay()
      .then((data) => {
        if (!mounted.current) return
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
      if (mounted.current) setSummary(data)
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
        showError(describeApiError(err, 'De actie kon niet worden geregistreerd.').message)
      } finally {
        await refresh()
        if (mounted.current) setBusy(false)
      }
    },
    [refresh, showError, showSuccess],
  )

  if (hidden || !summary) return null

  const { attendance } = summary

  return (
    <section className="dac drv-card" aria-label="Werkdag">
      <div className="dac-head">
        <h2>Werkdag</h2>
        <Badge tone={ATTENDANCE_LIVE_STATUS_TONE[attendance.status]}>
          {ATTENDANCE_LIVE_STATUS_LABELS[attendance.status]}
        </Badge>
      </div>

      {attendance.clockInAt && (
        <p className="dac-line">Gestart om <strong>{formatTime(attendance.clockInAt)}</strong></p>
      )}
      {attendance.status === 'OnBreak' && attendance.breakStartedAt && (
        <p className="dac-line">Pauze sinds <strong>{formatTime(attendance.breakStartedAt)}</strong></p>
      )}

      <div className="dac-actions">
        {attendance.canClockIn && (
          <button type="button" className="dac-btn dac-btn-primary" disabled={busy} onClick={() => runAction(clockIn, 'Ingepunt. Goede rit!')}>
            Inpunten
          </button>
        )}
        {attendance.canStartBreak && (
          <button type="button" className="dac-btn" disabled={busy} onClick={() => runAction(startBreak, 'Pauze gestart.')}>
            Pauze starten
          </button>
        )}
        {attendance.canEndBreak && (
          <button type="button" className="dac-btn dac-btn-primary" disabled={busy} onClick={() => runAction(endBreak, 'Pauze beëindigd.')}>
            Pauze beëindigen
          </button>
        )}
        {attendance.canClockOut && (
          <button type="button" className="dac-btn dac-btn-out" disabled={busy} onClick={() => runAction(clockOut, 'Uitgepunt.')}>
            Uitpunten
          </button>
        )}
      </div>

      <dl className="dac-grid">
        <div>
          <dt>Diensttijd (attendance)</dt>
          <dd>{formatDurationMinutes(attendance.workedMinutesToday + attendance.breakMinutesToday)}</dd>
        </div>
        <div>
          <dt>Werk</dt>
          <dd>{formatDurationMinutes(attendance.workedMinutesToday)}</dd>
        </div>
        <div>
          <dt>Pauze</dt>
          <dd>{formatDurationMinutes(attendance.breakMinutesToday)}</dd>
        </div>
      </dl>

      {summary.plannedToday.length > 0 && (
        <p className="dac-line dac-planned">
          Gepland vandaag:{' '}
          {summary.plannedToday
            .map((plan) => `${plan.startTime.slice(0, 5)}–${plan.endTime.slice(0, 5)}`)
            .join(', ')}
        </p>
      )}

      <div className="dac-tacho">
        <h3>Tachograaf</h3>
        {summary.tachographConnected ? (
          <p className="dac-line">Rijtijden via tachograaf.</p>
        ) : (
          <p className="dac-tacho-off">
            Tachograafdata niet gekoppeld — rijtijden, beschikbaarheid en rust verschijnen hier zodra
            een tachograafbron is aangesloten. Attendance is géén bron voor wettelijke rijtijden.
          </p>
        )}
      </div>
    </section>
  )
}
