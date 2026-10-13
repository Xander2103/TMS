import { useEffect, useState } from 'react'
import { createSearchParams, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import { getKpiDashboard } from '../api/kpiApi'
import { KpiCard } from '../components/KpiCard'
import { KpiExportControl } from '../components/KpiExportControl'
import { PackageReportsControl } from '../../packages/components/PackageReportsControl'
import { KpiFilterBar } from '../components/KpiFilterBar'
import { KpiActivitiesSection } from '../components/KpiActivitiesSection'
import { num, pct, presetRange, type KpiDashboard, type KpiFilterState } from '../types'
import '../components/kpi.css'

/**
 * Management KPI dashboard. Every number comes from the backend read model
 * (/api/kpi/dashboard, definitions in docs/kpi-definitions.md); cards deep-link to the
 * filtered detail view where one exists. Financial cards need profitability.view.
 */
export function KpiDashboardPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canSeeProfitability = hasPermission('profitability.view')

  const [filter, setFilter] = useState<KpiFilterState>(() => ({
    ...presetRange('month'),
    customerId: null,
    driverId: null,
    vehicleId: null,
  }))
  const [data, setData] = useState<KpiDashboard | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const requestKey = JSON.stringify(filter)
  useEffect(() => {
    let mounted = true
    getKpiDashboard(filter)
      .then((result) => {
        if (!mounted) return
        setData(result)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('kpiReports.kpi.loadFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  const tripsLink = `/kpi/trips?${createSearchParams({
    from: filter.from,
    to: filter.to,
    ...(filter.customerId ? { customerId: filter.customerId } : {}),
    ...(filter.driverId ? { driverId: filter.driverId } : {}),
    ...(filter.vehicleId ? { vehicleId: filter.vehicleId } : {}),
  }).toString()}`
  const profitLink = canSeeProfitability ? tripsLink : undefined

  return (
    <div>
      <Breadcrumbs items={[{ label: t('kpiReports.kpi.breadcrumb') }]} />
      <PageHeader
        title={t('kpiReports.kpi.title')}
        subtitle={t('kpiReports.kpi.subtitle')}
        action={
          <span className="kpi-export-actions">
            <KpiExportControl filter={filter} />
            <PackageReportsControl />
          </span>
        }
      />

      <KpiFilterBar filter={filter} onChange={setFilter} />

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && !data && <p className="placeholder-text">{t('kpiReports.kpi.loading')}</p>}
      {data && (
        <>
          {canSeeProfitability && (
            <section className="kpi-section">
              <h2>{t('kpiReports.kpi.financialSection')}</h2>
              <div className="kpi-grid">
                <KpiCard label={t('kpiReports.kpi.revenueToday')} value={euro(data.revenueToday)} to={profitLink} />
                <KpiCard label={t('kpiReports.kpi.revenuePeriod')} value={euro(data.revenuePeriod)} to={profitLink} />
                <KpiCard label={t('kpiReports.kpi.profitToday')} value={euro(data.profitToday)} to={profitLink} />
                <KpiCard label={t('kpiReports.kpi.profitPeriod')} value={euro(data.profitPeriod)} to={profitLink} />
                <KpiCard label={t('kpiReports.kpi.averageMargin')} value={pct(data.averageMarginPct)} to={profitLink} />
                <KpiCard
                  label={t('kpiReports.kpi.profitPerTrip')}
                  value={data.profitPerTrip !== null ? euro(data.profitPerTrip) : '—'}
                  hint={t('kpiReports.kpi.trips', { count: data.tripCount })}
                  to={profitLink}
                />
                <KpiCard
                  label={t('kpiReports.kpi.costOverruns')}
                  value={num(data.costOverrunTripCount)}
                  hint={data.avgCostOverrunPct !== null
                    ? t('kpiReports.kpi.costOverrunHint', { pct: pct(data.avgCostOverrunPct) })
                    : t('kpiReports.kpi.costOverrunHintNone')}
                  to={profitLink}
                  tone={data.costOverrunTripCount > 0 ? 'warning' : undefined}
                />
              </div>
            </section>
          )}

          <section className="kpi-section">
            <h2>{t('kpiReports.kpi.fleetSection')}</h2>
            <div className="kpi-grid">
              <KpiCard label={t('kpiReports.kpi.vehicleUtilisation')} value={pct(data.vehicleUtilisationPct)} hint={t('kpiReports.kpi.vehicleUtilisationHint')} to={profitLink} />
              <KpiCard label={t('kpiReports.kpi.totalKm')} value={t('kpiReports.kpi.kmValue', { value: num(data.totalKm) })} to={profitLink} />
              <KpiCard label={t('kpiReports.kpi.emptyKm')} value={t('kpiReports.kpi.kmValue', { value: num(data.emptyKm) })} hint={pct(data.emptyKmPct)} to={profitLink} />
              <KpiCard label={t('kpiReports.kpi.fuelUsage')} value={t('kpiReports.kpi.litresValue', { value: num(data.fuelLitres) })} to="/tank-cards" />
              <KpiCard label={t('kpiReports.kpi.fuelCost')} value={euro(data.fuelCost)} to="/tank-cards" />
              <KpiCard label={t('kpiReports.kpi.co2')} value={t('kpiReports.kpi.kgValue', { value: num(data.co2Kg) })} hint={t('kpiReports.kpi.co2Hint')} />
              <KpiCard
                label={t('kpiReports.kpi.openDamage')}
                value={num(data.openDamageCount)}
                to="/fleet"
                tone={data.openDamageCount > 0 ? 'warning' : undefined}
              />
            </div>
          </section>

          <section className="kpi-section">
            <h2>{t('kpiReports.kpi.executionSection')}</h2>
            <div className="kpi-grid">
              <KpiCard label={t('kpiReports.kpi.deliveryReliability')} value={pct(data.deliveryReliabilityPct)} hint={t('kpiReports.kpi.deliveryReliabilityHint')} to={profitLink} />
              <KpiCard label={t('kpiReports.kpi.onTimeArrival')} value={pct(data.onTimeArrivalPct)} hint={t('kpiReports.kpi.onTimeArrivalHint')} to={profitLink} />
              <KpiCard
                label={t('kpiReports.kpi.etaDeviation')}
                value={data.avgEtaDeviationMinutes !== null ? t('kpiReports.kpi.minutesValue', { value: num(data.avgEtaDeviationMinutes, 1) }) : '—'}
                hint={t('kpiReports.kpi.etaDeviationHint')}
              />
              <KpiCard
                label={t('kpiReports.kpi.failedDeliveries')}
                value={num(data.failedDeliveries)}
                tone={data.failedDeliveries > 0 ? 'danger' : undefined}
                to={profitLink}
              />
              <KpiCard label={t('kpiReports.kpi.partialDeliveries')} value={num(data.partialDeliveries)} to={profitLink} />
              <KpiCard
                label={t('kpiReports.kpi.openExceptions')}
                value={num(data.openExceptions)}
                to="/exceptions"
                tone={data.openExceptions > 0 ? 'warning' : undefined}
              />
            </div>
          </section>

          {(data.topCustomers.length > 0 || data.kmPerDriver.length > 0) && (
            <section className="kpi-section kpi-tables">
              {canSeeProfitability && data.topCustomers.length > 0 && (
                <div className="kpi-table-panel">
                  <h3>{t('kpiReports.kpi.topCustomers')}</h3>
                  <table className="kpi-table">
                    <thead>
                      <tr>
                        <th>{t('kpiReports.kpi.customerHeader')}</th>
                        <th className="kpi-num">{t('kpiReports.kpi.revenueHeader')}</th>
                        <th className="kpi-num">{t('kpiReports.kpi.allocatedCostHeader')}</th>
                        <th className="kpi-num">{t('kpiReports.kpi.profitHeader')}</th>
                        <th className="kpi-num">{t('kpiReports.kpi.marginHeader')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.topCustomers.map((customer) => (
                        <tr
                          key={customer.customerId}
                          className="kpi-row-link"
                          onClick={() => navigate(`/customers/${customer.customerId}`)}
                        >
                          <td>{customer.customerName}</td>
                          <td className="kpi-num">{euro(customer.revenue)}</td>
                          <td className="kpi-num">{euro(customer.allocatedCost)}</td>
                          <td className="kpi-num">{euro(customer.profit)}</td>
                          <td className="kpi-num">{pct(customer.marginPct)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              {data.kmPerDriver.length > 0 && (
                <div className="kpi-table-panel">
                  <h3>{t('kpiReports.kpi.kmPerDriver')}</h3>
                  <table className="kpi-table">
                    <thead>
                      <tr>
                        <th>{t('kpiReports.kpi.driverHeader')}</th>
                        <th className="kpi-num">{t('kpiReports.kpi.kmHeader')}</th>
                        <th className="kpi-num">{t('kpiReports.kpi.hoursHeader')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.kmPerDriver.map((driver) => (
                        <tr key={driver.driverId}>
                          <td>{driver.driverName}</td>
                          <td className="kpi-num">{t('kpiReports.kpi.kmValue', { value: num(driver.km) })}</td>
                          <td className="kpi-num">{t('kpiReports.kpi.hoursValue', { value: num(driver.hours, 1) })}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>
          )}
        </>
      )}

      <KpiActivitiesSection from={filter.from} to={filter.to} />
    </div>
  )
}
