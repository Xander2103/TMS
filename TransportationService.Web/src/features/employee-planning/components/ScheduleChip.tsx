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

/** Chip headline: trips show their trip number, everything else its state label. */
function chipText(entry: ScheduleEntry): string {
  return entry.sourceType === 'Trip' ? entry.label : SCHEDULE_STATE_LABELS[entry.state]
}

/** One schedule cell chip: colour + icon + label, never colour alone. */
export function ScheduleChip({ entry, onClick }: { entry: ScheduleEntry; onClick?: () => void }) {
  const range = timeRange(entry)
  const description = chipDescription(entry)
  const content = (
    <>
      <span className="schedule-chip-icon" aria-hidden="true">
        {SCHEDULE_STATE_ICONS[entry.state]}
      </span>
      <span className="schedule-chip-text">
        <span className="schedule-chip-state">{chipText(entry)}</span>
        {range && <span className="schedule-chip-time">{range}</span>}
      </span>
    </>
  )

  return onClick ? (
    <button
      type="button"
      className={`schedule-chip schedule-chip-${entry.state.toLowerCase()}`}
      onClick={onClick}
      title={description}
      aria-label={description}
    >
      {content}
    </button>
  ) : (
    <span className={`schedule-chip schedule-chip-${entry.state.toLowerCase()}`} title={description}>
      {content}
    </span>
  )
}

/** Accessible legend: every state with its colour swatch, icon and label. */
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
      </ul>
    </details>
  )
}
