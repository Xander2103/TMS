import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useAuth } from '../../auth/authContextValue'
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
      .catch(() => setLoadError('De laadlijst kon niet worden geladen.'))
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
        title="Magazijn"
        subtitle="Laadlijsten en colli"
        action={
          <span className="wh-day-toggle" role="radiogroup" aria-label="Dag">
            <Button variant={dayOffset === 0 ? 'primary' : 'secondary'} onClick={() => setDayOffset(0)}>
              Vandaag
            </Button>
            <Button variant={dayOffset === 1 ? 'primary' : 'secondary'} onClick={() => setDayOffset(1)}>
              Morgen
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
        <h2>Colli zoeken</h2>
        <input
          className="wh-search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Zoek op collinummer, barcode of omschrijving…"
          aria-label="Colli zoeken"
        />
        {results && (
          <ul className="wh-results">
            {results.length === 0 && <li className="wh-empty">Geen colli gevonden.</li>}
            {results.map((row) => (
              <li key={row.packageId}>
                <Link to={`/packages/${row.packageId}`}>
                  <code>{row.packageNumber}</code>
                </Link>
                <span className="wh-result-desc">{row.description}</span>
                <Badge tone={PACKAGE_STATUS_TONE[row.status]}>{PACKAGE_STATUS_LABELS[row.status]}</Badge>
                {row.exceptionState === 'Open' && <Badge tone="danger">Melding</Badge>}
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
        <LoadingState message="Laadlijst laden..." />
      ) : trips.length === 0 ? (
        <section className="to-section">
          <p>Geen te laden ritten op deze dag.</p>
        </section>
      ) : (
        trips.map((trip) => (
          <section key={trip.tripId} className="to-section wh-trip">
            <div className="wh-trip-header">
              <h2>
                {canOpenTrip ? <Link to={`/planning/${trip.tripId}`}>{trip.tripNumber}</Link> : trip.tripNumber}
              </h2>
              <Badge tone={trip.status === 'InProgress' ? 'info' : 'neutral'}>{trip.status === 'InProgress' ? 'Onderweg' : 'Gepland'}</Badge>
              {trip.isComplete ? (
                <Badge tone="success">✓ Volledig geladen</Badge>
              ) : (
                <Badge tone="warning">
                  {trip.loadedCount}/{trip.mandatoryPackages} geladen
                </Badge>
              )}
              {trip.missingCount > 0 && <Badge tone="danger">{trip.missingCount} vermist</Badge>}
              {trip.damagedCount > 0 && <Badge tone="danger">{trip.damagedCount} beschadigd</Badge>}
              {trip.openExceptionCount > 0 && <Badge tone="warning">{trip.openExceptionCount} melding(en)</Badge>}
            </div>
            <p className="wh-trip-meta">
              {[trip.driverName ?? 'Geen chauffeur', trip.vehicleNumber ?? 'Geen voertuig',
                `${trip.orderCount} opdracht(en)`, `${trip.totalPackages} colli`].join(' · ')}
            </p>
            <ul className="wh-stops">
              {trip.loadingStops.map((stop) => (
                <li key={stop.stopId}>
                  <span className="wh-stop-label">
                    {stop.locationName ?? stop.city ?? 'Laadstop'}
                    {stop.city && stop.locationName ? ` (${stop.city})` : ''}
                  </span>
                  <span className="wh-stop-count">{stop.expectedPackages} colli</span>
                  {canScan && (
                    <Button
                      variant="secondary"
                      onClick={() =>
                        setScanTarget({
                          tripId: trip.tripId,
                          stopId: stop.stopId,
                          label: `${trip.tripNumber} — ${stop.locationName ?? stop.city ?? 'laadstop'}`,
                        })
                      }
                    >
                      Scannen
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
