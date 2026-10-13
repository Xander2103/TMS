import type { ReactNode } from 'react'
import { useLocale } from '../../i18n/localeContext'
import { addDays, cellAriaLabel, dayIndexMonday, getDayNames, mondayOf, toIsoDate } from './dateUtils'
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

/**
 * Week calendar: 7 day columns (ma-zo) with date headers; entries render via `renderEntry`
 * and stay individually interactive (day columns are plain containers, not buttons).
 *
 * Deliberately plain markup, no `role="grid"`/`"row"`/`"gridcell"` — see the same note on
 * `MonthGrid`. Day headers and entries stay reachable via ordinary Tab order with nl-BE
 * `aria-label`s.
 */
export function WeekGrid<T>({
  anchor,
  entriesByDate,
  renderEntry,
  onSelectDate,
  emptyLabel,
  today = new Date(),
}: WeekGridProps<T>) {
  const { t } = useLocale()
  const monday = mondayOf(anchor)
  const todayIso = toIsoDate(today)
  const days = Array.from({ length: 7 }, (_, index) => addDays(monday, index))

  return (
    <div className="cal-week">
      {days.map((date) => {
        const iso = toIsoDate(date)
        const entries = entriesByDate.get(iso) ?? []
        const isToday = iso === todayIso
        const label = `${getDayNames()[dayIndexMonday(date)]} ${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}`
        const ariaLabel = cellAriaLabel(date, entries.length)
        return (
          <div key={iso} className={`cal-week-day${isToday ? ' cal-today' : ''}`}>
            {onSelectDate ? (
              <button type="button" className="cal-week-date" onClick={() => onSelectDate(iso)} aria-label={ariaLabel}>
                {label}
              </button>
            ) : (
              <div className="cal-week-date" aria-label={ariaLabel}>
                {label}
              </div>
            )}
            <div className="cal-week-entries">
              {entries.length === 0 && <span className="cal-week-free">{emptyLabel ?? t('ui.calendar.free')}</span>}
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
