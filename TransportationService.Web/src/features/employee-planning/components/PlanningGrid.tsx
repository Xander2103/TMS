import { useCallback, useMemo, useRef } from 'react'
import { Link } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import { formatDurationMinutes } from '../../../utils/dates'
import { useVirtualRows } from '../hooks/useVirtualRows'
import type { ScheduleEntry, ScheduleRow } from '../types'
import { ScheduleChip } from './ScheduleChip'

import { DAY_KEYS, dayIndexOf } from '../planningDays'

interface PlanningGridProps {
  rows: ScheduleRow[]
  days: string[]
  today: string
  canManage: boolean
  canViewEmployees: boolean
  chipAction: (entry: ScheduleEntry, row: ScheduleRow) => (() => void) | undefined
  onAddShift: (row: ScheduleRow, date: string) => void
  /** Hard cap for the scroll container so the grid is one scrollable work surface. */
  maxHeight?: string
}

const ROW_BASE_HEIGHT = 50
const CHIP_HEIGHT = 26
const ADD_BUTTON_HEIGHT = 22

/**
 * Employees × days. One scroll container carries the sticky day header, the sticky employee
 * column and row virtualization (only the rows around the viewport exist in the DOM once the
 * team is large), so 500 employees × 7 days never means 3,500 live cells.
 */
export function PlanningGrid({ rows, days, today, canManage, canViewEmployees, chipAction, onAddShift, maxHeight }: PlanningGridProps) {
  const { t } = useLocale()
  const scrollRef = useRef<HTMLDivElement>(null)

  const estimateRow = useCallback(
    (index: number) => {
      const row = rows[index]
      const maxEntries = row ? Math.max(1, ...row.days.map((d) => d.entries.length)) : 1
      return ROW_BASE_HEIGHT + Math.max(0, maxEntries - 1) * CHIP_HEIGHT + (canManage ? ADD_BUTTON_HEIGHT : 0)
    },
    [rows, canManage],
  )
  const rowKey = useCallback((index: number) => rows[index]?.employeeId ?? String(index), [rows])
  const { start, end, topPad, bottomPad, active, measure } = useVirtualRows({
    scrollRef,
    rowCount: rows.length,
    estimateRow,
    rowKey,
  })

  const visible = useMemo(() => rows.slice(start, end), [rows, start, end])

  return (
    <div className="ep-grid-wrap" ref={scrollRef} style={maxHeight ? { maxHeight } : undefined} data-virtualized={active ? 'true' : 'false'}>
      <table className="ep-grid">
        <thead>
          <tr>
            <th className="ep-employee-cell ep-corner" scope="col">{t('employeePlanning.page.colEmployee')}</th>
            {days.map((date) => {
              const dayIndex = dayIndexOf(date)
              return (
                <th key={date} scope="col" className={`${dayIndex >= 5 ? 'ep-weekend' : ''} ${date === today ? 'ep-today' : ''}`}>
                  {t(DAY_KEYS[dayIndex])} {date.slice(8, 10)}/{date.slice(5, 7)}
                </th>
              )
            })}
          </tr>
        </thead>
        <tbody>
          {topPad > 0 && (
            <tr className="ep-spacer" aria-hidden="true">
              <td colSpan={days.length + 1} style={{ height: topPad }} />
            </tr>
          )}
          {visible.map((row, offset) => {
            const index = start + offset
            const outside = row.matchesFilters === false
            return (
              <tr key={row.employeeId} ref={measure(index)} className={outside ? 'ep-row-outside' : undefined} data-employee-id={row.employeeId}>
                <th scope="row" className="ep-employee-cell">
                  <div className="ep-employee-name">
                    {canViewEmployees ? (
                      <Link to={`/employees/${row.employeeId}`} className="ep-employee-link">
                        {row.employeeName}
                      </Link>
                    ) : (
                      row.employeeName
                    )}
                    {row.isActive === false && <span className="ep-inline-badge">{t('employeePlanning.picker.inactiveBadge')}</span>}
                  </div>
                  <div className="ep-employee-meta">
                    {[row.departmentName, row.functionNames?.join(', ')].filter(Boolean).join(' · ') || '—'}
                    {' · '}
                    {t('employeePlanning.meta.planned', { duration: formatDurationMinutes(row.plannedMinutes) })}
                  </div>
                  {outside && <span className="ep-inline-badge ep-inline-badge-outside">{t('employeePlanning.chips.outsideFilters')}</span>}
                </th>
                {row.days.map((day) => {
                  const dayIndex = dayIndexOf(day.date)
                  return (
                    <td key={day.date} className={`ep-day-cell ${dayIndex >= 5 ? 'ep-weekend' : ''}`}>
                      <div className="ep-day-cell-inner">
                        {day.entries.map((entry, entryIndex) => (
                          <ScheduleChip key={entryIndex} entry={entry} onClick={chipAction(entry, row)} />
                        ))}
                        {canManage && (
                          <button type="button" className="ep-add-shift" onClick={() => onAddShift(row, day.date)}>
                            {t('employeePlanning.page.addShift')}
                          </button>
                        )}
                      </div>
                    </td>
                  )
                })}
              </tr>
            )
          })}
          {bottomPad > 0 && (
            <tr className="ep-spacer" aria-hidden="true">
              <td colSpan={days.length + 1} style={{ height: bottomPad }} />
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}
