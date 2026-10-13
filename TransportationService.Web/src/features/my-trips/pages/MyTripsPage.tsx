import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE } from '../../planning/types'
import { listMyTrips } from '../api/myTripsApi'
import type { MyTrip } from '../types'
import './my-trips.css'

/** Driver view: own upcoming trips (planned + in progress + recently completed). */
export function MyTripsPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const [trips, setTrips] = useState<MyTrip[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    listMyTrips()
      .then((data) => {
        if (!mounted) return
        setTrips(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('myTrips.list.loadError'))
      })
    return () => {
      mounted = false
    }
  }, [t])

  return (
    <div>
      <Breadcrumbs items={[{ label: t('myTrips.list.title') }]} />
      <PageHeader title={t('myTrips.list.title')} subtitle={t('myTrips.list.subtitle')} />

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && trips === null && <p className="placeholder-text">{t('myTrips.list.loading')}</p>}
      {!loadError && trips !== null && trips.length === 0 && (
        <p className="placeholder-text">{t('myTrips.list.empty')}</p>
      )}

      {!loadError && trips !== null && trips.length > 0 && (
        <div className="mt-cards">
          {trips.map((trip) => (
            <button key={trip.id} type="button" className="mt-card" onClick={() => navigate(`/my-trips/${trip.id}`)}>
              <div className="mt-card-head">
                <code>{trip.tripNumber}</code>
                <Badge tone={TRIP_STATUS_TONE[trip.status]}>{t(TRIP_STATUS_LABELS[trip.status])}</Badge>
              </div>
              <div className="mt-card-date">{trip.tripDate}</div>
              <div className="mt-card-meta">
                {trip.vehicleNumber ? `${trip.vehicleNumber} (${trip.vehicleLicensePlate})` : t('myTrips.list.noVehicle')}
                {trip.trailerNumber ? ` + ${trip.trailerNumber}` : ''}
              </div>
              <div className="mt-card-progress">
                {t('myTrips.list.orders', { count: trip.orderCount })} ·{' '}
                {t('myTrips.list.stopsHandled', { completed: trip.completedStopCount, total: trip.stopCount })}
              </div>
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
