import { useMemo, useState, type ReactNode } from 'react'
import { ArrowUpDown, Search } from 'lucide-react'
import { EmptyState } from '../../../components/ui/EmptyState'
import { useLocale } from '../../../i18n/localeContext'
import type { DossierDocument } from '../api/dossierDocumentsApi'
import { DocumentPager } from './DocumentPager'
import { DossierDocumentTable, type DossierDocumentRowActions } from './DossierDocumentTable'
import {
  DEFAULT_DOCUMENT_PAGE_SIZE,
  DOCUMENT_PAGE_SIZES,
  DOCUMENT_SORTS,
  documentTypeLabel,
  filterAndSortDocuments,
  type DocumentPortalFilter,
  type DocumentSort,
} from './documentList'

interface DossierDocumentListProps {
  documents: DossierDocument[]
  actions?: DossierDocumentRowActions
  emptyText: string
  emptyAction?: ReactNode
}

/** The documents of one level: search, type/portal filters and sorting above the table, paging below it. */
export function DossierDocumentList({ documents, actions, emptyText, emptyAction }: DossierDocumentListProps) {
  const { t, locale } = useLocale()
  const [search, setSearch] = useState('')
  const [type, setType] = useState('')
  const [portal, setPortal] = useState<DocumentPortalFilter>('all')
  const [sort, setSort] = useState<DocumentSort>('newest')
  const [pageSize, setPageSize] = useState(DEFAULT_DOCUMENT_PAGE_SIZE)
  const [page, setPage] = useState(1)

  const typeOptions = useMemo(
    () => [...new Set(documents.map((document) => documentTypeLabel(document, t)))].sort((a, b) => a.localeCompare(b, locale)),
    [documents, t, locale],
  )
  const shown = useMemo(
    () => filterAndSortDocuments(documents, { search, type, portal, sort }, t, locale),
    [documents, search, type, portal, sort, t, locale],
  )

  if (documents.length === 0) return <DossierDocumentTable documents={[]} emptyText={emptyText} emptyAction={emptyAction} />

  // A filter or a delete can shrink the list under the current page.
  const totalPages = Math.max(1, Math.ceil(shown.length / pageSize))
  const current = Math.min(page, totalPages)
  const start = (current - 1) * pageSize
  const visible = shown.slice(start, start + pageSize)

  function filterChange<T>(setter: (value: T) => void) {
    return (value: T) => {
      setter(value)
      setPage(1)
    }
  }

  function resetFilters() {
    setSearch('')
    setType('')
    setPortal('all')
    setPage(1)
  }

  return (
    <>
      <div className="ddoc-filters" role="search">
        <label className="ddoc-search">
          <Search size={16} aria-hidden />
          <input
            type="search"
            value={search}
            onChange={(event) => filterChange(setSearch)(event.target.value)}
            placeholder={t('dossierDocuments.filters.searchPlaceholder')}
            aria-label={t('dossierDocuments.filters.search')}
          />
        </label>
        <select className="ddoc-filter" value={type} onChange={(event) => filterChange(setType)(event.target.value)} aria-label={t('dossierDocuments.filters.type')}>
          <option value="">{t('dossierDocuments.filters.typeAll')}</option>
          {typeOptions.map((option) => <option key={option} value={option}>{option}</option>)}
        </select>
        <select className="ddoc-filter" value={portal} onChange={(event) => filterChange(setPortal)(event.target.value as DocumentPortalFilter)} aria-label={t('dossierDocuments.filters.portal')}>
          <option value="all">{t('dossierDocuments.filters.portalAll')}</option>
          <option value="visible">{t('dossierDocuments.visibility.visible')}</option>
          <option value="internal">{t('dossierDocuments.visibility.internal')}</option>
        </select>
        <label className="ddoc-sort">
          <ArrowUpDown size={15} aria-hidden />
          <select className="ddoc-filter" value={sort} onChange={(event) => filterChange(setSort)(event.target.value as DocumentSort)} aria-label={t('dossierDocuments.filters.sort')}>
            {DOCUMENT_SORTS.map((option) => <option key={option} value={option}>{t(`dossierDocuments.filters.sortBy.${option}`)}</option>)}
          </select>
        </label>
      </div>

      {shown.length === 0 ? (
        <EmptyState
          message={t('dossierDocuments.filters.noResults')}
          action={<button type="button" className="link-button" onClick={resetFilters}>{t('dossierDocuments.filters.reset')}</button>}
        />
      ) : (
        <>
          <DossierDocumentTable documents={visible} actions={actions} emptyText={emptyText} />
          <div className="ddoc-list-footer">
            <span className="ddoc-summary">
              {t('dossierDocuments.range', { start: start + 1, end: start + visible.length, count: shown.length })}
            </span>
            {shown.length > DEFAULT_DOCUMENT_PAGE_SIZE && (
              <span className="ddoc-list-paging">
                <select
                  className="ddoc-filter"
                  value={pageSize}
                  onChange={(event) => filterChange(setPageSize)(Number(event.target.value))}
                  aria-label={t('dossierDocuments.filters.pageSize')}
                >
                  {DOCUMENT_PAGE_SIZES.map((size) => <option key={size} value={size}>{t('dossierDocuments.filters.perPage', { size })}</option>)}
                </select>
                {totalPages > 1 && <DocumentPager page={current} totalPages={totalPages} onPageChange={setPage} />}
              </span>
            )}
          </div>
        </>
      )}
    </>
  )
}
