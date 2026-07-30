import type { ReactNode } from 'react'
import { DAY_NAMES, addDays, cellAriaLabel, dayIndexMonday, mondayOf, toIsoDate } from './dateUtils'
import type { CalendarCellContext } from './MonthGrid'
import './calendar.css'

export interface WeekGridProps<T> {
  /** Any date within the week to display; the grid always shows Monday..Sunday of that week. */
  anchor: Date
  /** Entries keyed by ISO date (yyyy-MM-dd). */
  entriesByDate: Map<string, T[]>
  renderEntry: (entry: T, ctx: CalendarCellContext) => ReactNode
  /** Called with the ISO date when a day header is clicked (omit to render a plain heading). */
  onSelectDate?: (date: string) => void
  /** Shown in a day column that has no entries. */
  emptyLabel?: string
  /** Injectable for tests; defaults to now. */
  today?: Date
}

/** Week calendar: 7 day columns (ma-zo) with date headers; entries render via `renderEntry`
 * and stay individually interactive (day columns are plain containers, not buttons). */
export function WeekGrid<T>({
  anchor,
  entriesByDate,
  renderEntry,
  onSelectDate,
  emptyLabel = 'vrij',
  today = new Date(),
}: WeekGridProps<T>) {
  const monday = mondayOf(anchor)
  const todayIso = toIsoDate(today)
  const days = Array.from({ length: 7 }, (_, index) => addDays(monday, index))

  return (
    <div className="cal-week" role="grid" aria-label="Weekkalender">
      {days.map((date) => {
        const iso = toIsoDate(date)
        const entries = entriesByDate.get(iso) ?? []
        const isToday = iso === todayIso
        const label = `${DAY_NAMES[dayIndexMonday(date)]} ${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}`
        const ariaLabel = cellAriaLabel(date, entries.length)
        return (
          <div key={iso} className={`cal-week-day${isToday ? ' cal-today' : ''}`} role="row">
            {onSelectDate ? (
              <button type="button" className="cal-week-date" onClick={() => onSelectDate(iso)} aria-label={ariaLabel}>
                {label}
              </button>
            ) : (
              <div className="cal-week-date" aria-label={ariaLabel}>
                {label}
              </div>
            )}
            <div className="cal-week-entries" role="gridcell">
              {entries.length === 0 && <span className="cal-week-free">{emptyLabel}</span>}
              {entries.map((entry, index) => (
                <span className="cal-week-entry" key={index}>
                  {renderEntry(entry, { date: iso })}
                </span>
              ))}
            </div>
          </div>
        )
      })}
    </div>
  )
}
