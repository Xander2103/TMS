import { useEffect, useState } from 'react'
import { KpiCard } from '../../kpi/components/KpiCard'
import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import { listMessages, getStats, STATUS_LABELS, STATUS_TONE, type EdiMessageRow, type EdiStats } from '../api/ediApi'
import { formatDateTime } from '../../../utils/dates'

/** Stat tiles (dashboard tile vocabulary, shared with the KPI module) plus a 10-row recent-activity glance. */
export function DashboardTab() {
  const { t } = useLocale()
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
        if (mounted) setError(t('edi.dashboard.loadFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  if (error) return <p className="placeholder-text">{error}</p>
  if (stats === null || recent === null) return <p className="placeholder-text">{t('edi.dashboard.loading')}</p>

  return (
    <div>
      <div className="kpi-grid">
        <KpiCard label={t('edi.dashboard.processed7d')} value={stats.processedLast7Days} to="/edi?tab=berichten" />
        <KpiCard label={t('edi.dashboard.failed')} value={stats.failed} tone={stats.failed > 0 ? 'warning' : undefined} to="/edi?tab=berichten" />
        <KpiCard
          label={t('edi.dashboard.awaitingMapping')}
          value={stats.mappingIssues}
          tone={stats.mappingIssues > 0 ? 'warning' : undefined}
          to="/edi?tab=mappings"
        />
        <KpiCard label={t('edi.dashboard.deadLetter')} value={stats.deadLettered} tone={stats.deadLettered > 0 ? 'danger' : undefined} to="/edi?tab=berichten" />
        <KpiCard
          label={t('edi.dashboard.partnersWithoutCustomer')}
          value={stats.partnersWithoutCustomer}
          tone={stats.partnersWithoutCustomer > 0 ? 'warning' : undefined}
          to="/edi?tab=handelspartners"
        />
      </div>

      <section className="edi-section">
        <h3>{t('edi.dashboard.recentTitle')}</h3>
        <table className="data-table">
          <thead>
            <tr>
              <th>{t('edi.dashboard.dateHeader')}</th>
              <th>{t('edi.dashboard.directionHeader')}</th>
              <th>{t('edi.dashboard.partnerHeader')}</th>
              <th>{t('edi.dashboard.typeHeader')}</th>
              <th>{t('edi.dashboard.statusHeader')}</th>
            </tr>
          </thead>
          <tbody>
            {recent.map((row) => (
              <tr key={row.id}>
                <td>{formatDateTime(row.createdAt)}</td>
                <td>{row.direction === 'Inbound' ? t('edi.dashboard.inShort') : t('edi.dashboard.outShort')}</td>
                <td>
                  <code>{row.partnerCode}</code>
                </td>
                <td>{row.messageType}</td>
                <td>
                  <Badge tone={STATUS_TONE[row.status]}>{t(STATUS_LABELS[row.status])}</Badge>
                </td>
              </tr>
            ))}
            {recent.length === 0 && (
              <tr>
                <td colSpan={5}>{t('edi.dashboard.empty')}</td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </div>
  )
}
