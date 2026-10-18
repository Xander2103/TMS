import type { ReactNode } from 'react'
import { LoadingState } from '../feedback/LoadingState'
import { ErrorState } from '../feedback/ErrorState'
import { EmptyState } from './EmptyState'
import { useLocale } from '../../i18n/localeContext'
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
  /** Class applied to the header AND every cell of the column (stable width/visibility hooks for CSS). */
  className?: string
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
  /**
   * The error behind `error` (usePagedQuery's errorCause): a 403 renders "Geen toegang", a 404
   * "Niet gevonden", and neither offers "Opnieuw proberen".
   */
  errorCause?: unknown
  emptyMessage?: string
  /** Next step offered inside the empty state (e.g. a "Nieuw dossier" button). */
  emptyAction?: ReactNode
  /** Re-runs the failed load; the error state offers "Opnieuw proberen" when given. */
  onRetry?: () => void
  loadingMessage?: string
  /** Invoked when a row is clicked; makes rows keyboard-focusable. */
  onRowClick?: (row: TRow) => void
  /** Optional extra class name per row, e.g. greying out inactive records. */
  rowClassName?: (row: TRow) => string | undefined
  /** Optional DOM id per row, so callers can deep-link/scroll to one row (`getElementById`). */
  rowId?: (row: TRow) => string | undefined
  /** Current (server-side) sort; columns with a `sortKey` become clickable when `onSortChange` is set. */
  sort?: SortState | null
  /** Called with the next sort when a sortable header is clicked (same column toggles direction). */
  onSortChange?: (sort: SortState) => void
  /**
   * B34: `table-layout: fixed` — columns without a width share the remaining space and long text
   * truncates (TruncatedText) instead of widening the table past its container.
   */
  fixedLayout?: boolean
  /** B10: row density — compact rows for operational lists the planner scans all day. */
  density?: 'compact' | 'normal'
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
  errorCause,
  emptyMessage,
  emptyAction,
  onRetry,
  loadingMessage,
  onRowClick,
  rowClassName,
  rowId,
  sort,
  onSortChange,
  fixedLayout = false,
  density = 'normal',
}: DataTableProps<TRow>) {
  const { t } = useLocale()
  if (isLoading) return <LoadingState message={loadingMessage ?? t('ui.table.loading')} />
  if (error) return <ErrorState message={error} error={errorCause} onRetry={onRetry} />
  if (rows.length === 0) return <EmptyState message={emptyMessage ?? t('ui.table.empty')} action={emptyAction} />

  function handleSortClick(sortKey: string) {
    if (!onSortChange) return
    const nextDir: 'asc' | 'desc' = sort?.key === sortKey && sort.dir === 'asc' ? 'desc' : 'asc'
    onSortChange({ key: sortKey, dir: nextDir })
  }

  return (
    <div className="data-table-wrapper">
      <table className={['data-table', fixedLayout ? 'data-table-fixed' : '', density === 'compact' ? 'data-table-compact' : ''].filter(Boolean).join(' ')}>
        <thead>
          <tr>
            {columns.map((column) => {
              const sortable = Boolean(column.sortKey && onSortChange)
              const isSorted = sortable && sort?.key === column.sortKey
              return (
                <th
                  key={column.key}
                  className={column.className}
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
                id={rowId?.(row)}
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
                  <td key={column.key} className={column.className} style={{ textAlign: column.align ?? 'left' }}>
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
