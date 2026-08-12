import { useEffect, useState } from 'react'
import { KpiCard } from '../../kpi/components/KpiCard'
import { Badge } from '../../../components/ui/Badge'
import { listMessages, getStats, STATUS_LABELS, STATUS_TONE, type EdiMessageRow, type EdiStats } from '../api/ediApi'
import { formatDateTime } from '../../../utils/dates'

/** Stat tiles (dashboard tile vocabulary, shared with the KPI module) plus a 10-row recent-activity glance. */
export function DashboardTab() {
  const [stats, setStats] = useState<EdiStats | null>(null)
  const [recent, setRecent] = useState<EdiMessageRow[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    Promise.all([getStats(), listMessages({ page: 1, pageSize: 10 })])
      .then(([statsData, messagesData]) => {
        if (!mounted) return
        setStats(statsData)
        setRecent(messagesData.items)
      })
      .catch(() => {
        if (mounted) setError('Het EDI-dashboard kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  if (error) return <p className="placeholder-text">{error}</p>
  if (stats === null || recent === null) return <p className="placeholder-text">Dashboard laden…</p>

  return (
    <div>
      <div className="kpi-grid">
        <KpiCard label="Verwerkt (7 dagen)" value={stats.processedLast7Days} to="/edi?tab=berichten" />
        <KpiCard label="Mislukt" value={stats.failed} tone={stats.failed > 0 ? 'warning' : undefined} to="/edi?tab=berichten" />
        <KpiCard
          label="Wacht op mapping"
          value={stats.mappingIssues}
          tone={stats.mappingIssues > 0 ? 'warning' : undefined}
          to="/edi?tab=mappings"
        />
        <KpiCard label="Dead letter" value={stats.deadLettered} tone={stats.deadLettered > 0 ? 'danger' : undefined} to="/edi?tab=berichten" />
        <KpiCard
          label="Partners zonder klant"
          value={stats.partnersWithoutCustomer}
          tone={stats.partnersWithoutCustomer > 0 ? 'warning' : undefined}
          to="/edi?tab=handelspartners"
        />
      </div>

      <section className="edi-section">
        <h3>Recente berichten</h3>
        <table className="data-table">
          <thead>
            <tr>
              <th>Datum</th>
              <th>Richting</th>
              <th>Partner</th>
              <th>Type</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {recent.map((row) => (
              <tr key={row.id}>
                <td>{formatDateTime(row.createdAt)}</td>
                <td>{row.direction === 'Inbound' ? '↓ in' : '↑ uit'}</td>
                <td>
                  <code>{row.partnerCode}</code>
                </td>
                <td>{row.messageType}</td>
                <td>
                  <Badge tone={STATUS_TONE[row.status]}>{STATUS_LABELS[row.status]}</Badge>
                </td>
              </tr>
            ))}
            {recent.length === 0 && (
              <tr>
                <td colSpan={5}>Nog geen EDI-berichten.</td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </div>
  )
}
