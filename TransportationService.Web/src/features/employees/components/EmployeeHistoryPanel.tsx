import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Pagination } from '../../../components/ui/Pagination'
import { getEmployeeHistory, type EmployeeHistoryPage } from '../api/employeeHistoryApi'
import './EmployeeHistoryPanel.css'

const PAGE_SIZE = 25

/** Dutch category chip tones — informational only, no strict semantics. */
const CATEGORY_TONES: Record<string, 'info' | 'neutral' | 'success' | 'warning'> = {
  Profiel: 'info',
  Kwalificaties: 'success',
  Documenten: 'neutral',
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
 * Complete readable personnel history (corrections wave §4): one card per audited save,
 * newest first, with the actor, the dossier section and a Voor/Na table per changed field.
 * Old partial audit entries render through the same endpoint with fewer field rows.
 */
export function EmployeeHistoryPanel({ employeeId }: { employeeId: string }) {
  const [data, setData] = useState<EmployeeHistoryPage | null>(null)
  const [page, setPage] = useState(1)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    getEmployeeHistory(employeeId, page, PAGE_SIZE)
      .then((result) => {
        setData(result)
        setError(null)
      })
      .catch(() => setError('De historiek kon niet worden geladen.'))
  }, [employeeId, page])

  useEffect(() => {
    reload()
  }, [reload])

  if (error) return <p className="placeholder-text">{error}</p>
  if (data === null) return <p className="placeholder-text">Historiek laden…</p>
  if (data.items.length === 0) return <p className="placeholder-text">Nog geen historiek voor deze medewerker.</p>

  return (
    <div className="employee-history">
      {data.items.map((entry) => (
        <article key={entry.id} className="employee-history-entry">
          <header className="employee-history-header">
            <span className="employee-history-when">{formatTimestamp(entry.timestamp)}</span>
            <span className="employee-history-actor">
              {entry.actionLabel} door {entry.userName ?? 'Systeem'}
            </span>
            <Badge tone={CATEGORY_TONES[entry.category] ?? 'neutral'}>{entry.category}</Badge>
          </header>
          {entry.changes.length > 0 ? (
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
          ) : (
            <p className="placeholder-text">{entry.actionLabel}.</p>
          )}
        </article>
      ))}
      <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPageChange={setPage} />
    </div>
  )
}
