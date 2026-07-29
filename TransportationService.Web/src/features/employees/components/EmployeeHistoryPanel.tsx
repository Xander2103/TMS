import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Pagination } from '../../../components/ui/Pagination'
import { getEmployeeHistory, type EmployeeHistoryPage } from '../api/employeeHistoryApi'
import './EmployeeHistoryPanel.css'

const PAGE_SIZE = 25

/** Chip order: "Alles" (no filter) first, then every dossier section in nav order. */
const CATEGORY_FILTERS = [
  'Alles',
  'Profiel',
  'Kwalificaties',
  'Documenten',
  'Notities',
  'Bedrijfsmiddelen',
  'Afwezigheden',
  'Verlofsaldo',
  'Chauffeursprofiel',
] as const

/** Dutch category chip tones — informational only, no strict semantics. */
const CATEGORY_TONES: Record<string, 'info' | 'neutral' | 'success' | 'warning'> = {
  Profiel: 'info',
  Kwalificaties: 'success',
  Documenten: 'neutral',
  Notities: 'info',
  Afwezigheden: 'warning',
  Verlofsaldo: 'warning',
  Bedrijfsmiddelen: 'neutral',
  Chauffeursprofiel: 'info',
}

function formatTimestamp(iso: string): string {
  const date = new Date(iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`)
  return date.toLocaleString('nl-BE', { dateStyle: 'short', timeStyle: 'short' })
}

/**
 * Complete readable personnel history (corrections wave §4): one card per audited save, newest
 * first, collapsed by default to a single Dutch summary line — the actor, the dossier section
 * and (on demand) the full Veld/Voor/Na table. Old partial audit entries render through the
 * same endpoint with fewer field rows; unmapped/legacy id fields resolve to names server-side.
 */
export function EmployeeHistoryPanel({ employeeId }: { employeeId: string }) {
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
      .catch(() => setError('De historiek kon niet worden geladen.'))
  }, [employeeId, page, category])

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
    <div className="employee-history">
      <div className="employee-history-filters" role="group" aria-label="Filter op categorie">
        {CATEGORY_FILTERS.map((filter) => (
          <button
            key={filter}
            type="button"
            className={`employee-history-filter-chip${(category ?? 'Alles') === filter ? ' employee-history-filter-chip-active' : ''}`}
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
        <p className="placeholder-text">Nog geen historiek voor deze medewerker.</p>
      )}
      {!error && data !== null && data.items.length > 0 && (
        <>
          {data.items.map((entry) => {
            const isExpanded = expanded.has(entry.id)
            const canExpand = entry.changes.length > 0
            return (
              <article key={entry.id} className="employee-history-entry">
                <header className="employee-history-header">
                  <span className="employee-history-when">{formatTimestamp(entry.timestamp)}</span>
                  <span className="employee-history-actor">
                    {entry.actionLabel} door {entry.userName ?? 'Systeem'}
                  </span>
                  <Badge tone={CATEGORY_TONES[entry.category] ?? 'neutral'}>{entry.category}</Badge>
                </header>
                <p className="employee-history-summary">{entry.summary}</p>
                {canExpand && (
                  <button
                    type="button"
                    className="employee-history-toggle"
                    aria-expanded={isExpanded}
                    onClick={() => toggleExpanded(entry.id)}
                  >
                    {isExpanded ? 'Inklappen' : 'Uitklappen'}
                  </button>
                )}
                {canExpand && isExpanded && (
                  <table className="employee-history-table">
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
