import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { useAuth } from '../../auth/authContextValue'
import { useOnlineStatus } from '../../../hooks/useOnlineStatus'
import { useLocale } from '../../../i18n/localeContext'
import { TRIP_STATUS_TONE, type TripStatus } from '../../planning/types'
import type { EtaSource } from '../../operations/types'
import { getMyDashboard } from '../api/driverApi'
import { DriverActivityCard } from '../../time-attendance/components/DriverActivityCard'
import type { MyDashboard } from '../types'
import { formatDate } from '../../../utils/dates'

const SNAPSHOT_PREFIX = 'ts.driverSnapshot.v1'

/** Translation keys per status/source code; resolved via t() at render time. */
const TRIP_STATUS_KEYS: Record<TripStatus, string> = {
  Draft: 'driverApp.tripStatus.Draft',
  Planned: 'driverApp.tripStatus.Planned',
  InProgress: 'driverApp.tripStatus.InProgress',
  Completed: 'driverApp.tripStatus.Completed',
  Cancelled: 'driverApp.tripStatus.Cancelled',
}

const ETA_SOURCE_KEYS: Record<EtaSource, string> = {
  Heuristic: 'driverApp.etaSource.Heuristic',
  Provider: 'driverApp.etaSource.Provider',
  DispatcherOverride: 'driverApp.etaSource.DispatcherOverride',
}

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
  const { t } = useLocale()
  const [dashboard, setDashboard] = useState<MyDashboard | null>(null)
  const [fromCache, setFromCache] = useState(false)
  const [loadError, setLoadError] = useState(false)

  useEffect(() => {
    if (!user) return
    let cancelled = false
    getMyDashboard()
      .then((data) => {
        if (cancelled) return
        setDashboard(data)
        setFromCache(false)
        setLoadError(false)
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
          setLoadError(true)
        }
      })
    return () => {
      cancelled = true
    }
  }, [user, online])

  if (loadError) {
    return <p className="drv-muted">{t('driverApp.home.loadFailedNoCache')}</p>
  }

  if (!dashboard) {
    return <p className="drv-muted">{t('driverApp.home.loading')}</p>
  }

  const trip = dashboard.currentTrip

  return (
    <div>
      {fromCache && (
        <p className="drv-muted" role="status">{t('driverApp.home.cachedNotice')}</p>
      )}

      {online && <DriverActivityCard />}

      <section className="drv-card">
        <h2>{t('driverApp.home.currentTrip')}</h2>
        {trip ? (
          <>
            <div className="drv-fact-row">
              <strong>{trip.tripNumber}</strong>
              <Badge tone={TRIP_STATUS_TONE[trip.status]}>{t(TRIP_STATUS_KEYS[trip.status])}</Badge>
            </div>
            <div className="drv-fact-row">
              <span>{t('driverApp.home.vehicle')}</span>
              <span>{trip.vehicleNumber ?? '—'}{trip.trailerNumber ? ` + ${trip.trailerNumber}` : ''}</span>
            </div>
            <div className="drv-fact-row">
              <span>{t('driverApp.home.progress')}</span>
              <span>{t('driverApp.home.progressStops', { completed: trip.completedStopCount, total: trip.stopCount })}</span>
            </div>
            {dashboard.nextStopCity && (
              <div className="drv-fact-row">
                <span>{t('driverApp.home.nextStop')}</span>
                <span>{dashboard.nextStopLocationName ?? dashboard.nextStopCity}</span>
              </div>
            )}
            {dashboard.nextStopEta && (
              <div className="drv-fact-row">
                <span>{t('driverApp.home.eta')}</span>
                <span>
                  {new Date(dashboard.nextStopEta).toLocaleTimeString('nl-BE', { hour: '2-digit', minute: '2-digit' })}
                  {dashboard.nextStopEtaSource && ` (${t(ETA_SOURCE_KEYS[dashboard.nextStopEtaSource])})`}
                </span>
              </div>
            )}
            <Link className="drv-big-link" to={`/my-trips/${trip.id}`}>{t('driverApp.home.openTrip')}</Link>
          </>
        ) : (
          <p className="drv-muted">{t('driverApp.home.noActiveTrip')} {dashboard.nextTrip
            ? t('driverApp.home.nextTripAt', { tripNumber: dashboard.nextTrip.tripNumber, date: formatDate(dashboard.nextTrip.tripDate) })
            : t('driverApp.home.nothingPlanned')}</p>
        )}
      </section>

      <section className="drv-card">
        <h2>{t('driverApp.home.attentionTitle')}</h2>
        <div className="drv-fact-row">
          <span>{t('driverApp.home.openStops')}</span>
          <strong>{dashboard.openStopCount}</strong>
        </div>
        <div className="drv-fact-row">
          <span>{t('driverApp.home.unresolvedExceptions')}</span>
          <strong>{dashboard.unresolvedExceptionCount}</strong>
        </div>
        <div className="drv-fact-row">
          <span>{t('driverApp.home.activeIncidents')}</span>
          <strong>{dashboard.activeIncidentCount}</strong>
        </div>
        <div className="drv-fact-row">
          <span>{t('driverApp.home.tripsToday')}</span>
          <strong>{dashboard.todayTripCount}</strong>
        </div>
      </section>

      {dashboard.nextTrip && trip && (
        <section className="drv-card">
          <h2>{t('driverApp.home.nextTrip')}</h2>
          <div className="drv-fact-row">
            <strong>{dashboard.nextTrip.tripNumber}</strong>
            <span>{formatDate(dashboard.nextTrip.tripDate)}</span>
          </div>
        </section>
      )}
    </div>
  )
}
