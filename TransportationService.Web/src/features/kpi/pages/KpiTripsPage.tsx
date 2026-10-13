import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import { euro } from '../../invoices/types'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE, type TripStatus } from '../../planning/types'
import { getTripProfitability } from '../api/kpiApi'
import { KpiExportControl } from '../components/KpiExportControl'
import { KpiFilterBar } from '../components/KpiFilterBar'
import { num, pct, presetRange, type KpiFilterState, type TripProfitabilityRow } from '../types'
import '../components/kpi.css'

/** Trip-profitability drill-down (profitability.view): one row per trip, totals footer. */
export function KpiTripsPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const [searchParams, setSearchParams] = useSearchParams()

  const filter = useMemo<KpiFilterState>(() => {
    const fallback = presetRange('month')
    return {
      from: searchParams.get('from') ?? fallback.from,
      to: searchParams.get('to') ?? fallback.to,
      customerId: searchParams.get('customerId'),
      driverId: searchParams.get('driverId'),
      vehicleId: searchParams.get('vehicleId'),
    }
  }, [searchParams])

  function setFilter(next: KpiFilterState) {
    const params: Record<string, string> = { from: next.from, to: next.to }
    if (next.customerId) params.customerId = next.customerId
    if (next.driverId) params.driverId = next.driverId
    if (next.vehicleId) params.vehicleId = next.vehicleId
    setSearchParams(params, { replace: true })
  }

  const [rows, setRows] = useState<TripProfitabilityRow[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const requestKey = JSON.stringify(filter)
  useEffect(() => {
    let mounted = true
    getTripProfitability(filter)
      .then((data) => {
        if (!mounted) return
        setRows(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('kpiReports.trips.loadFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  const totals = useMemo(() => {
    if (!rows || rows.length === 0) return null
    const revenue = rows.reduce((sum, row) => sum + row.revenue, 0)
    const cost = rows.reduce((sum, row) => sum + (row.finalCost ?? row.projectedCost ?? row.estimatedCost), 0)
    return { revenue, cost, profit: revenue - cost }
  }, [rows])

  return (
    <div>
      <Breadcrumbs items={[{ label: t('kpiReports.kpi.breadcrumb'), to: '/kpi' }, { label: t('kpiReports.trips.breadcrumb') }]} />
      <PageHeader
        title={t('kpiReports.trips.title')}
        subtitle={t('kpiReports.trips.subtitle')}
        action={<KpiExportControl filter={filter} />}
      />

      <KpiFilterBar filter={filter} onChange={setFilter} />

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && rows === null && <p className="placeholder-text">{t('kpiReports.trips.loading')}</p>}
      {rows !== null && rows.length === 0 && <p className="placeholder-text">{t('kpiReports.trips.empty')}</p>}
      {rows !== null && rows.length > 0 && (
        <div className="kpi-table-panel">
          <table className="kpi-table">
            <thead>
              <tr>
                <th>{t('kpiReports.trips.tripHeader')}</th>
                <th>{t('kpiReports.trips.dateHeader')}</th>
                <th>{t('kpiReports.trips.driverHeader')}</th>
                <th>{t('kpiReports.trips.customersHeader')}</th>
                <th className="kpi-num">{t('kpiReports.trips.revenueHeader')}</th>
                <th className="kpi-num">{t('kpiReports.trips.estimatedHeader')}</th>
                <th className="kpi-num">{t('kpiReports.trips.costsHeader')}</th>
                <th className="kpi-num">{t('kpiReports.trips.profitHeader')}</th>
                <th className="kpi-num">{t('kpiReports.trips.marginHeader')}</th>
                <th className="kpi-num">{t('kpiReports.trips.kmEmptyHeader')}</th>
                <th>{t('kpiReports.trips.statusHeader')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.tripId} className="kpi-row-link" onClick={() => navigate(`/planning/${row.tripId}`)}>
                  <td>
                    {row.tripNumber}
                    {row.isFinalized ? ' ✓' : ''}
                  </td>
                  <td>{formatDate(row.tripDate)}</td>
                  <td>{row.driverName ?? '—'}</td>
                  <td>{row.customerNames || '—'}</td>
                  <td className="kpi-num">{euro(row.revenue)}</td>
                  <td className="kpi-num">{euro(row.estimatedCost)}</td>
                  <td className="kpi-num">{euro(row.finalCost ?? row.projectedCost ?? row.estimatedCost)}</td>
                  <td className="kpi-num">{euro(row.profit)}</td>
                  <td className="kpi-num">{pct(row.marginPct)}</td>
                  <td className="kpi-num">
                    {num(row.totalKm)} ({num(row.emptyKm)})
                  </td>
                  <td>
                    <Badge tone={TRIP_STATUS_TONE[row.status as TripStatus] ?? 'neutral'}>
                      {TRIP_STATUS_LABELS[row.status as TripStatus] ? t(TRIP_STATUS_LABELS[row.status as TripStatus]) : row.status}
                    </Badge>
                  </td>
                </tr>
              ))}
              {totals && (
                <tr className="kpi-totals-row">
                  <td colSpan={4}>{t('kpiReports.trips.totals', { count: rows.length })}</td>
                  <td className="kpi-num">{euro(totals.revenue)}</td>
                  <td className="kpi-num" />
                  <td className="kpi-num">{euro(totals.cost)}</td>
                  <td className="kpi-num">{euro(totals.profit)}</td>
                  <td className="kpi-num" colSpan={3} />
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
