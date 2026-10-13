import { useCallback, useMemo, useRef } from 'react'
import { Link } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import { useVirtualRows } from '../hooks/useVirtualRows'
import type { ScheduleEntry, ScheduleRow } from '../types'
import { ScheduleChip } from './ScheduleChip'

interface ListEntry {
  key: string
  row: ScheduleRow
  date: string
  entry: ScheduleEntry
}

interface PlanningListViewProps {
  rows: ScheduleRow[]
  canViewEmployees: boolean
  chipAction: (entry: ScheduleEntry, row: ScheduleRow) => (() => void) | undefined
  maxHeight?: string
}

const LIST_ROW_HEIGHT = 44

/**
 * Flat list of every entry of the SAME selection as the grid (one state, one query): employee,
 * date, state chip, time, location. Virtualized as well — a 500-person month is thousands of rows.
 */
export function PlanningListView({ rows, canViewEmployees, chipAction, maxHeight }: PlanningListViewProps) {
  const { t, formatDate } = useLocale()
  const scrollRef = useRef<HTMLDivElement>(null)

  const entries = useMemo<ListEntry[]>(
    () =>
      rows.flatMap((row) =>
        row.days.flatMap((day) =>
          day.entries.map((entry, index) => ({ key: `${row.employeeId}-${day.date}-${index}`, row, date: day.date, entry })),
        ),
      ),
    [rows],
  )

  const estimateRow = useCallback(() => LIST_ROW_HEIGHT, [])
  const rowKey = useCallback((index: number) => entries[index]?.key ?? String(index), [entries])
  const { start, end, topPad, bottomPad, active, measure } = useVirtualRows({
    scrollRef,
    rowCount: entries.length,
    estimateRow,
    rowKey,
    threshold: 100,
  })

  if (entries.length === 0) {
    return <p className="ep-empty">{t('employeePlanning.empty.noEntries')}</p>
  }

  return (
    <div className="ep-grid-wrap ep-list-wrap" ref={scrollRef} style={maxHeight ? { maxHeight } : undefined} data-virtualized={active ? 'true' : 'false'}>
      <table className="ep-grid ep-list">
        <thead>
          <tr>
            <th scope="col" className="ep-employee-cell ep-corner">{t('employeePlanning.page.colEmployee')}</th>
            <th scope="col">{t('employeePlanning.page.colDate')}</th>
            <th scope="col">{t('employeePlanning.page.colStatus')}</th>
            <th scope="col">{t('employeePlanning.page.colTime')}</th>
            <th scope="col">{t('employeePlanning.page.colLocation')}</th>
          </tr>
        </thead>
        <tbody>
          {topPad > 0 && (
            <tr className="ep-spacer" aria-hidden="true">
              <td colSpan={5} style={{ height: topPad }} />
            </tr>
          )}
          {entries.slice(start, end).map((item, offset) => (
            <tr key={item.key} ref={measure(start + offset)} className={item.row.matchesFilters === false ? 'ep-row-outside' : undefined}>
              <th scope="row" className="ep-employee-cell">
                {canViewEmployees ? (
                  <Link to={`/employees/${item.row.employeeId}`} className="ep-employee-link">
                    {item.row.employeeName}
                  </Link>
                ) : (
                  item.row.employeeName
                )}
              </th>
              <td>{formatDate(item.date)}</td>
              <td>
                <ScheduleChip entry={item.entry} onClick={chipAction(item.entry, item.row)} />
              </td>
              <td>
                {item.entry.startTime && item.entry.endTime
                  ? `${item.entry.startTime.slice(0, 5)}–${item.entry.endTime.slice(0, 5)}`
                  : '—'}
              </td>
              <td>{item.entry.workLocation ?? item.entry.vehicleSummary ?? '—'}</td>
            </tr>
          ))}
          {bottomPad > 0 && (
            <tr className="ep-spacer" aria-hidden="true">
              <td colSpan={5} style={{ height: bottomPad }} />
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}
