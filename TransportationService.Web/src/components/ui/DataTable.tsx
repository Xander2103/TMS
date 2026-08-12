import type { ReactNode } from 'react'
import { LoadingState } from '../feedback/LoadingState'
import { ErrorState } from '../feedback/ErrorState'
import { EmptyState } from './EmptyState'
import './DataTable.css'

export interface Column<TRow> {
  /** Stable key for the column. */
  key: string
  header: ReactNode
  /** Cell renderer for a row. */
  render: (row: TRow) => ReactNode
  /** Optional alignment; defaults to left. */
  align?: 'left' | 'right' | 'center'
  /** Optional fixed width (CSS value). */
  width?: string
  /**
   * Server-side sort key for this column. When set — together with the table-level
   * `sort`/`onSortChange` props — the header renders as a sort button with `aria-sort`.
   */
  sortKey?: string
}

export interface SortState {
  key: string
  dir: 'asc' | 'desc'
}

interface DataTableProps<TRow> {
  columns: Column<TRow>[]
  rows: TRow[]
  rowKey: (row: TRow) => string
  isLoading?: boolean
  error?: string | null
  emptyMessage?: string
  loadingMessage?: string
  /** Invoked when a row is clicked; makes rows keyboard-focusable. */
  onRowClick?: (row: TRow) => void
  /** Optional extra class name per row, e.g. greying out inactive records. */
  rowClassName?: (row: TRow) => string | undefined
  /** Current (server-side) sort; columns with a `sortKey` become clickable when `onSortChange` is set. */
  sort?: SortState | null
  /** Called with the next sort when a sortable header is clicked (same column toggles direction). */
  onSortChange?: (sort: SortState) => void
}

/**
 * Generic, accessible table used by every list screen. Owns its own loading / error / empty
 * presentation so callers never re-implement those states.
 */
export function DataTable<TRow>({
  columns,
  rows,
  rowKey,
  isLoading = false,
  error = null,
  emptyMessage = 'Geen gegevens gevonden.',
  loadingMessage = 'Laden...',
  onRowClick,
  rowClassName,
  sort,
  onSortChange,
}: DataTableProps<TRow>) {
  if (isLoading) return <LoadingState message={loadingMessage} />
  if (error) return <ErrorState message={error} />
  if (rows.length === 0) return <EmptyState message={emptyMessage} />

  function handleSortClick(sortKey: string) {
    if (!onSortChange) return
    const nextDir: 'asc' | 'desc' = sort?.key === sortKey && sort.dir === 'asc' ? 'desc' : 'asc'
    onSortChange({ key: sortKey, dir: nextDir })
  }

  return (
    <div className="data-table-wrapper">
      <table className="data-table">
        <thead>
          <tr>
            {columns.map((column) => {
              const sortable = Boolean(column.sortKey && onSortChange)
              const isSorted = sortable && sort?.key === column.sortKey
              return (
                <th
                  key={column.key}
                  style={{ textAlign: column.align ?? 'left', width: column.width }}
                  aria-sort={isSorted ? (sort!.dir === 'asc' ? 'ascending' : 'descending') : undefined}
                >
                  {sortable ? (
                    <button
                      type="button"
                      className="data-table-sort-button"
                      onClick={() => handleSortClick(column.sortKey!)}
                    >
                      {column.header}
                      <span className="data-table-sort-indicator" aria-hidden="true">
                        {isSorted ? (sort!.dir === 'asc' ? '▲' : '▼') : '↕'}
                      </span>
                    </button>
                  ) : (
                    column.header
                  )}
                </th>
              )
            })}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const clickable = Boolean(onRowClick)
            const className = [clickable ? 'data-table-row-clickable' : undefined, rowClassName?.(row)]
              .filter(Boolean)
              .join(' ')
            return (
              <tr
                key={rowKey(row)}
                className={className || undefined}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
                tabIndex={clickable ? 0 : undefined}
                onKeyDown={
                  onRowClick
                    ? (event) => {
                        if (event.key === 'Enter') onRowClick(row)
                      }
                    : undefined
                }
              >
                {columns.map((column) => (
                  <td key={column.key} style={{ textAlign: column.align ?? 'left' }}>
                    {column.render(row)}
                  </td>
                ))}
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
