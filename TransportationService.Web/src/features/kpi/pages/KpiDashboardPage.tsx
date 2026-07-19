import { useEffect, useState } from 'react'
import { createSearchParams, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { useAuth } from '../../auth/authContextValue'
import { euro } from '../../invoices/types'
import { getKpiDashboard } from '../api/kpiApi'
import { KpiCard } from '../components/KpiCard'
import { KpiFilterBar } from '../components/KpiFilterBar'
import { num, pct, presetRange, type KpiDashboard, type KpiFilterState } from '../types'
import '../components/kpi.css'

/**
 * Management KPI dashboard. Every number comes from the backend read model
 * (/api/kpi/dashboard, definitions in docs/kpi-definitions.md); cards deep-link to the
 * filtered detail view where one exists. Financial cards need profitability.view.
 */
export function KpiDashboardPage() {
  const navigate = useNavigate()
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
        if (mounted) setLoadError('De KPI-cijfers konden niet worden geladen.')
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
      <Breadcrumbs items={[{ label: "KPI's" }]} />
      <PageHeader
        title="KPI-dashboard"
        subtitle="Managementcijfers voor de gekozen periode. Klik op een kaart voor het detailrapport."
      />

      <KpiFilterBar filter={filter} onChange={setFilter} />

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && !data && <p className="placeholder-text">KPI's laden…</p>}
      {data && (
        <>
          {canSeeProfitability && (
            <section className="kpi-section">
              <h2>Financieel</h2>
              <div className="kpi-grid">
                <KpiCard label="Omzet vandaag" value={euro(data.revenueToday)} to={profitLink} />
                <KpiCard label="Omzet periode" value={euro(data.revenuePeriod)} to={profitLink} />
                <KpiCard label="Winst vandaag" value={euro(data.profitToday)} to={profitLink} />
                <KpiCard label="Winst periode" value={euro(data.profitPeriod)} to={profitLink} />
                <KpiCard label="Gemiddelde marge" value={pct(data.averageMarginPct)} to={profitLink} />
                <KpiCard label="Winst per rit" value={data.profitPerTrip !== null ? euro(data.profitPerTrip) : '—'} hint={`${data.tripCount} ritten`} to={profitLink} />
                <KpiCard
                  label="Kostenoverschrijdingen"
                  value={num(data.costOverrunTripCount)}
                  hint={data.avgCostOverrunPct !== null ? `gem. afwijking ${pct(data.avgCostOverrunPct)}` : 'definitieve vs. geschatte kost'}
                  to={profitLink}
                  tone={data.costOverrunTripCount > 0 ? 'warning' : undefined}
                />
              </div>
            </section>
          )}

          <section className="kpi-section">
            <h2>Vloot &amp; kilometers</h2>
            <div className="kpi-grid">
              <KpiCard label="Voertuigbezetting" value={pct(data.vehicleUtilisationPct)} hint="actieve uren / beschikbare uren" to={profitLink} />
              <KpiCard label="Totale kilometers" value={`${num(data.totalKm)} km`} to={profitLink} />
              <KpiCard label="Lege kilometers" value={`${num(data.emptyKm)} km`} hint={pct(data.emptyKmPct)} to={profitLink} />
              <KpiCard label="Brandstofverbruik" value={`${num(data.fuelLitres)} l`} to="/tank-cards" />
              <KpiCard label="Brandstofkosten" value={euro(data.fuelCost)} to="/tank-cards" />
              <KpiCard label="CO₂-raming" value={`${num(data.co2Kg)} kg`} hint="op basis van getankte liters" />
              <KpiCard
                label="Open schadegevallen"
                value={num(data.openDamageCount)}
                to="/fleet"
                tone={data.openDamageCount > 0 ? 'warning' : undefined}
              />
            </div>
          </section>

          <section className="kpi-section">
            <h2>Uitvoering</h2>
            <div className="kpi-grid">
              <KpiCard label="Leverbetrouwbaarheid" value={pct(data.deliveryReliabilityPct)} hint="succesvolle losstops / gepland" to={profitLink} />
              <KpiCard label="Stiptheid aankomst" value={pct(data.onTimeArrivalPct)} hint="binnen het afgesproken venster" to={profitLink} />
              <KpiCard
                label="Gem. ETA-afwijking"
                value={data.avgEtaDeviationMinutes !== null ? `${num(data.avgEtaDeviationMinutes, 1)} min` : '—'}
                hint="aankomst t.o.v. laatste ETA"
              />
              <KpiCard
                label="Mislukte leveringen"
                value={num(data.failedDeliveries)}
                tone={data.failedDeliveries > 0 ? 'danger' : undefined}
                to={profitLink}
              />
              <KpiCard label="Deelleveringen" value={num(data.partialDeliveries)} to={profitLink} />
              <KpiCard
                label="Open afwijkingen"
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
                  <h3>Top klanten (omzet, winst per klant)</h3>
                  <table className="kpi-table">
                    <thead>
                      <tr>
                        <th>Klant</th>
                        <th className="kpi-num">Omzet</th>
                        <th className="kpi-num">Toegerekende kost</th>
                        <th className="kpi-num">Winst</th>
                        <th className="kpi-num">Marge</th>
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
                  <h3>Kilometers per chauffeur</h3>
                  <table className="kpi-table">
                    <thead>
                      <tr>
                        <th>Chauffeur</th>
                        <th className="kpi-num">Kilometers</th>
                        <th className="kpi-num">Uren</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.kmPerDriver.map((driver) => (
                        <tr key={driver.driverId}>
                          <td>{driver.driverName}</td>
                          <td className="kpi-num">{num(driver.km)} km</td>
                          <td className="kpi-num">{num(driver.hours, 1)} u</td>
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
    </div>
  )
}
