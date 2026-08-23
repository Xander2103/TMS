import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Pagination } from '../../../components/ui/Pagination'
import { getEmployeeHistory, type EmployeeHistoryPage } from '../api/employeeHistoryApi'
import { formatDateTime } from '../../../utils/dates'
import { useLocale } from '../../../i18n/localeContext'
import './EmployeeHistoryPanel.css'

const PAGE_SIZE = 25

/**
 * Chip order: "all" (no filter) first, then every dossier section in nav order. The values are
 * the API's stable category CODES — used both as the `category` query parameter and as the
 * translation key suffix (employees.history.categories.<code>).
 */
const CATEGORY_FILTERS = [
  'all',
  'profile',
  'qualifications',
  'documents',
  'notes',
  'issued_items',
  'absences',
  'leave_balance',
  'driver_profile',
] as const

/** Category chip tones (keyed by category code) — informational only, no strict semantics. */
const CATEGORY_TONES: Record<string, 'info' | 'neutral' | 'success' | 'warning'> = {
  profile: 'info',
  qualifications: 'success',
  documents: 'neutral',
  notes: 'info',
  absences: 'warning',
  leave_balance: 'warning',
  issued_items: 'neutral',
  driver_profile: 'info',
}

/**
 * Complete readable personnel history (corrections wave §4): one card per audited save, newest
 * first, collapsed by default to a single summary line — the actor, the dossier section
 * and (on demand) the full field/before/after table. Old partial audit entries render through
 * the same endpoint with fewer field rows; unmapped/legacy id fields resolve to names
 * server-side. Category filtering and display key off the API's stable `categoryCode`.
 */
export function EmployeeHistoryPanel({ employeeId }: { employeeId: string }) {
  const { t } = useLocale()
  const [data, setData] = useState<EmployeeHistoryPage | null>(null)
  const [page, setPage] = useState(1)
  const [category, setCategory] = useState<string | null>(null)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    getEmployeeHistory(employeeId, page, PAGE_SIZE, category)
      .then((result) => {
        setData(result)
        setError(null)
      })
      .catch(() => setError('employees.history.loadFailed'))
  }, [employeeId, page, category])

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
    <div className="employee-history">
      <div className="employee-history-filters" role="group" aria-label={t('employees.history.filterLabel')}>
        {CATEGORY_FILTERS.map((filter) => (
          <button
            key={filter}
            type="button"
            className={`employee-history-filter-chip${(category ?? 'all') === filter ? ' employee-history-filter-chip-active' : ''}`}
            aria-pressed={(category ?? 'all') === filter}
            onClick={() => selectCategory(filter)}
          >
            {t(`employees.history.categories.${filter}`)}
          </button>
        ))}
      </div>

      {error && <p className="placeholder-text">{t(error)}</p>}
      {!error && data === null && <p className="placeholder-text">{t('employees.history.loading')}</p>}
      {!error && data !== null && data.items.length === 0 && (
        <p className="placeholder-text">{t('employees.history.empty')}</p>
      )}
      {!error && data !== null && data.items.length > 0 && (
        <>
          {data.items.map((entry) => {
            const isExpanded = expanded.has(entry.id)
            const canExpand = entry.changes.length > 0
            return (
              <article key={entry.id} className="employee-history-entry">
                <header className="employee-history-header">
                  <span className="employee-history-when">{formatDateTime(entry.timestamp)}</span>
                  <span className="employee-history-actor">
                    {t('employees.history.actorLine', {
                      action: entry.actionLabel,
                      name: entry.userName ?? t('employees.history.system'),
                    })}
                  </span>
                  <Badge tone={CATEGORY_TONES[entry.categoryCode] ?? 'neutral'}>
                    {t(`employees.history.categories.${entry.categoryCode}`)}
                  </Badge>
                </header>
                <p className="employee-history-summary">{entry.summary}</p>
                {canExpand && (
                  <button
                    type="button"
                    className="employee-history-toggle"
                    aria-expanded={isExpanded}
                    onClick={() => toggleExpanded(entry.id)}
                  >
                    {isExpanded ? t('employees.history.collapse') : t('employees.history.expand')}
                  </button>
                )}
                {canExpand && isExpanded && (
                  <table className="employee-history-table">
                    <thead>
                      <tr>
                        <th>{t('employees.history.columnField')}</th>
                        <th>{t('employees.history.columnBefore')}</th>
                        <th>{t('employees.history.columnAfter')}</th>
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
