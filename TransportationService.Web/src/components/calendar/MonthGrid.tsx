import type { ReactNode } from 'react'
import { useLocale } from '../../i18n/localeContext'
import { addDays, cellAriaLabel, getDayNames, monthGridRange, toIsoDate } from './dateUtils'
import './calendar.css'

export interface CalendarCellContext {
  /** ISO (yyyy-MM-dd) date of the cell the entry is rendered in. */
  date: string
}

export interface MonthGridProps<T> {
  /** Any date within the month to display. */
  anchor: Date
  /** Entries keyed by ISO date (yyyy-MM-dd). */
  entriesByDate: Map<string, T[]>
  renderEntry: (entry: T, ctx: CalendarCellContext) => ReactNode
  /** Called with the ISO date of the clicked cell (leading/trailing pad cells included). */
  onSelectDate?: (date: string) => void
  /** Entries shown before the "+N meer" overflow marker kicks in. */
  maxVisible?: number
  /** Injectable for tests; defaults to now. */
  today?: Date
}

/**
 * Month calendar: 7 columns (ma-zo), leading + trailing pad cells so every row is a full week,
 * today highlighted, up to `maxVisible` entries per cell plus a "+N meer" overflow marker.
 * Cells are buttons (whole-cell click -> `onSelectDate`); entries themselves are rendered via
 * `renderEntry` and are expected to stay non-interactive here (buttons cannot nest) — the
 * week/list views are where entries become individually clickable.
 *
 * Deliberately plain markup, no `role="grid"`/`"row"`/`"gridcell"`: the APG grid pattern requires
 * a roving-tabindex/arrow-key implementation we don't have, and a bare `role="grid"` without one
 * is worse than no role (screen readers announce grid navigation that doesn't work, and it would
 * make every one of the 42 cells a fresh Tab stop either way). Cells stay plain `<button>`s with
 * the nl-BE `aria-label`, giving standard Tab/Enter/Space operability.
 */
export function MonthGrid<T>({
  anchor,
  entriesByDate,
  renderEntry,
  onSelectDate,
  maxVisible = 2,
  today = new Date(),
}: MonthGridProps<T>) {
  const { t } = useLocale()
  const month = anchor.getMonth()
  const { start, end } = monthGridRange(anchor)
  const totalCells = Math.round((end.getTime() - start.getTime()) / 86_400_000) + 1
  const todayIso = toIsoDate(today)

  const cells = Array.from({ length: totalCells }, (_, index) => addDays(start, index))

  return (
    <div className="cal-month">
      <div className="cal-month-headerrow">
        {getDayNames().map((name) => (
          <div key={name} className="cal-month-header">
            {name}
          </div>
        ))}
      </div>
      <div className="cal-month-grid">
        {cells.map((date) => {
          const iso = toIsoDate(date)
          const entries = entriesByDate.get(iso) ?? []
          const inMonth = date.getMonth() === month
          const isToday = iso === todayIso
          const overflow = entries.length - maxVisible
          return (
            <button
              type="button"
              key={iso}
              className={`cal-month-cell${inMonth ? '' : ' cal-month-cell-outside'}${isToday ? ' cal-today' : ''}`}
              onClick={onSelectDate ? () => onSelectDate(iso) : undefined}
              aria-label={cellAriaLabel(date, entries.length)}
            >
              <span className="cal-month-daynr">{date.getDate()}</span>
              <span className="cal-month-entries">
                {entries.slice(0, maxVisible).map((entry, index) => (
                  <span className="cal-month-entry" key={index}>
                    {renderEntry(entry, { date: iso })}
                  </span>
                ))}
              </span>
              {overflow > 0 && <span className="cal-month-more">{t('ui.calendar.more', { count: overflow })}</span>}
            </button>
          )
        })}
      </div>
    </div>
  )
}
