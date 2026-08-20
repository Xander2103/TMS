import { Badge } from '../../../components/ui/Badge'
import { formatDurationMinutes, formatTime } from '../../../utils/dates'
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
  const entries: TimelineEntry[] = [
    {
      key: 'in',
      time: session.clockInAt,
      label: 'Ingepunt',
      corrections: correctionsFor(session, 'ClockIn'),
    },
    ...session.breaks.flatMap((b) => [
      { key: `bs-${b.id}`, time: b.startedAt, label: 'Pauze gestart', corrections: correctionsFor(session, 'BreakStart', b.id) },
      ...(b.endedAt
        ? [{ key: `be-${b.id}`, time: b.endedAt, label: 'Pauze gestopt', corrections: correctionsFor(session, 'BreakEnd', b.id) }]
        : []),
    ]),
    ...(session.clockOutAt
      ? [{
          key: 'out',
          time: session.clockOutAt,
          label: session.status === 'AutoClosed' ? 'Automatisch afgesloten' : 'Uitgepunt',
          corrections: correctionsFor(session, 'ClockOut'),
        }]
      : []),
  ].sort((a, b) => (a.time ?? '').localeCompare(b.time ?? ''))

  const cancelCorrections = correctionsFor(session, 'SessionCancelled')
  const manualCorrections = correctionsFor(session, 'ManualSession')

  return (
    <div className={session.status === 'Cancelled' ? 'ta-session ta-session-cancelled' : 'ta-session'}>
      <div className="ta-session-meta">
        <span className="ta-session-source">{ATTENDANCE_SOURCE_LABELS[session.clockInSource]}</span>
        {session.locationName && <span className="ta-session-loc">{session.locationName}</span>}
        {session.status === 'Cancelled' && <Badge tone="danger">Geannuleerd</Badge>}
        {session.status === 'AutoClosed' && <Badge tone="warning">Automatisch afgesloten</Badge>}
        {session.hasCorrections && session.status !== 'Cancelled' && <Badge tone="info">Gecorrigeerd</Badge>}
        <span className="ta-session-net">
          Netto {formatDurationMinutes(session.netMinutes)}
          {session.breakMinutes > 0 && ` · pauze ${formatDurationMinutes(session.breakMinutes)}`}
        </span>
      </div>
      <ol className="ta-timeline">
        {entries.map((entry) => (
          <li key={entry.key}>
            <span className="ta-timeline-time">{entry.time ? formatTime(entry.time) : '—'}</span>
            <span className="ta-timeline-label">{entry.label}</span>
            {entry.corrections.map((correction) => (
              <span key={correction.id} className="ta-correction">
                ↳ gecorrigeerd{correction.oldValue ? ` vanuit ${formatTime(correction.oldValue)}` : ''}
                {correction.correctedByName ? ` door ${correction.correctedByName}` : ''} · reden: {correction.reason}
              </span>
            ))}
          </li>
        ))}
        {!session.clockOutAt && session.status !== 'Cancelled' && (
          <li>
            <span className="ta-timeline-time">…</span>
            <span className="ta-timeline-label">Nog niet uitgepunt</span>
          </li>
        )}
      </ol>
      {[...cancelCorrections, ...manualCorrections].map((correction) => (
        <p key={correction.id} className="ta-correction">
          {correction.kind === 'SessionCancelled' ? 'Geannuleerd' : 'Manueel aangemaakt'}
          {correction.correctedByName ? ` door ${correction.correctedByName}` : ''} · reden: {correction.reason}
        </p>
      ))}
    </div>
  )
}
