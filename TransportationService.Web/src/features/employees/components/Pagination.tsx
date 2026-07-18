import './Pagination.css'

interface PaginationProps {
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

export function Pagination({ page, pageSize, totalCount, onPageChange }: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  if (totalPages <= 1) {
    return null
  }

  return (
    <nav className="pagination" aria-label="Paginering">
      <button type="button" onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
        Vorige
      </button>
      <span>
        Pagina {page} van {totalPages}
      </span>
      <button type="button" onClick={() => onPageChange(page + 1)} disabled={page >= totalPages}>
        Volgende
      </button>
    </nav>
  )
}
