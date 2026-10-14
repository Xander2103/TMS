import { useCallback, useMemo, useRef } from 'react'
import { Link } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import { formatDurationMinutes } from '../../../utils/dates'
import type { ListDensity } from '../../../components/ui/listDensity'
import { useVirtualRows } from '../hooks/useVirtualRows'
import { estimatePlanningRowHeight } from '../rowHeights'
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
  /** B49: compact rows (one-line chips and employee cell) show roughly twice as many employees. */
  density?: ListDensity
}

/**
 * Employees × days. One scroll container carries the sticky day header, the sticky employee
 * column and row virtualization (only the rows around the viewport exist in the DOM once the
 * team is large), so 500 employees × 7 days never means 3,500 live cells.
 */
export function PlanningGrid({ rows, days, today, canManage, canViewEmployees, chipAction, onAddShift, maxHeight, density = 'normal' }: PlanningGridProps) {
  const { t } = useLocale()
  const scrollRef = useRef<HTMLDivElement>(null)

  const compact = density === 'compact'
  const estimateRow = useCallback(
    (index: number) => estimatePlanningRowHeight(rows[index], density, canManage),
    [rows, density, canManage],
  )
  const rowKey = useCallback((index: number) => rows[index]?.employeeId ?? String(index), [rows])
  const { start, end, topPad, bottomPad, active, measure } = useVirtualRows({
    scrollRef,
    rowCount: rows.length,
    estimateRow,
    rowKey,
    layoutKey: density,
  })

  const visible = useMemo(() => rows.slice(start, end), [rows, start, end])

  return (
    <div className="ep-grid-wrap" ref={scrollRef} style={maxHeight ? { maxHeight } : undefined} data-virtualized={active ? 'true' : 'false'}>
      <table className={compact ? 'ep-grid ep-grid-compact' : 'ep-grid'} data-density={density}>
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
            const groups = [row.departmentName, row.functionNames?.join(', ')].filter(Boolean).join(' · ') || '—'
            return (
              <tr key={row.employeeId} ref={measure(index)} className={outside ? 'ep-row-outside' : undefined} data-employee-id={row.employeeId}>
                <th scope="row" className="ep-employee-cell">
                  <div className="ep-employee-name" title={compact ? row.employeeName : undefined}>
                    {canViewEmployees ? (
                      <Link to={`/employees/${row.employeeId}`} className="ep-employee-link">
                        {row.employeeName}
                      </Link>
                    ) : (
                      row.employeeName
                    )}
                    {row.isActive === false && <span className="ep-inline-badge">{t('employeePlanning.picker.inactiveBadge')}</span>}
                    {compact && outside && <span className="ep-inline-badge ep-inline-badge-outside">{t('employeePlanning.chips.outsideFilters')}</span>}
                  </div>
                  <div className="ep-employee-meta" title={compact ? groups : undefined}>
                    {/* The planned total never truncates: in compact only the groups part gets the ellipsis. */}
                    <span className="ep-employee-groups">{groups}</span>
                    <span className="ep-employee-total">
                      {' · '}
                      {t('employeePlanning.meta.planned', { duration: formatDurationMinutes(row.plannedMinutes) })}
                    </span>
                  </div>
                  {!compact && outside && <span className="ep-inline-badge ep-inline-badge-outside">{t('employeePlanning.chips.outsideFilters')}</span>}
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
                          <button
                            type="button"
                            className="ep-add-shift"
                            onClick={() => onAddShift(row, day.date)}
                            aria-label={compact ? t('employeePlanning.page.addShift') : undefined}
                            title={compact ? t('employeePlanning.page.addShift') : undefined}
                          >
                            {compact ? '+' : t('employeePlanning.page.addShift')}
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
