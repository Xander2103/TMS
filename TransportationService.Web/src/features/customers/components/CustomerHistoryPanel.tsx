import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Pagination } from '../../../components/ui/Pagination'
import { getCustomerHistory, type CustomerHistoryPage } from '../api/customerHistoryApi'
import './CustomerHistoryPanel.css'

const PAGE_SIZE = 25

/** Chip order: "Alles" (no filter) first, then every backend history category. */
const CATEGORY_FILTERS = ['Alles', 'Klant', 'Contactpersonen', 'Locaties', 'Facturatie', 'Communicatie'] as const

/** Dutch category chip tones — informational only, no strict semantics. */
const CATEGORY_TONES: Record<string, 'info' | 'neutral' | 'success' | 'warning'> = {
  Klant: 'info',
  Contactpersonen: 'success',
  Locaties: 'neutral',
  Facturatie: 'warning',
  Communicatie: 'info',
}

function formatTimestamp(iso: string): string {
  const date = new Date(iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`)
  return date.toLocaleString('nl-BE', { dateStyle: 'short', timeStyle: 'short' })
}

/**
 * Readable customer history (mirrors EmployeeHistoryPanel): one card per audited change,
 * newest first, collapsed to a Dutch summary line — the actor, the category and (on demand)
 * the full Veld/Voor/Na table. Ids are resolved to names server-side; nothing raw is shown.
 */
export function CustomerHistoryPanel({ customerId }: { customerId: string }) {
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
      .catch(() => setError('De historiek kon niet worden geladen.'))
  }, [customerId, page, category])

  useEffect(() => {
    reload()
  }, [reload])

  const selectCategory = (next: string) => {
    setCategory(next === 'Alles' ? null : next)
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
      <div className="customer-history-filters" role="group" aria-label="Filter op categorie">
        {CATEGORY_FILTERS.map((filter) => (
          <button
            key={filter}
            type="button"
            className={`customer-history-filter-chip${(category ?? 'Alles') === filter ? ' customer-history-filter-chip-active' : ''}`}
            aria-pressed={(category ?? 'Alles') === filter}
            onClick={() => selectCategory(filter)}
          >
            {filter}
          </button>
        ))}
      </div>

      {error && <p className="placeholder-text">{error}</p>}
      {!error && data === null && <p className="placeholder-text">Historiek laden…</p>}
      {!error && data !== null && data.items.length === 0 && (
        <p className="placeholder-text">Nog geen historiek voor deze klant.</p>
      )}
      {!error && data !== null && data.items.length > 0 && (
        <>
          {data.items.map((entry) => {
            const isExpanded = expanded.has(entry.id)
            const canExpand = entry.changes.length > 0
            return (
              <article key={entry.id} className="customer-history-entry">
                <header className="customer-history-header">
                  <span className="customer-history-when">{formatTimestamp(entry.timestamp)}</span>
                  <span className="customer-history-actor">
                    {entry.actionLabel} door {entry.userName ?? 'Systeem'}
                  </span>
                  <Badge tone={CATEGORY_TONES[entry.category] ?? 'neutral'}>{entry.category}</Badge>
                </header>
                <p className="customer-history-summary">{entry.summary}</p>
                {canExpand && (
                  <button
                    type="button"
                    className="customer-history-toggle"
                    aria-expanded={isExpanded}
                    onClick={() => toggleExpanded(entry.id)}
                  >
                    {isExpanded ? 'Inklappen' : 'Uitklappen'}
                  </button>
                )}
                {canExpand && isExpanded && (
                  <table className="customer-history-table">
                    <thead>
                      <tr>
                        <th>Veld</th>
                        <th>Voor</th>
                        <th>Na</th>
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
