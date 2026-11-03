import type { ReactNode } from 'react'
import { ArrowUpDown, ChevronLeft, ChevronRight, Search } from 'lucide-react'
import { useLocale } from '../../../i18n/localeContext'
import { PAGE_SIZE_OPTIONS, pageNumbers, type HistorySort, type ListPaging } from './historyListState'
import './dossier-history.css'

interface HistoryCardHeaderProps {
  icon: ReactNode
  title: string
  subtitle: string
  action?: ReactNode
}

/** Icon tile, title + one explaining line, and at most one action. */
export function HistoryCardHeader({ icon, title, subtitle, action }: HistoryCardHeaderProps) {
  return (
    <div className="dossier-history-card-head">
      <span className="dossier-history-card-icon" aria-hidden="true">{icon}</span>
      <div className="dossier-history-card-title">
        <h3>{title}</h3>
        <p>{subtitle}</p>
      </div>
      {action}
    </div>
  )
}

interface HistoryListToolbarProps {
  search: string
  onSearch: (value: string) => void
  searchPlaceholder: string
  sort: HistorySort
  onSort: (sort: HistorySort) => void
  /** E.g. the type filter of the change history, between the search field and the sort. */
  filter?: ReactNode
  disabled?: boolean
}

export function HistoryListToolbar({ search, onSearch, searchPlaceholder, sort, onSort, filter, disabled = false }: HistoryListToolbarProps) {
  const { t } = useLocale()
  return (
    <div className="dossier-history-toolbar">
      <label className="dossier-history-search">
        <Search size={16} aria-hidden="true" />
        <input
          type="search"
          value={search}
          placeholder={searchPlaceholder}
          aria-label={searchPlaceholder}
          disabled={disabled}
          onChange={(event) => onSearch(event.target.value)}
        />
      </label>
      {filter}
      <label className="dossier-history-select">
        <ArrowUpDown size={15} aria-hidden="true" />
        <select
          value={sort}
          aria-label={t('dossierNotes.list.sortLabel')}
          disabled={disabled}
          onChange={(event) => onSort(event.target.value === 'oldest' ? 'oldest' : 'newest')}
        >
          <option value="newest">{t('dossierNotes.list.newest')}</option>
          <option value="oldest">{t('dossierNotes.list.oldest')}</option>
        </select>
      </label>
    </div>
  )
}

interface HistoryListFooterProps {
  /** "1–20 van 42 wijzigingen". */
  summary: string
  paging: ListPaging
  disabled?: boolean
}

/** Range, page size (20 by default) and, once there is more than one page, the numbered pager. */
export function HistoryListFooter({ summary, paging, disabled = false }: HistoryListFooterProps) {
  const { t } = useLocale()
  const { page, pageSize, totalPages } = paging
  return (
    <div className="dossier-history-footer">
      <span className="dossier-history-range">{summary}</span>
      <label className="dossier-history-select">
        <select
          value={pageSize}
          aria-label={t('dossierNotes.list.pageSizeLabel')}
          disabled={disabled}
          onChange={(event) => paging.setPageSize(Number(event.target.value))}
        >
          {PAGE_SIZE_OPTIONS.map((size) => (
            <option key={size} value={size}>{t('dossierNotes.list.perPage', { size })}</option>
          ))}
        </select>
      </label>
      {totalPages > 1 && (
        <nav className="dossier-history-pager" aria-label={t('ui.pagination.label')}>
          <button type="button" aria-label={t('ui.pagination.previous')} disabled={disabled || page <= 1} onClick={() => paging.setPage(page - 1)}>
            <ChevronLeft size={16} aria-hidden="true" />
          </button>
          {pageNumbers(page, totalPages).map((entry, index) =>
            entry === 'gap' ? (
              <span key={`gap-${index}`} className="dossier-history-pager-gap" aria-hidden="true">…</span>
            ) : (
              <button
                key={entry}
                type="button"
                aria-label={t('dossierNotes.list.pageNumber', { page: entry })}
                aria-current={entry === page ? 'page' : undefined}
                disabled={disabled}
                onClick={() => paging.setPage(entry)}
              >
                {entry}
              </button>
            ),
          )}
          <button type="button" aria-label={t('ui.pagination.next')} disabled={disabled || page >= totalPages} onClick={() => paging.setPage(page + 1)}>
            <ChevronRight size={16} aria-hidden="true" />
          </button>
        </nav>
      )}
    </div>
  )
}
