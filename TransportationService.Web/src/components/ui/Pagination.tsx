import './Pagination.css'

interface PaginationProps {
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

/** Shared pager. Renders nothing when everything fits on a single page. */
export function Pagination({ page, pageSize, totalCount, onPageChange }: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  if (totalPages <= 1) {
    return null
  }

  const rangeStart = (page - 1) * pageSize + 1
  const rangeEnd = Math.min(page * pageSize, totalCount)

  return (
    <nav className="ui-pagination" aria-label="Paginering">
      <span className="ui-pagination-summary">
        {rangeStart}–{rangeEnd} van {totalCount}
      </span>
      <div className="ui-pagination-controls">
        <button type="button" onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
          Vorige
        </button>
        <span>
          Pagina {page} / {totalPages}
        </span>
        <button type="button" onClick={() => onPageChange(page + 1)} disabled={page >= totalPages}>
          Volgende
        </button>
      </div>
    </nav>
  )
}
