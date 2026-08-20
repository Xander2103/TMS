import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { ApiError } from '../../../api/apiClient'
import { formatDurationMinutes, formatTime } from '../../../utils/dates'
import {
  clockIn, clockOut, endBreak, getMyAttendanceStatus, startBreak,
} from '../api/timeAttendanceApi'
import { ATTENDANCE_LIVE_STATUS_LABELS, ATTENDANCE_LIVE_STATUS_TONE } from '../types'
import type { AttendanceStatus } from '../types'
import './work-status-card.css'

const POLL_INTERVAL_MS = 60_000

/**
 * De centrale werkstatus-card (spec §8/§52): maximaal de acties die NU geldig zijn,
 * grote knoppen, en status die na elke actie en elke minuut ververst. Verbergt
 * zichzelf wanneer er geen personeelsdossier aan het account hangt (404).
 */
export function WorkStatusCard() {
  const { showError, showSuccess } = useToast()
  const [status, setStatus] = useState<AttendanceStatus | null>(null)
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
    getMyAttendanceStatus()
      .then((data) => {
        if (!mounted.current) return
        setStatus(data)
        setHidden(false)
      })
      .catch((err) => {
        if (!mounted.current) return
        if (err instanceof ApiError && (err.status === 404 || err.status === 403)) {
          setHidden(true)
        }
      })
  }, [tick])

  const runAction = useCallback(
    async (action: () => Promise<AttendanceStatus>, successMessage: string) => {
      setBusy(true)
      try {
        const next = await action()
        if (!mounted.current) return
        setStatus(next)
        showSuccess(successMessage)
      } catch (err) {
        // Een 409 (bv. dubbelklik of tweede browser) bevat de echte reden; daarna status verversen.
        showError(describeApiError(err, 'De actie kon niet worden geregistreerd.').message)
        try {
          const refreshed = await getMyAttendanceStatus()
          if (mounted.current) setStatus(refreshed)
        } catch {
          /* status blijft staan; volgende poll herstelt */
        }
      } finally {
        if (mounted.current) setBusy(false)
      }
    },
    [showError, showSuccess],
  )

  if (hidden || !status) return null

  return (
    <section className="wsc" aria-label="Werkstatus">
      <div className="wsc-head">
        <h2 className="wsc-title">Werkstatus</h2>
        <Badge tone={ATTENDANCE_LIVE_STATUS_TONE[status.status]}>
          {ATTENDANCE_LIVE_STATUS_LABELS[status.status]}
        </Badge>
      </div>

      <div className="wsc-body">
        {status.status === 'NotClockedIn' && (
          <p className="wsc-hint">Je bent nog niet ingepunt.</p>
        )}
        {status.status === 'ClockedOut' && status.lastClockOutAt && (
          <p className="wsc-hint">Uitgepunt om {formatTime(status.lastClockOutAt)}. Fijne avond!</p>
        )}
        {status.status === 'Working' && status.clockInAt && (
          <p className="wsc-hint">
            Ingepunt om <strong>{formatTime(status.clockInAt)}</strong>
          </p>
        )}
        {status.status === 'OnBreak' && status.breakStartedAt && (
          <p className="wsc-hint">
            Pauze sinds <strong>{formatTime(status.breakStartedAt)}</strong>
          </p>
        )}

        <dl className="wsc-stats">
          <div className="wsc-stat">
            <dt>Vandaag gewerkt</dt>
            <dd>{formatDurationMinutes(status.workedMinutesToday)}</dd>
          </div>
          <div className="wsc-stat">
            <dt>Pauze</dt>
            <dd>{formatDurationMinutes(status.breakMinutesToday)}</dd>
          </div>
        </dl>
      </div>

      <div className="wsc-actions">
        {status.canClockIn && (
          <button
            type="button"
            className="wsc-btn wsc-btn-primary"
            disabled={busy}
            onClick={() => runAction(clockIn, 'Ingepunt. Fijne werkdag!')}
          >
            Inpunten
          </button>
        )}
        {status.canStartBreak && (
          <button
            type="button"
            className="wsc-btn"
            disabled={busy}
            onClick={() => runAction(startBreak, 'Pauze gestart.')}
          >
            Pauze starten
          </button>
        )}
        {status.canEndBreak && (
          <button
            type="button"
            className="wsc-btn wsc-btn-primary"
            disabled={busy}
            onClick={() => runAction(endBreak, 'Pauze beëindigd.')}
          >
            Pauze beëindigen
          </button>
        )}
        {status.canClockOut && (
          <button
            type="button"
            className="wsc-btn wsc-btn-out"
            disabled={busy}
            onClick={() => runAction(clockOut, 'Uitgepunt. Tot morgen!')}
          >
            Uitpunten
          </button>
        )}
      </div>

      <Link to="/portal/time" className="wsc-link">
        Mijn uren bekijken →
      </Link>
    </section>
  )
}
