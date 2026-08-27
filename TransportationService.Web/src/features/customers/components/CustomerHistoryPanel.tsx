import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Pagination } from '../../../components/ui/Pagination'
import { getCustomerHistory, type CustomerHistoryPage } from '../api/customerHistoryApi'
import { formatDateTime } from '../../../utils/dates'
import { useLocale } from '../../../i18n/localeContext'
import './CustomerHistoryPanel.css'

const PAGE_SIZE = 25

/**
 * Chip order: "all" (no filter) first, then every backend history category. The values are the
 * API's stable category CODES — used both as the `category` query parameter and as the
 * translation key suffix (customers.history.categories.<code>). The endpoint also still
 * accepts the legacy Dutch labels, but new code always sends the code.
 */
const CATEGORY_FILTERS = ['all', 'customer', 'contacts', 'locations', 'billing', 'communication'] as const

/** Category chip tones (keyed by category code) — informational only, no strict semantics. */
const CATEGORY_TONES: Record<string, 'info' | 'neutral' | 'success' | 'warning'> = {
  customer: 'info',
  contacts: 'success',
  locations: 'neutral',
  billing: 'warning',
  communication: 'info',
}

/**
 * Readable customer history (mirrors EmployeeHistoryPanel): one card per audited change,
 * newest first, collapsed to a summary line — the actor, the category and (on demand)
 * the full field/before/after table. Ids are resolved to names server-side; nothing raw is
 * shown. Category filtering and display key off the API's stable `categoryCode`.
 */
export function CustomerHistoryPanel({ customerId }: { customerId: string }) {
  const { t } = useLocale()
  const [data, setData] = useState<CustomerHistoryPage | null>(null)
  const [page, setPage] = useState(1)
  const [category, setCategory] = useState<string | null>(null)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    getCustomerHistory(customerId, page, PAGE_SIZE, category)
      .then((result) => {
        setData(result)
        setError(null)
      })
      .catch(() => setError('customers.history.loadFailed'))
  }, [customerId, page, category])

  useEffect(() => {
    reload()
  }, [reload])

  const selectCategory = (next: string) => {
    setCategory(next === 'all' ? null : next)
    setPage(1)
  }

  const toggleExpanded = (id: string) => {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }

  return (
    <div className="customer-history">
      <div className="customer-history-filters" role="group" aria-label={t('customers.history.filterLabel')}>
        {CATEGORY_FILTERS.map((filter) => (
          <button
            key={filter}
            type="button"
            className={`customer-history-filter-chip${(category ?? 'all') === filter ? ' customer-history-filter-chip-active' : ''}`}
            aria-pressed={(category ?? 'all') === filter}
            onClick={() => selectCategory(filter)}
          >
            {t(`customers.history.categories.${filter}`)}
          </button>
        ))}
      </div>

      {error && <p className="placeholder-text">{t(error)}</p>}
      {!error && data === null && <p className="placeholder-text">{t('customers.history.loading')}</p>}
      {!error && data !== null && data.items.length === 0 && (
        <p className="placeholder-text">{t('customers.history.empty')}</p>
      )}
      {!error && data !== null && data.items.length > 0 && (
        <>
          {data.items.map((entry) => {
            const isExpanded = expanded.has(entry.id)
            const canExpand = entry.changes.length > 0
            return (
              <article key={entry.id} className="customer-history-entry">
                <header className="customer-history-header">
                  <span className="customer-history-when">{formatDateTime(entry.timestamp)}</span>
                  <span className="customer-history-actor">
                    {t('customers.history.actorLine', {
                      action: entry.actionLabel,
                      name: entry.userName ?? t('customers.history.system'),
                    })}
                  </span>
                  <Badge tone={CATEGORY_TONES[entry.categoryCode] ?? 'neutral'}>
                    {t(`customers.history.categories.${entry.categoryCode}`)}
                  </Badge>
                </header>
                <p className="customer-history-summary">{entry.summary}</p>
                {canExpand && (
                  <button
                    type="button"
                    className="customer-history-toggle"
                    aria-expanded={isExpanded}
                    onClick={() => toggleExpanded(entry.id)}
                  >
                    {isExpanded ? t('customers.history.collapse') : t('customers.history.expand')}
                  </button>
                )}
                {canExpand && isExpanded && (
                  <table className="customer-history-table">
                    <thead>
                      <tr>
                        <th>{t('customers.history.columnField')}</th>
                        <th>{t('customers.history.columnBefore')}</th>
                        <th>{t('customers.history.columnAfter')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {entry.changes.map((change, index) => (
                        <tr key={`${entry.id}-${index}`}>
                          <td>{change.field}</td>
                          <td>{change.before ?? '—'}</td>
                          <td>{change.after ?? '—'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </article>
            )
          })}
          <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPageChange={setPage} />
        </>
      )}
    </div>
  )
}
