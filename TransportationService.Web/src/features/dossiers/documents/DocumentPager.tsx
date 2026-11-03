import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useLocale } from '../../../i18n/localeContext'

/** First, last and the neighbours of the current page; null marks a gap. */
function visiblePages(page: number, totalPages: number): (number | null)[] {
  const pages: (number | null)[] = []
  for (let candidate = 1; candidate <= totalPages; candidate++) {
    if (candidate === 1 || candidate === totalPages || Math.abs(candidate - page) <= 1) pages.push(candidate)
    else if (pages[pages.length - 1] !== null) pages.push(null)
  }
  return pages
}

export function DocumentPager({ page, totalPages, onPageChange }: { page: number; totalPages: number; onPageChange: (page: number) => void }) {
  const { t } = useLocale()
  return (
    <nav className="ddoc-pager" aria-label={t('ui.pagination.label')}>
      <button type="button" aria-label={t('ui.pagination.previous')} disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
        <ChevronLeft size={16} aria-hidden />
      </button>
      {visiblePages(page, totalPages).map((candidate, index) =>
        candidate === null ? (
          <span key={`gap-${index}`} className="ddoc-pager-gap" aria-hidden>…</span>
        ) : (
          <button
            key={candidate}
            type="button"
            className={candidate === page ? 'is-active' : undefined}
            aria-current={candidate === page ? 'page' : undefined}
            onClick={() => onPageChange(candidate)}
          >
            {candidate}
          </button>
        ),
      )}
      <button type="button" aria-label={t('ui.pagination.next')} disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
        <ChevronRight size={16} aria-hidden />
      </button>
    </nav>
  )
}
