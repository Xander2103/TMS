import { useEffect, useState } from 'react'
import { euro } from '../../invoices/types'
import { useLocale } from '../../../i18n/localeContext'
import { getActivityKpis } from '../api/kpiApi'
import { num, type ActivityKpiReport } from '../types'
import './kpi.css'

interface KpiActivitiesSectionProps {
  from: string
  to: string
}

/**
 * P11: activity-based KPIs per activity type (/api/kpi/activities). A dossier with a crane
 * AND a plateau activity feeds both rows; the totals row dedupes shared orders. The
 * pallet-day stat only appears when the period has overlapping storage stays.
 */
export function KpiActivitiesSection({ from, to }: KpiActivitiesSectionProps) {
  const { t } = useLocale()
  const [data, setData] = useState<ActivityKpiReport | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    getActivityKpis(from, to)
      .then((result) => {
        if (!mounted) return
        setData(result)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('kpiReports.activities.loadFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [from, to])

  return (
    <section className="kpi-section">
      <h2>{t('kpiReports.activities.title')}</h2>
      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && !data && <p className="placeholder-text">{t('kpiReports.activities.loading')}</p>}
      {data && data.rows.length === 0 && (
        <p className="placeholder-text">{t('kpiReports.activities.empty')}</p>
      )}
      {data && data.rows.length > 0 && (
        <div className="kpi-table-panel">
          <table className="kpi-table">
            <thead>
              <tr>
                <th>{t('kpiReports.activities.typeHeader')}</th>
                <th>{t('kpiReports.activities.categoryHeader')}</th>
                <th className="kpi-num">{t('kpiReports.activities.countHeader')}</th>
                <th className="kpi-num">{t('kpiReports.activities.linkedOrdersHeader')}</th>
                <th className="kpi-num">{t('kpiReports.activities.revenueHeader')}</th>
                <th className="kpi-num">{t('kpiReports.activities.redeliveriesHeader')}</th>
              </tr>
            </thead>
            <tbody>
              {data.rows.map((row) => (
                <tr key={row.activityTypeId}>
                  <td>{row.name}</td>
                  <td>{row.kpiCategory ?? '—'}</td>
                  <td className="kpi-num">{num(row.activityCount)}</td>
                  <td className="kpi-num">{num(row.linkedOrderCount)}</td>
                  <td className="kpi-num">{euro(row.revenue)}</td>
                  <td className="kpi-num">{num(row.redeliveryCount)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr className="kpi-totals-row">
                <td>{t('kpiReports.activities.total')}</td>
                <td />
                <td className="kpi-num">{num(data.totals.activityCount)}</td>
                <td className="kpi-num">{num(data.totals.linkedOrderCount)}</td>
                <td className="kpi-num">{euro(data.totals.revenue)}</td>
                <td className="kpi-num">{num(data.totals.redeliveryCount)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      )}
      {data && data.palletDays !== null && (
        <p className="kpi-activities-palletdays">
          {t('kpiReports.activities.palletDays')} <strong>{num(data.palletDays)}</strong>
        </p>
      )}
    </section>
  )
}
