import {
  SCHEDULE_STATES,
  SCHEDULE_STATE_ICONS,
  SCHEDULE_STATE_LABELS,
  chipDescription,
  type ScheduleEntry,
  type ScheduleEntryState,
} from '../types'
import './employee-planning.css'

function timeRange(entry: ScheduleEntry): string | null {
  if (!entry.startTime || !entry.endTime) return null
  return `${entry.startTime.slice(0, 5)}–${entry.endTime.slice(0, 5)}`
}

/** Chip headline: trips show their trip number, notes their title, the rest its state label. */
function chipText(entry: ScheduleEntry): string {
  return entry.sourceType === 'Trip' || entry.sourceType === 'Note' ? entry.label : SCHEDULE_STATE_LABELS[entry.state]
}

/** One schedule cell chip: colour + icon + label, never colour alone. */
export function ScheduleChip({
  entry,
  onClick,
  compact,
  fullWidth,
}: {
  entry: ScheduleEntry
  onClick?: () => void
  compact?: boolean
  /** All-day entries (e.g. absences, which carry no start/end time) read better as a full-width banner. */
  fullWidth?: boolean
}) {
  const range = timeRange(entry)
  const description = chipDescription(entry)
  const conflictClass = entry.conflictSeverity
    ? ` schedule-chip-conflict schedule-chip-conflict-${entry.conflictSeverity.toLowerCase()}`
    : ''
  const compactClass = compact ? ' schedule-chip-compact' : ''
  const fullWidthClass = fullWidth ? ' schedule-chip-fullwidth' : ''
  // Dynamic colour (leave type / personal note) overrides the per-state convention; the
  // icon + label stay, so colour is never the only signal.
  const colourStyle = entry.colour
    ? { background: `${entry.colour}22`, borderColor: entry.colour }
    : undefined
  const content = (
    <>
      <span className="schedule-chip-icon" aria-hidden="true">
        {SCHEDULE_STATE_ICONS[entry.state]}
      </span>
      <span className="schedule-chip-text">
        <span className="schedule-chip-state">{chipText(entry)}</span>
        {range && <span className="schedule-chip-time">{range}</span>}
      </span>
      {entry.conflictSeverity && (
        <span className="schedule-chip-conflict-marker" aria-hidden="true">
          ⚠
        </span>
      )}
    </>
  )

  return onClick ? (
    <button
      type="button"
      className={`schedule-chip schedule-chip-${entry.state.toLowerCase()}${conflictClass}${compactClass}${fullWidthClass}`}
      style={colourStyle}
      onClick={onClick}
      title={description}
      aria-label={description}
    >
      {content}
    </button>
  ) : (
    <span
      className={`schedule-chip schedule-chip-${entry.state.toLowerCase()}${conflictClass}${compactClass}${fullWidthClass}`}
      style={colourStyle}
      title={description}
    >
      {content}
    </span>
  )
}

/** Accessible legend: every state with its colour swatch, icon and label, plus the conflict marker. */
export function ScheduleLegend() {
  return (
    <details className="schedule-legend">
      <summary>Legenda</summary>
      <ul>
        {SCHEDULE_STATES.map((state: ScheduleEntryState) => (
          <li key={state}>
            <span className={`schedule-chip schedule-chip-${state.toLowerCase()}`}>
              <span className="schedule-chip-icon" aria-hidden="true">
                {SCHEDULE_STATE_ICONS[state]}
              </span>
              <span className="schedule-chip-text">
                <span className="schedule-chip-state">{SCHEDULE_STATE_LABELS[state]}</span>
              </span>
            </span>
          </li>
        ))}
        <li>
          <span className="schedule-chip schedule-chip-conflict schedule-chip-conflict-blocking">
            <span className="schedule-chip-conflict-marker" aria-hidden="true">
              ⚠
            </span>
            <span className="schedule-chip-text">
              <span className="schedule-chip-state">Conflict / aandacht</span>
            </span>
          </span>
        </li>
      </ul>
    </details>
  )
}
