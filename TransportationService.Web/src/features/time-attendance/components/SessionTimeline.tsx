import { Badge } from '../../../components/ui/Badge'
import { formatDurationMinutes, formatTime } from '../../../utils/dates'
import { useLocale } from '../../../i18n/localeContext'
import { ATTENDANCE_SOURCE_LABELS } from '../types'
import type { AttendanceCorrection, AttendanceSession } from '../types'
import './time-attendance-shared.css'

interface TimelineEntry {
  key: string
  time: string | null
  label: string
  corrections: AttendanceCorrection[]
}

function correctionsFor(
  session: AttendanceSession,
  kind: AttendanceCorrection['kind'],
  breakId?: string,
): AttendanceCorrection[] {
  return session.corrections.filter((c) => c.kind === kind && (breakId === undefined || c.breakId === breakId))
}

/**
 * Leesbare sessietimeline (spec §68): punches en pauzes chronologisch, met per regel de
 * correctie-annotatie "gecorrigeerd vanuit … door … reden: …" wanneer die bestaat.
 */
export function SessionTimeline({ session }: { session: AttendanceSession }) {
  const { t } = useLocale()

  const correctionAnnotation = (correction: AttendanceCorrection): string =>
    (correction.oldValue
      ? t('attendance.timeline.correctionFrom', { time: formatTime(correction.oldValue) })
      : t('attendance.timeline.correctionPlain'))
    + (correction.correctedByName ? t('attendance.timeline.correctionBy', { name: correction.correctedByName }) : '')
    + t('attendance.timeline.correctionReason', { reason: correction.reason })

  const entries: TimelineEntry[] = [
    {
      key: 'in',
      time: session.clockInAt,
      label: t('attendance.timeline.clockedIn'),
      corrections: correctionsFor(session, 'ClockIn'),
    },
    ...session.breaks.flatMap((b) => [
      { key: `bs-${b.id}`, time: b.startedAt, label: t('attendance.timeline.breakStarted'), corrections: correctionsFor(session, 'BreakStart', b.id) },
      ...(b.endedAt
        ? [{ key: `be-${b.id}`, time: b.endedAt, label: t('attendance.timeline.breakEnded'), corrections: correctionsFor(session, 'BreakEnd', b.id) }]
        : []),
    ]),
    ...(session.clockOutAt
      ? [{
          key: 'out',
          time: session.clockOutAt,
          label: session.status === 'AutoClosed' ? t('attendance.timeline.autoClosed') : t('attendance.timeline.clockedOut'),
          corrections: correctionsFor(session, 'ClockOut'),
        }]
      : []),
  ].sort((a, b) => (a.time ?? '').localeCompare(b.time ?? ''))

  const cancelCorrections = correctionsFor(session, 'SessionCancelled')
  const manualCorrections = correctionsFor(session, 'ManualSession')

  return (
    <div className={session.status === 'Cancelled' ? 'ta-session ta-session-cancelled' : 'ta-session'}>
      <div className="ta-session-meta">
        <span className="ta-session-source">{t(ATTENDANCE_SOURCE_LABELS[session.clockInSource])}</span>
        {session.locationName && <span className="ta-session-loc">{session.locationName}</span>}
        {session.status === 'Cancelled' && <Badge tone="danger">{t('attendance.timeline.cancelled')}</Badge>}
        {session.status === 'AutoClosed' && <Badge tone="warning">{t('attendance.timeline.autoClosed')}</Badge>}
        {session.hasCorrections && session.status !== 'Cancelled' && <Badge tone="info">{t('attendance.timeline.corrected')}</Badge>}
        <span className="ta-session-net">
          {t('attendance.timeline.net', { duration: formatDurationMinutes(session.netMinutes) })}
          {session.breakMinutes > 0
            && t('attendance.timeline.breakDuration', { duration: formatDurationMinutes(session.breakMinutes) })}
        </span>
      </div>
      <ol className="ta-timeline">
        {entries.map((entry) => (
          <li key={entry.key}>
            <span className="ta-timeline-time">{entry.time ? formatTime(entry.time) : '—'}</span>
            <span className="ta-timeline-label">{entry.label}</span>
            {entry.corrections.map((correction) => (
              <span key={correction.id} className="ta-correction">
                {correctionAnnotation(correction)}
              </span>
            ))}
          </li>
        ))}
        {!session.clockOutAt && session.status !== 'Cancelled' && (
          <li>
            <span className="ta-timeline-time">…</span>
            <span className="ta-timeline-label">{t('attendance.timeline.notClockedOut')}</span>
          </li>
        )}
      </ol>
      {[...cancelCorrections, ...manualCorrections].map((correction) => (
        <p key={correction.id} className="ta-correction">
          {(correction.kind === 'SessionCancelled'
            ? t('attendance.timeline.cancelledBy')
            : t('attendance.timeline.manualCreated'))
            + (correction.correctedByName ? t('attendance.timeline.correctionBy', { name: correction.correctedByName }) : '')
            + t('attendance.timeline.correctionReason', { reason: correction.reason })}
        </p>
      ))}
    </div>
  )
}
