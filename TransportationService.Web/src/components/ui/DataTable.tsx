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
}: DataTableProps<TRow>) {
  if (isLoading) return <LoadingState message={loadingMessage} />
  if (error) return <ErrorState message={error} />
  if (rows.length === 0) return <EmptyState message={emptyMessage} />

  return (
    <div className="data-table-wrapper">
      <table className="data-table">
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column.key} style={{ textAlign: column.align ?? 'left', width: column.width }}>
                {column.header}
              </th>
            ))}
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
