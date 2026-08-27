import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { ScanPanel } from '../../scanning/components/ScanPanel'
import { PackageReportsControl } from '../components/PackageReportsControl'
import { getWarehouseTrips, searchWarehousePackages } from '../api/packagesApi'
import {
  PACKAGE_STATUS_LABELS,
  PACKAGE_STATUS_TONE,
  type WarehousePackageRow,
  type WarehouseTrip,
} from '../types'
import '../components/packages.css'

function isoDate(offsetDays: number): string {
  const date = new Date()
  date.setDate(date.getDate() + offsetDays)
  return date.toISOString().slice(0, 10)
}

/**
 * Warehouse loading floor: today's/tomorrow's trips with load completeness, direct scan
 * access per loading stop and package search. Costs, HR and profitability deliberately
 * have no surface here.
 */
export function WarehousePage() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const [dayOffset, setDayOffset] = useState(0)
  const [state, setState] = useState<{ trips: WarehouseTrip[]; loadedOffset: number | null }>({
    trips: [],
    loadedOffset: null,
  })
  const [loadError, setLoadError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [results, setResults] = useState<WarehousePackageRow[] | null>(null)
  const [scanTarget, setScanTarget] = useState<{ tripId: string; stopId: string; label: string } | null>(null)

  const canScan = hasPermission('scanning.execute')
  const canOpenTrip = hasPermission('planning.view')

  const reload = useCallback(() => {
    getWarehouseTrips(isoDate(dayOffset))
      .then((data) => {
        setState({ trips: data, loadedOffset: dayOffset })
        setLoadError(null)
      })
      .catch(() => setLoadError(t('packages.warehouse.loadListFailed')))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dayOffset])

  useEffect(() => {
    reload()
  }, [reload])

  useEffect(() => {
    const term = search.trim()
    const timer = window.setTimeout(() => {
      if (term.length < 2) {
        setResults(null)
        return
      }
      searchWarehousePackages(term)
        .then(setResults)
        .catch(() => setResults([]))
    }, 300)
    return () => window.clearTimeout(timer)
  }, [search])

  const trips = state.loadedOffset === dayOffset ? state.trips : null

  if (loadError) return <ErrorState message={loadError} />

  return (
    <div>
      <PageHeader
        title={t('packages.warehouse.title')}
        subtitle={t('packages.warehouse.subtitle')}
        action={
          <span className="wh-day-toggle" role="radiogroup" aria-label={t('packages.warehouse.dayAria')}>
            <Button variant={dayOffset === 0 ? 'primary' : 'secondary'} onClick={() => setDayOffset(0)}>
              {t('packages.warehouse.today')}
            </Button>
            <Button variant={dayOffset === 1 ? 'primary' : 'secondary'} onClick={() => setDayOffset(1)}>
              {t('packages.warehouse.tomorrow')}
            </Button>
          </span>
        }
      />

      {hasPermission('package_reports.export') && (
        <section className="to-section">
          <PackageReportsControl />
        </section>
      )}

      <section className="to-section">
        <h2>{t('packages.warehouse.searchTitle')}</h2>
        <input
          className="wh-search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t('packages.warehouse.searchPlaceholder')}
          aria-label={t('packages.warehouse.searchAria')}
        />
        {results && (
          <ul className="wh-results">
            {results.length === 0 && <li className="wh-empty">{t('packages.warehouse.noResults')}</li>}
            {results.map((row) => (
              <li key={row.packageId}>
                <Link to={`/packages/${row.packageId}`}>
                  <code>{row.packageNumber}</code>
                </Link>
                <span className="wh-result-desc">{row.description}</span>
                <Badge tone={PACKAGE_STATUS_TONE[row.status]}>{t(PACKAGE_STATUS_LABELS[row.status])}</Badge>
                {row.exceptionState === 'Open' && <Badge tone="danger">{t('packages.warehouse.exception')}</Badge>}
                <span className="wh-result-meta">
                  {row.orderNumber}
                  {row.tripNumber ? ` · ${row.tripNumber}` : ''}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      {!trips ? (
        <LoadingState message={t('packages.warehouse.loadingList')} />
      ) : trips.length === 0 ? (
        <section className="to-section">
          <p>{t('packages.warehouse.noTrips')}</p>
        </section>
      ) : (
        trips.map((trip) => (
          <section key={trip.tripId} className="to-section wh-trip">
            <div className="wh-trip-header">
              <h2>
                {canOpenTrip ? <Link to={`/planning/${trip.tripId}`}>{trip.tripNumber}</Link> : trip.tripNumber}
              </h2>
              <Badge tone={trip.status === 'InProgress' ? 'info' : 'neutral'}>
                {trip.status === 'InProgress' ? t('packages.warehouse.statusInProgress') : t('packages.warehouse.statusPlanned')}
              </Badge>
              {trip.isComplete ? (
                <Badge tone="success">{t('packages.warehouse.complete')}</Badge>
              ) : (
                <Badge tone="warning">
                  {t('packages.warehouse.loadedProgress', { loaded: trip.loadedCount, total: trip.mandatoryPackages })}
                </Badge>
              )}
              {trip.missingCount > 0 && <Badge tone="danger">{t('packages.warehouse.missing', { count: trip.missingCount })}</Badge>}
              {trip.damagedCount > 0 && <Badge tone="danger">{t('packages.warehouse.damaged', { count: trip.damagedCount })}</Badge>}
              {trip.openExceptionCount > 0 && <Badge tone="warning">{t('packages.warehouse.exceptions', { count: trip.openExceptionCount })}</Badge>}
            </div>
            <p className="wh-trip-meta">
              {[trip.driverName ?? t('packages.warehouse.noDriver'), trip.vehicleNumber ?? t('packages.warehouse.noVehicle'),
                t('packages.warehouse.orders', { count: trip.orderCount }), t('packages.warehouse.colli', { count: trip.totalPackages })].join(' · ')}
            </p>
            <ul className="wh-stops">
              {trip.loadingStops.map((stop) => (
                <li key={stop.stopId}>
                  <span className="wh-stop-label">
                    {stop.locationName ?? stop.city ?? t('packages.warehouse.loadingStop')}
                    {stop.city && stop.locationName ? ` (${stop.city})` : ''}
                  </span>
                  <span className="wh-stop-count">{t('packages.warehouse.colli', { count: stop.expectedPackages })}</span>
                  {canScan && (
                    <Button
                      variant="secondary"
                      onClick={() =>
                        setScanTarget({
                          tripId: trip.tripId,
                          stopId: stop.stopId,
                          label: `${trip.tripNumber} — ${stop.locationName ?? stop.city ?? t('packages.warehouse.loadingStopLower')}`,
                        })
                      }
                    >
                      {t('packages.warehouse.scan')}
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          </section>
        ))
      )}

      {scanTarget && (
        <ScanPanel
          tripId={scanTarget.tripId}
          stopId={scanTarget.stopId}
          stopLabel={scanTarget.label}
          scanType="Load"
          canCorrect={hasPermission('scanning.correct')}
          onClose={() => {
            setScanTarget(null)
            reload()
          }}
        />
      )}
    </div>
  )
}
