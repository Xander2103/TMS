import { useEffect, useState } from 'react'
import { euro } from '../../invoices/types'
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
        if (mounted) setLoadError('De activiteiten-KPI’s konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [from, to])

  return (
    <section className="kpi-section">
      <h2>Activiteiten</h2>
      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && !data && <p className="placeholder-text">Activiteiten laden…</p>}
      {data && data.rows.length === 0 && (
        <p className="placeholder-text">Geen activiteiten in deze periode.</p>
      )}
      {data && data.rows.length > 0 && (
        <div className="kpi-table-panel">
          <table className="kpi-table">
            <thead>
              <tr>
                <th>Activiteitstype</th>
                <th>Categorie</th>
                <th className="kpi-num">Aantal activiteiten</th>
                <th className="kpi-num">Gekoppelde opdrachten</th>
                <th className="kpi-num">Omzet</th>
                <th className="kpi-num">Herleveringen</th>
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
                <td>Totaal</td>
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
          Pallet-dagen (opslag): <strong>{num(data.palletDays)}</strong>
        </p>
      )}
    </section>
  )
}
