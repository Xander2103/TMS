import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { useAuth } from '../../auth/authContextValue'
import { useOnlineStatus } from '../../../hooks/useOnlineStatus'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE } from '../../planning/types'
import { ETA_SOURCE_LABELS } from '../../operations/types'
import { getMyDashboard } from '../api/driverApi'
import { DriverActivityCard } from '../../time-attendance/components/DriverActivityCard'
import type { MyDashboard } from '../types'
import { formatDate } from '../../../utils/dates'

const SNAPSHOT_PREFIX = 'ts.driverSnapshot.v1'

function snapshotKey(userId: string): string {
  return `${SNAPSHOT_PREFIX}.${userId}`
}

function readSnapshot(userId: string): MyDashboard | null {
  try {
    const raw = localStorage.getItem(snapshotKey(userId))
    return raw ? (JSON.parse(raw) as { data: MyDashboard }).data : null
  } catch {
    return null
  }
}

/**
 * Driver home. Online it refreshes the dashboard and caches a per-user snapshot; offline it
 * serves the snapshot with a clear "cached" marker — only the driver's own active work is
 * ever cached, and logout wipes it.
 */
export function DriverHomePage() {
  const { user } = useAuth()
  const online = useOnlineStatus()
  const [dashboard, setDashboard] = useState<MyDashboard | null>(null)
  const [fromCache, setFromCache] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!user) return
    let cancelled = false
    getMyDashboard()
      .then((data) => {
        if (cancelled) return
        setDashboard(data)
        setFromCache(false)
        setError(null)
        try {
          localStorage.setItem(snapshotKey(user.id), JSON.stringify({ data, cachedAt: new Date().toISOString() }))
        } catch {
          // cache is best-effort
        }
      })
      .catch(() => {
        if (cancelled) return
        const cached = readSnapshot(user.id)
        if (cached) {
          setDashboard(cached)
          setFromCache(true)
        } else {
          setError('Dashboard laden mislukt en er is geen offline kopie beschikbaar.')
        }
      })
    return () => {
      cancelled = true
    }
  }, [user, online])

  if (error) {
    return <p className="drv-muted">{error}</p>
  }

  if (!dashboard) {
    return <p className="drv-muted">Laden…</p>
  }

  const trip = dashboard.currentTrip

  return (
    <div>
      {fromCache && (
        <p className="drv-muted" role="status">Offline kopie — wordt ververst zodra er verbinding is.</p>
      )}

      {online && <DriverActivityCard />}

      <section className="drv-card">
        <h2>Huidige rit</h2>
        {trip ? (
          <>
            <div className="drv-fact-row">
              <strong>{trip.tripNumber}</strong>
              <Badge tone={TRIP_STATUS_TONE[trip.status]}>{TRIP_STATUS_LABELS[trip.status]}</Badge>
            </div>
            <div className="drv-fact-row">
              <span>Voertuig</span>
              <span>{trip.vehicleNumber ?? '—'}{trip.trailerNumber ? ` + ${trip.trailerNumber}` : ''}</span>
            </div>
            <div className="drv-fact-row">
              <span>Voortgang</span>
              <span>{trip.completedStopCount}/{trip.stopCount} stops</span>
            </div>
            {dashboard.nextStopCity && (
              <div className="drv-fact-row">
                <span>Volgende stop</span>
                <span>{dashboard.nextStopLocationName ?? dashboard.nextStopCity}</span>
              </div>
            )}
            {dashboard.nextStopEta && (
              <div className="drv-fact-row">
                <span>ETA</span>
                <span>
                  {new Date(dashboard.nextStopEta).toLocaleTimeString('nl-BE', { hour: '2-digit', minute: '2-digit' })}
                  {dashboard.nextStopEtaSource && ` (${ETA_SOURCE_LABELS[dashboard.nextStopEtaSource]})`}
                </span>
              </div>
            )}
            <Link className="drv-big-link" to={`/my-trips/${trip.id}`}>Rit openen</Link>
          </>
        ) : (
          <p className="drv-muted">Geen actieve rit. {dashboard.nextTrip
            ? `Volgende: ${dashboard.nextTrip.tripNumber} op ${formatDate(dashboard.nextTrip.tripDate)}.`
            : 'Er staat niets gepland.'}</p>
        )}
      </section>

      <section className="drv-card">
        <h2>Aandachtspunten</h2>
        <div className="drv-fact-row">
          <span>Openstaande stops</span>
          <strong>{dashboard.openStopCount}</strong>
        </div>
        <div className="drv-fact-row">
          <span>Onopgeloste afwijkingen</span>
          <strong>{dashboard.unresolvedExceptionCount}</strong>
        </div>
        <div className="drv-fact-row">
          <span>Actieve incidenten</span>
          <strong>{dashboard.activeIncidentCount}</strong>
        </div>
        <div className="drv-fact-row">
          <span>Ritten vandaag</span>
          <strong>{dashboard.todayTripCount}</strong>
        </div>
      </section>

      {dashboard.nextTrip && trip && (
        <section className="drv-card">
          <h2>Volgende rit</h2>
          <div className="drv-fact-row">
            <strong>{dashboard.nextTrip.tripNumber}</strong>
            <span>{formatDate(dashboard.nextTrip.tripDate)}</span>
          </div>
        </section>
      )}
    </div>
  )
}
